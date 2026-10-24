using System.Collections.Generic;
using System.Linq;
using Godot;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.Units;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Game;

/// <summary>
/// Draws the strategic map and turns clicks on it into orders. Lives inside <see cref="StrategicMap"/>.
/// </summary>
public partial class StrategicMapCanvas : Control
{
	/// <summary>Background image resolution along the longer side.</summary>
	private const int ImageSize = 600;

	/// <summary>A click this close to a symbol, in pixels, picks it.</summary>
	private const float PickRadius = 9f;

	private static readonly Color Energized = new(1f, 0.82f, 0.25f);
	private static readonly Color Dead = new(0.45f, 0.45f, 0.45f);
	private static readonly Color Cut = new(0.8f, 0.15f, 0.1f);
	private static readonly Color SelectedColor = new(0.3f, 1f, 0.4f);

	private StrategicMap _map = null!;
	private WorldState _world = null!;
	private PowerGrid _grid = null!;
	private UnitManager _units = null!;
	private AirDefence _airDefence = null!;
	private Vision _vision = null!;
	private RtsCamera _camera = null!;
	private int _playerId;
	private Vector2 _worldSize;
	private ImageTexture _background = null!;

	private Vector2? _dragStart;
	private Vector2 _mouse;

	public void Setup(StrategicMap map, Terrain terrain, WorldState world, PowerGrid grid, UnitManager units, AirDefence airDefence, Vision vision,
		RtsCamera camera, int playerId)
	{
		_map = map;
		_world = world;
		_grid = grid;
		_units = units;
		_airDefence = airDefence;
		_vision = vision;
		_camera = camera;
		_playerId = playerId;
		_worldSize = new Vector2((float)terrain.SizeX.ToDouble(), (float)terrain.SizeZ.ToDouble());
		_background = BuildBackground(terrain, world.Territory);
		MouseFilter = MouseFilterEnum.Stop;
		ClipContents = true;
	}

	/// <summary>The square area the map is drawn in, centred in this control.</summary>
	private Rect2 MapRect
	{
		get
		{
			float scale = Mathf.Min(Size.X / _worldSize.X, Size.Y / _worldSize.Y);
			var size = _worldSize * scale;
			return new Rect2((Size - size) / 2, size);
		}
	}

	private Vector2 ToMap(float x, float z)
	{
		var rect = MapRect;
		return rect.Position + new Vector2(x / _worldSize.X, z / _worldSize.Y) * rect.Size;
	}

	private Vector2 ToMap(Sim.Core.Fixed x, Sim.Core.Fixed z) => ToMap((float)x.ToDouble(), (float)z.ToDouble());

	private Vector2 ToWorld(Vector2 map)
	{
		var rect = MapRect;
		var t = (map - rect.Position) / rect.Size;
		return new Vector2(Mathf.Clamp(t.X, 0, 1) * _worldSize.X, Mathf.Clamp(t.Y, 0, 1) * _worldSize.Y);
	}

