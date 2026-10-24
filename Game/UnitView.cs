using System.Collections.Generic;
using Godot;
using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Units;

namespace WarOfResilience.Game;

/// <summary>
/// Draws units: small real-scale models up close and country-coloured markers that grow with the
/// camera distance, so units stay visible on the whole map. Positions are interpolated between
/// simulation ticks for smooth movement. Only reads from Sim.
/// </summary>
public partial class UnitView : Node3D
{
	/// <summary>Marker size as a fraction of the camera distance.</summary>
	private const float MarkerScale = 0.014f;

	/// <summary>Models are drawn up to this camera distance.</summary>
	private const float ModelsVisibleUpTo = 20_000f;

	private static readonly Color SelectedColor = new(0.3f, 1f, 0.4f);

	private readonly List<UnitVisual> _visuals = new();
	private readonly HashSet<int> _selected = new();
	private UnitManager? _units;
	private Vision? _vision;
	private int _playerId;
	private RtsCamera? _camera;
	private float _alpha = 1;

	/// <param name="vision">Enemy units are only drawn while <paramref name="playerId"/> can see them.</param>
	public void Build(UnitManager units, Vision vision, int playerId, RtsCamera camera)
	{
		_units = units;
		_vision = vision;
		_playerId = playerId;
		_camera = camera;
		foreach (var unit in units.Units)
		{
			AddVisual(unit);
		}

		CapturePrevious();
	}

	/// <summary>Screen-independent world position of a unit as currently drawn.</summary>
	public Vector3 DrawnPosition(int unitId) => _visuals[unitId].Root.Position;

	public IReadOnlyCollection<int> Selected => _selected;

	public void SetSelected(IEnumerable<int> unitIds)
	{
		_selected.Clear();
		_selected.UnionWith(unitIds);
		foreach (var visual in _visuals)
		{
			visual.Ring.Visible = _selected.Contains(visual.Unit.Id);
		}
	}

	/// <summary>Call right before each simulation tick so drawing can blend from the old positions.</summary>
	public void CapturePrevious()
	{
		foreach (var visual in _visuals)
		{
			visual.Previous = SimPosition(visual.Unit);
		}
	}

	/// <summary>How far between the last tick and the next one we are, 0 to 1.</summary>
	public void SetInterpolation(float alpha) => _alpha = Mathf.Clamp(alpha, 0, 1);

	public override void _Process(double delta)
	{
		if (_units == null || _camera == null)
		{
			return;
		}

		// Units built during the game.
		while (_visuals.Count < _units.Units.Count)
		{
			var unit = _units.Units[_visuals.Count];
			AddVisual(unit);
			_visuals[^1].Previous = SimPosition(unit);
		}

		float distance = _camera.Distance;
		float markerSize = distance * MarkerScale;
		bool showMarkers = distance > 1_500f;

		foreach (var visual in _visuals)
		{
			var unit = visual.Unit;
			if (!unit.IsAlive)
			{
				if (visual.Root.Visible)
				{
					// Just died: a kamikaze hit or being hit. Blow up where it was last drawn.
					visual.Root.Visible = false;
					visual.Ring.Visible = false;
					_selected.Remove(unit.Id);
					var explosion = new Explosion(Mathf.Max(40f, distance * 0.02f)) { Position = visual.Root.Position };
					AddChild(explosion);
				}

				continue;
			}

			visual.Root.Visible = _vision == null || _vision.CanSee(_playerId, unit);
			visual.Root.Position = visual.Previous.Lerp(SimPosition(unit), _alpha);
			if (unit.FacingX != Fixed.Zero || unit.FacingZ != Fixed.Zero)
			{
				float heading = Mathf.Atan2(-(float)unit.FacingX.ToDouble(), -(float)unit.FacingZ.ToDouble());
				visual.Model.Rotation = new Vector3(0, heading, 0);
			}

			visual.Marker.Visible = showMarkers;
			visual.Marker.Scale = new Vector3(markerSize, markerSize * 0.35f, markerSize);
			visual.Marker.Position = new Vector3(0, markerSize * 0.3f, 0);

			float ring = Mathf.Max(markerSize * 0.9f, 12f);
			visual.Ring.Scale = new Vector3(ring, ring, ring);
		}
	}

