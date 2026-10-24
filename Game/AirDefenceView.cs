using System.Collections.Generic;
using Godot;
using WarOfResilience.Sim.Units;

namespace WarOfResilience.Game;

/// <summary>
/// Draws air defence: the range ring of each launcher the player can see, missiles in flight with
/// a smoke trail, and a puff where a missile missed (a hit shows as the aircraft's explosion in
/// <see cref="UnitView"/>). Only reads from Sim.
/// </summary>
public partial class AirDefenceView : Node3D
{
	private const int RingSegments = 96;

	/// <summary>Rings and trails sit this far above the ground so hills do not hide them.</summary>
	private const float RingLift = 40f;

	private AirDefence _airDefence = null!;
	private UnitManager _units = null!;
	private Vision _vision = null!;
	private TerrainView _terrain = null!;
	private RtsCamera _camera = null!;
	private int _playerId;

	private ImmediateMesh _rings = null!;
	private ImmediateMesh _trails = null!;
	private StandardMaterial3D _lineMaterial = null!;
	private StandardMaterial3D _missileMaterial = null!;
	private readonly Dictionary<int, MissileVisual> _missiles = new();
	private readonly HashSet<int> _shown = new();

	public void Setup(AirDefence airDefence, UnitManager units, Vision vision, TerrainView terrain, RtsCamera camera, int playerId)
	{
		_airDefence = airDefence;
		_units = units;
		_vision = vision;
		_terrain = terrain;
		_camera = camera;
		_playerId = playerId;

		_lineMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			VertexColorUseAsAlbedo = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		_missileMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.95f, 0.8f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};

		_rings = new ImmediateMesh();
		AddChild(new MeshInstance3D { Name = "Rings", Mesh = _rings, MaterialOverride = _lineMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		_trails = new ImmediateMesh();
		AddChild(new MeshInstance3D { Name = "Trails", Mesh = _trails, MaterialOverride = _lineMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
	}

	public override void _Process(double delta)
	{
		if (_airDefence == null)
		{
			return;
		}

		DrawRings();
		ShowMisses();
		UpdateMissiles((float)delta);
	}

	private void DrawRings()
	{
		_rings.ClearSurfaces();
		bool any = false;
		foreach (var unit in _units.Units)
		{
			if (unit.Type.AirDefence is not { } stats || !unit.IsAlive || !_vision.CanSee(_playerId, unit))
			{
				continue;
			}

			if (!any)
			{
				_rings.SurfaceBegin(Mesh.PrimitiveType.Lines);
				any = true;
			}

			var color = WorldView.ColorOf(unit.OwnerId).Lightened(0.3f);
			color.A = _airDefence.AmmoOf(unit) > 0 ? 0.85f : 0.3f;
			float cx = (float)unit.X.ToDouble();
			float cz = (float)unit.Z.ToDouble();
			Vector3 Point(int i)
			{
				float angle = Mathf.Tau * i / RingSegments;
				float x = cx + Mathf.Cos(angle) * stats.RangeMetres;
				float z = cz + Mathf.Sin(angle) * stats.RangeMetres;
				return new Vector3(x, Mathf.Max(_terrain.HeightAt(x, z), _terrain.SeaLevel) + RingLift, z);
			}

			var previous = Point(0);
			for (int i = 1; i <= RingSegments; i++)
			{
				var next = Point(i);
				_rings.SurfaceSetColor(color);
				_rings.SurfaceAddVertex(previous);
				_rings.SurfaceSetColor(color);
				_rings.SurfaceAddVertex(next);
				previous = next;
			}
		}

		if (any)
		{
			_rings.SurfaceEnd();
		}
	}

	private void UpdateMissiles(float delta)
	{
		float size = Mathf.Max(6f, _camera.Distance * 0.006f);
		var alive = new HashSet<int>();
		foreach (var missile in _airDefence.Missiles)
		{
			if (!Visible(missile))
			{
				continue;
			}

			alive.Add(missile.Id);
			var position = new Vector3((float)missile.X.ToDouble(), (float)missile.Y.ToDouble(), (float)missile.Z.ToDouble());
			if (!_missiles.TryGetValue(missile.Id, out var visual))
			{
				var mesh = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.5f, Height = 1f }, MaterialOverride = _missileMaterial, Position = position };
				AddChild(mesh);
				visual = new MissileVisual(mesh);
				visual.Trail.Add(position);
				_missiles.Add(missile.Id, visual);
			}

			// Ease towards the simulated position so the missile does not jump once per tick.
			visual.Mesh.Position = visual.Mesh.Position.Lerp(position, 1 - Mathf.Exp(-25 * delta));
			visual.Mesh.Scale = new Vector3(size, size, size);
			visual.Trail.Add(visual.Mesh.Position);
		}

		foreach (var id in new List<int>(_missiles.Keys))
		{
			if (!alive.Contains(id))
			{
				_missiles[id].Mesh.QueueFree();
				_missiles.Remove(id);
			}
		}

		_trails.ClearSurfaces();
		if (_missiles.Count == 0)
		{
			return;
		}

		_trails.SurfaceBegin(Mesh.PrimitiveType.Lines);
		foreach (var visual in _missiles.Values)
		{
			for (int i = 1; i < visual.Trail.Count; i++)
			{
				float fade = (float)i / visual.Trail.Count;
				var color = new Color(0.9f, 0.9f, 0.9f, 0.25f + 0.6f * fade);
				_trails.SurfaceSetColor(color);
				_trails.SurfaceAddVertex(visual.Trail[i - 1]);
				_trails.SurfaceSetColor(color);
				_trails.SurfaceAddVertex(visual.Trail[i]);
			}
		}

		_trails.SurfaceEnd();
	}

	/// <summary>A grey puff where a missile missed or blew itself up.</summary>
	private void ShowMisses()
	{
		foreach (var interception in _airDefence.Recent)
		{
			// Only missiles the player saw in flight.
			if (interception.Hit || !_missiles.TryGetValue(interception.MissileId, out var visual) || !_shown.Add(interception.MissileId))
			{
				continue;
			}

			AddChild(new Explosion(Mathf.Max(15f, _camera.Distance * 0.008f)) { Position = visual.Mesh.Position });
		}
	}

	/// <summary>The player sees their own missiles, missiles fired at their units and launchers in sight.</summary>
	private bool Visible(Missile missile) =>
		missile.OwnerId == _playerId
		|| _units.Units[missile.TargetId].OwnerId == _playerId
		|| _vision.CanSee(_playerId, _units.Units[missile.LauncherId]);

	private sealed class MissileVisual
	{
		public MissileVisual(MeshInstance3D mesh)
		{
			Mesh = mesh;
		}

		public MeshInstance3D Mesh { get; }

		public List<Vector3> Trail { get; } = new();
	}
}