	public override void _Draw()
	{
		if (_world == null)
		{
			return;
		}

		var font = ThemeDB.FallbackFont;
		DrawTextureRect(_background, MapRect, false);

		// Power lines.
		foreach (var line in _grid.Lines)
		{
			var a = _grid.Nodes[line.FromNodeId];
			var b = _grid.Nodes[line.ToNodeId];
			var color = !line.IsOperational ? Cut : line.IsEnergized ? Energized : Dead;
			DrawLine(ToMap(a.X, a.Z), ToMap(b.X, b.Z), color, line.IsOperational ? 1.5f : 2.5f);
		}

		// Cities.
		foreach (var city in _world.Cities)
		{
			var p = ToMap(city.X, city.Z);
			float r = 4 + Mathf.Sqrt(city.PopulationThousands) / 8;
			DrawCircle(p, r, WorldView.ColorOf(city.CountryId));
			DrawArc(p, r, 0, Mathf.Tau, 24, Colors.White, 1.5f);
			string name = city.PowerPercent switch { 100 => city.Name, 0 => $"{city.Name} (blackout)", var x => $"{city.Name} ({x}% power)" };
			var color = city.PowerPercent == 100 ? Colors.White : new Color(1f, 0.55f, 0.4f);
			// Near the right edge the name goes on the left of the dot.
			float width = font.GetStringSize(name, HorizontalAlignment.Left, -1, 14).X;
			var at = p.X + r + 4 + width > MapRect.End.X ? p + new Vector2(-r - 4 - width, 5) : p + new Vector2(r + 4, 5);
			DrawString(font, at, name, HorizontalAlignment.Left, -1, 14, color);
		}

		// Plants and substations.
		foreach (var node in _grid.Nodes)
		{
			var p = ToMap(node.X, node.Z);
			var color = WorldView.ColorOf(node.CountryId);
			if (node is PowerPlant plant)
			{
				DrawCircle(p, 7, new Color(0.1f, 0.1f, 0.1f));
				DrawCircle(p, 6, node.IsOperational ? Energized : Dead);
				DrawArc(p, 7, 0, Mathf.Tau, 20, color, 2f);
				string letter = plant.Kind.ToString()[..1];
				DrawString(font, p + new Vector2(-4, 4), letter, HorizontalAlignment.Left, -1, 11, Colors.Black);
			}
			else
			{
				var rect = new Rect2(p - new Vector2(3.5f, 3.5f), new Vector2(7, 7));
				DrawRect(rect, node.IsOperational ? color.Lightened(0.4f) : Dead);
				DrawRect(rect, Colors.Black, filled: false, width: 1);
			}

			if (!node.IsOperational)
			{
				DrawLine(p + new Vector2(-6, -6), p + new Vector2(6, 6), Cut, 2.5f);
				DrawLine(p + new Vector2(-6, 6), p + new Vector2(6, -6), Cut, 2.5f);
			}
		}

		// Air defence coverage of launchers the player can see; faint when out of missiles.
		foreach (var unit in _units.Units)
		{
			if (unit.Type.AirDefence is not { } stats || !unit.IsAlive || !_vision.CanSee(_playerId, unit))
			{
				continue;
			}

			var centre = ToMap(unit.X, unit.Z);
			float radius = stats.RangeMetres / _worldSize.X * MapRect.Size.X;
			var color = WorldView.ColorOf(unit.OwnerId).Lightened(0.3f);
			color.A = _airDefence.AmmoOf(unit) > 0 ? 0.22f : 0.08f;
			DrawCircle(centre, radius, color);
			color.A = 0.9f;
			DrawArc(centre, radius, 0, Mathf.Tau, 48, color, 1.5f);
		}

		// Enemy units last seen in the fog: hollow, with how long ago (one label per group).
		var labelled = new List<Vector2>();
		foreach (var (id, sighting) in _vision.LastSeenBy(_playerId))
		{
			var unit = _units.Units[id];
			if (_vision.CanSee(_playerId, unit))
			{
				continue;
			}

			var p = ToMap(sighting.X, sighting.Z);
			DrawArc(p, 5, 0, Mathf.Tau, 12, WorldView.ColorOf(unit.OwnerId), 1.5f);
			if (labelled.Any(l => l.DistanceTo(p) < 24))
			{
				continue;
			}

			labelled.Add(p);
			DrawString(font, p + new Vector2(7, 4), $"? {_map.Ago(sighting.Tick)}", HorizontalAlignment.Left, -1, 11,
				new Color(0.85f, 0.85f, 0.85f));
		}

		// Units the player can see: their own and enemies in sight.
		var selected = _map.Selected;
		foreach (var unit in _units.Units)
		{
			if (!unit.IsAlive || !_vision.CanSee(_playerId, unit))
			{
				continue;
			}

			var p = ToMap(unit.X, unit.Z);
			var color = WorldView.ColorOf(unit.OwnerId);
			if (unit.Type.Domain == UnitDomain.Air)
			{
				DrawColoredPolygon(new[] { p + new Vector2(0, -5), p + new Vector2(4.5f, 4), p + new Vector2(-4.5f, 4) }, color);
			}
			else
			{
				DrawRect(new Rect2(p - new Vector2(3.5f, 3.5f), new Vector2(7, 7)), color);
			}

			if (selected.Contains(unit.Id))
			{
				DrawArc(p, 7, 0, Mathf.Tau, 16, SelectedColor, 1.5f);
			}

			// Line to where a unit is going.
			if (unit.OwnerId == _playerId && unit.Destination is { } d)
			{
				DrawDashedLine(p, ToMap(d.X, d.Z), unit.Target != null ? Cut : SelectedColor, 1f, 4f);
			}
		}

		DrawCameraView();

		if (_dragStart is { } start)
		{
			var box = new Rect2(start, _mouse - start).Abs();
			DrawRect(box, new Color(0.3f, 1f, 0.4f, 0.12f));
			DrawRect(box, SelectedColor, filled: false, width: 1.5f);
		}
		else if (_map.Selected.Count > 0 && TargetAt(_mouse) is { } target)
		{
			DrawString(font, _mouse + new Vector2(14, -8), $"Attack {target.Name}", HorizontalAlignment.Left, -1, 15, new Color(1f, 0.6f, 0.5f));
		}
	}

	/// <summary>Outline of what the 3D camera shows.</summary>
	private void DrawCameraView()
	{
		var size = _camera.GetViewport().GetVisibleRect().Size;
		var corners = new[] { Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y) };
		var points = new List<Vector2>();
		foreach (var corner in corners)
		{
			if (_camera.GroundPointUnder(corner) is { } ground)
			{
				points.Add(ToMap(ground.X, ground.Z));
			}
		}