	private static Vector3 SimPosition(Unit unit) =>
		new((float)unit.X.ToDouble(), (float)unit.Y.ToDouble(), (float)unit.Z.ToDouble());

	private void AddVisual(Unit unit)
	{
		var root = new Node3D { Name = $"Unit_{unit.Id}_{unit.Type.Id}", Position = SimPosition(unit) };
		AddChild(root);

		var model = new Node3D { Name = "Model" };
		root.AddChild(model);
		switch (unit.Type.Id)
		{
			case "jeep":
				BuildJeep(model, unit.OwnerId);
				break;
			case "kamikaze_drone":
				BuildKamikazeDrone(model);
				break;
			case "air_defence":
				BuildAirDefence(model, unit.OwnerId);
				break;
			default:
				BuildReconDrone(model);
				break;
		}

		var color = WorldView.ColorOf(unit.OwnerId);
		var marker = new MeshInstance3D
		{
			Name = "Marker",
			Mesh = unit.Type.Domain == UnitDomain.Air
				? new PrismMesh { Size = new Vector3(1, 1, 1) }
				: new BoxMesh { Size = new Vector3(0.8f, 1, 0.8f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = unit.Type.Domain == UnitDomain.Air ? color.Lightened(0.35f) : color,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
		};
		root.AddChild(marker);

		var ring = new MeshInstance3D
		{
			Name = "SelectionRing",
			Mesh = new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f, Rings = 32, RingSegments = 4 },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = SelectedColor,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				NoDepthTest = true,
			},
			Visible = false,
		};
		root.AddChild(ring);

		_visuals.Add(new UnitVisual(unit, root, model, marker, ring));
	}

	private static void BuildJeep(Node3D model, int ownerId)
	{
		var olive = Material(new Color(0.33f, 0.36f, 0.24f));
		var dark = Material(new Color(0.1f, 0.1f, 0.1f));
		AddPart(model, new BoxMesh { Size = new Vector3(1.9f, 0.9f, 4.4f) }, olive, new Vector3(0, 0.85f, 0));
		AddPart(model, new BoxMesh { Size = new Vector3(1.8f, 0.7f, 1.9f) }, olive, new Vector3(0, 1.6f, 0.4f));
		AddPart(model, new BoxMesh { Size = new Vector3(1.6f, 0.5f, 0.05f) }, Material(new Color(0.4f, 0.5f, 0.6f)), new Vector3(0, 1.65f, -0.56f));
		foreach (float x in new[] { -0.95f, 0.95f })
		{
			foreach (float z in new[] { -1.4f, 1.4f })
			{
				AddPart(model, new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.4f, Height = 0.3f }, dark, new Vector3(x, 0.4f, z),
					new Vector3(0, 0, Mathf.Pi / 2));
			}
		}