		if (points.Count == 4)
		{
			// Far zoomed out the top corners land beyond the map; keep the outline on it.
			var rect = MapRect;
			for (int i = 0; i < points.Count; i++)
			{
				points[i] = points[i].Clamp(rect.Position, rect.End);
			}

			points.Add(points[0]);
			DrawPolyline(points.ToArray(), new Color(1, 1, 1, 0.8f), 1.5f);
		}
	}

	public override void _GuiInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseMotion motion:
				_mouse = motion.Position;
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
				_dragStart = press.Position;
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release when _dragStart is { } start:
				_dragStart = null;
				if (start.DistanceTo(release.Position) > 6)
				{
					var box = new Rect2(start, release.Position - start).Abs();
					_map.Select(_units.Units.Where(u => u.OwnerId == _playerId && u.IsAlive && box.HasPoint(ToMap(u.X, u.Z))).Select(u => u.Id),
						add: release.ShiftPressed);
				}
				else if (OwnUnitAt(release.Position) is { } unit)
				{
					_map.Select(new[] { unit.Id }, add: release.ShiftPressed);
				}
				else
				{
					_map.LookAt(ToWorld(release.Position));
				}

				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } click:
				if (TargetAt(click.Position) is { } target)
				{
					_map.Attack(target.Target);
				}
				else
				{
					_map.MoveTo(ToWorld(click.Position));
				}

				break;
		}

		AcceptEvent();
	}

	private Unit? OwnUnitAt(Vector2 position) => _units.Units
		.Where(u => u.OwnerId == _playerId && u.IsAlive)
		.OrderBy(u => ToMap(u.X, u.Z).DistanceTo(position))
		.FirstOrDefault(u => ToMap(u.X, u.Z).DistanceTo(position) < PickRadius);

	/// <summary>The enemy target under a map position: a unit in sight, a plant or substation, or a line.</summary>
	private (AttackTarget Target, string Name)? TargetAt(Vector2 position)
	{
		foreach (var unit in _units.Units)
		{
			if (unit.OwnerId != _playerId && unit.IsAlive && _vision.CanSee(_playerId, unit) && ToMap(unit.X, unit.Z).DistanceTo(position) < PickRadius)
			{
				return (AttackTarget.OnUnit(unit.Id), $"enemy {unit.Type.Name}");
			}
		}

		foreach (var node in _grid.Nodes)
		{
			if (node.CountryId != _playerId && node.IsOperational && ToMap(node.X, node.Z).DistanceTo(position) < PickRadius)
			{
				string name = node is PowerPlant plant
					? $"{plant.Kind.ToString().ToLowerInvariant()} plant"
					: $"transformer station ({_world.Cities[((Substation)node).CityId].Name})";
				return (AttackTarget.OnGridNode(node.Id), name);
			}
		}

		foreach (var line in _grid.Lines)
		{
			var a = _grid.Nodes[line.FromNodeId];
			var b = _grid.Nodes[line.ToNodeId];
			if (a.CountryId == _playerId || !line.IsOperational)
			{
				continue;
			}

			var closest = Geometry2D.GetClosestPointToSegment(position, ToMap(a.X, a.Z), ToMap(b.X, b.Z));
			if (closest.DistanceTo(position) < 5)
			{
				var world = ToWorld(closest);
				return (AttackTarget.OnPowerLine(line.Id, StrategicMap.ToFixed(world.X), StrategicMap.ToFixed(world.Y)), "power line");
			}
		}

		return null;
	}

	/// <summary>Shaded relief tinted by who owns the land, with blue water.</summary>
	private static ImageTexture BuildBackground(Terrain terrain, Territory territory)
	{
		int width = terrain.Width >= terrain.Depth ? ImageSize : ImageSize * terrain.Width / terrain.Depth;
		int depth = terrain.Depth >= terrain.Width ? ImageSize : ImageSize * terrain.Depth / terrain.Width;
		var image = Image.CreateEmpty(width, depth, false, Image.Format.Rgb8);
		float sea = (float)terrain.SeaLevel.ToDouble();
		float stepX = (terrain.Width - 1f) / (width - 1);
		float stepZ = (terrain.Depth - 1f) / (depth - 1);

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int gx = (int)(x * stepX);
				int gz = (int)(z * stepZ);
				float h = (float)terrain.HeightAtPoint(gx, gz).ToDouble();
				Color color;
				if (h < sea)
				{
					float depthShade = Mathf.Clamp((sea - h) / 300f, 0, 1);
					color = new Color(0.16f, 0.32f, 0.5f).Lerp(new Color(0.07f, 0.16f, 0.3f), depthShade);
				}
				else
				{
					// Light from the north-west.
					float west = (float)terrain.HeightAtPoint(gx - 2, gz).ToDouble();
					float north = (float)terrain.HeightAtPoint(gx, gz - 2).ToDouble();
					float shade = Mathf.Clamp(1 + ((west - h) + (north - h)) / 300f, 0.6f, 1.25f);
					int owner = territory.OwnerAtPoint(gx, gz);
					var plain = new Color(0.52f, 0.56f, 0.42f);
					var land = owner >= 0 ? plain.Lerp(WorldView.ColorOf(owner), 0.22f) : plain;
					color = land * shade;
					color.A = 1;
				}

				image.SetPixel(x, z, color);
			}
		}

		return ImageTexture.CreateFromImage(image);
	}
}