		// Country colour on the roof so units can be told apart up close.
		AddPart(model, new BoxMesh { Size = new Vector3(1.2f, 0.05f, 1.2f) }, Material(WorldView.ColorOf(ownerId)), new Vector3(0, 1.98f, 0.4f));
	}

	private static void BuildReconDrone(Node3D model)
	{
		var grey = Material(new Color(0.55f, 0.57f, 0.58f));
		AddPart(model, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.2f, Height = 2.6f }, grey, Vector3.Zero, new Vector3(Mathf.Pi / 2, 0, 0));
		AddPart(model, new BoxMesh { Size = new Vector3(4.8f, 0.06f, 0.45f) }, grey, new Vector3(0, 0.05f, -0.1f));
		AddPart(model, new BoxMesh { Size = new Vector3(1.4f, 0.05f, 0.3f) }, grey, new Vector3(0, 0.05f, 1.2f));
		AddPart(model, new BoxMesh { Size = new Vector3(0.05f, 0.5f, 0.3f) }, grey, new Vector3(0, 0.3f, 1.2f));
	}

	private static void BuildKamikazeDrone(Node3D model)
	{
		var sand = Material(new Color(0.62f, 0.58f, 0.48f));
		AddPart(model, new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 3.5f }, sand, Vector3.Zero, new Vector3(Mathf.Pi / 2, 0, 0));

		// Delta wing: a flat prism pointing forward.
		AddPart(model, new PrismMesh { Size = new Vector3(2.5f, 2.2f, 0.08f) }, sand, new Vector3(0, 0, 0.5f), new Vector3(-Mathf.Pi / 2, 0, 0));
		AddPart(model, new BoxMesh { Size = new Vector3(0.05f, 0.6f, 0.4f) }, sand, new Vector3(1.2f, 0.25f, 1.4f));
		AddPart(model, new BoxMesh { Size = new Vector3(0.05f, 0.6f, 0.4f) }, sand, new Vector3(-1.2f, 0.25f, 1.4f));
	}

	/// <summary>A truck carrying a raised box of four missile tubes, plus a radar panel.</summary>
	private static void BuildAirDefence(Node3D model, int ownerId)
	{
		var olive = Material(new Color(0.3f, 0.34f, 0.22f));
		var dark = Material(new Color(0.1f, 0.1f, 0.1f));
		AddPart(model, new BoxMesh { Size = new Vector3(2.5f, 1.0f, 8f) }, olive, new Vector3(0, 1.2f, 0));
		AddPart(model, new BoxMesh { Size = new Vector3(2.4f, 1.3f, 1.8f) }, olive, new Vector3(0, 2.3f, -3f));
		AddPart(model, new BoxMesh { Size = new Vector3(2.2f, 0.6f, 0.05f) }, Material(new Color(0.4f, 0.5f, 0.6f)), new Vector3(0, 2.5f, -3.92f));
		for (int i = 0; i < 4; i++)
		{
			float z = -2.8f + i * 1.9f;
			foreach (float x in new[] { -1.2f, 1.2f })
			{
				AddPart(model, new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.55f, Height = 0.4f }, dark, new Vector3(x, 0.55f, z),
					new Vector3(0, 0, Mathf.Pi / 2));
			}
		}

		// Launcher raised towards the sky, facing backwards.
		var launcher = new Node3D { Position = new Vector3(0, 1.9f, 1.2f), Rotation = new Vector3(Mathf.DegToRad(-40), 0, 0) };
		model.AddChild(launcher);
		AddPart(launcher, new BoxMesh { Size = new Vector3(2.2f, 1.6f, 5.5f) }, olive, new Vector3(0, 0.8f, 1.8f));
		foreach (float x in new[] { -0.5f, 0.5f })
		{
			foreach (float y in new[] { 0.4f, 1.2f })
			{
				AddPart(launcher, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.05f }, dark, new Vector3(x, y, 4.56f),
					new Vector3(Mathf.Pi / 2, 0, 0));
			}
		}

		AddPart(model, new BoxMesh { Size = new Vector3(1.6f, 1.2f, 0.1f) }, Material(new Color(0.25f, 0.27f, 0.2f)), new Vector3(0, 3.6f, -2.4f));
		AddPart(model, new BoxMesh { Size = new Vector3(1.2f, 0.05f, 1.2f) }, Material(WorldView.ColorOf(ownerId)), new Vector3(0, 2.98f, -3f));
	}

	private static void AddPart(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3 rotation = default)
	{
		parent.AddChild(new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = material,
			Position = position,
			Rotation = rotation,
			VisibilityRangeEnd = ModelsVisibleUpTo,
		});
	}

	private static StandardMaterial3D Material(Color color) => new() { AlbedoColor = color, Roughness = 0.7f };

	private sealed class UnitVisual
	{
		public UnitVisual(Unit unit, Node3D root, Node3D model, MeshInstance3D marker, MeshInstance3D ring)
		{
			Unit = unit;
			Root = root;
			Model = model;
			Marker = marker;
			Ring = ring;
		}

		public Unit Unit { get; }

		public Node3D Root { get; }

		public Node3D Model { get; }

		public MeshInstance3D Marker { get; }

		public MeshInstance3D Ring { get; }

		public Vector3 Previous { get; set; }
	}
}
