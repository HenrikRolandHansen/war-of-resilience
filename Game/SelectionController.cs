using System.Collections.Generic;
using System.Linq;
using Godot;
using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.Units;

namespace WarOfResilience.Game;

/// <summary>
/// Mouse orders for the local player: left click selects a unit, left drag selects all own units in
/// a box (Shift adds to the selection). A right click on an enemy unit, power plant, substation or
/// power line attacks it; anywhere else it moves the selection there. Orders go to the simulation
/// as commands, never by changing units directly.
/// </summary>
public partial class SelectionController : CanvasLayer
{
	/// <summary>Pixels the mouse must move with the left button held before it counts as a box.</summary>
	private const float DragThreshold = 6f;

	/// <summary>A click this close to a unit's marker picks it.</summary>
	private const float PickRadius = 18f;

	private Simulation _simulation = null!;
	private UnitManager _units = null!;
	private PowerGrid _grid = null!;
	private Vision _vision = null!;
	private string _hover = "";
	private UnitView _view = null!;
	private RtsCamera _camera = null!;
	private int _playerId;

	private SelectionBox _box = null!;
	private Label _info = null!;
	private bool _leftDown;
	private Vector2 _dragStart;
	private bool _boxing;

	public void Setup(Simulation simulation, UnitManager units, PowerGrid grid, Vision vision, UnitView view, RtsCamera camera, int playerId)
	{
		_simulation = simulation;
		_units = units;
		_grid = grid;
		_vision = vision;
		_view = view;
		_camera = camera;
		_playerId = playerId;
		_camera.RightClicked += OnRightClicked;
		UpdateInfo();
	}

	public override void _Ready()
	{
		_box = new SelectionBox { Name = "SelectionBox" };
		AddChild(_box);

		_info = new Label
		{
			Name = "Info",
			AnchorTop = 1,
			AnchorBottom = 1,
			OffsetLeft = 12,
			OffsetTop = -58,
			GrowVertical = Control.GrowDirection.Begin,
		};
		_info.AddThemeFontSizeOverride("font_size", 15);
		_info.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
		_info.AddThemeConstantOverride("outline_size", 5);
		AddChild(_info);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left } button when button.Pressed:
				_leftDown = true;
				_boxing = false;
				_dragStart = button.Position;
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
				if (_leftDown)
				{
					var picked = _boxing ? UnitsInBox(_dragStart, button.Position) : UnitAt(button.Position);
					Select(picked, add: button.ShiftPressed);
				}

				_leftDown = false;
				_boxing = false;
				_box.Clear();
				break;
			case InputEventMouseMotion motion when !_leftDown:
				UpdateHover(motion.Position);
				break;
			case InputEventMouseMotion motion when _leftDown:
				if (!_boxing && motion.Position.DistanceTo(_dragStart) > DragThreshold)
				{
					_boxing = true;
				}

				if (_boxing)
				{
					_box.Show(_dragStart, motion.Position);
				}

				break;
		}
	}

	private void Select(IEnumerable<int> unitIds, bool add)
	{
		var selection = add ? _view.Selected.Concat(unitIds) : unitIds;
		_view.SetSelected(selection.Distinct().ToList());
		UpdateInfo();
	}

	private IEnumerable<int> UnitAt(Vector2 screen)
	{
		int best = -1;
		float bestDistance = PickRadius;
		foreach (var unit in OwnUnits())
		{
			if (ScreenPosition(unit) is { } position && position.DistanceTo(screen) < bestDistance)
			{
				best = unit.Id;
				bestDistance = position.DistanceTo(screen);
			}
		}

		return best >= 0 ? new[] { best } : System.Array.Empty<int>();
	}

	private IEnumerable<int> UnitsInBox(Vector2 from, Vector2 to)
	{
		var rect = new Rect2(from, to - from).Abs();
		return OwnUnits().Where(u => ScreenPosition(u) is { } p && rect.HasPoint(p)).Select(u => u.Id).ToList();
	}

	private IEnumerable<Unit> OwnUnits() => _units.Units.Where(u => u.OwnerId == _playerId && u.IsAlive);

	private Vector2? ScreenPosition(Unit unit)
	{
		var world = _view.DrawnPosition(unit.Id);
		var camera = _camera.Camera;
		return camera.IsPositionBehind(world) ? null : camera.UnprojectPosition(world);
	}

	private void OnRightClicked(Vector2 screen)
	{
		if (_view.Selected.Count == 0)
		{
			return;
		}

		if (TargetAt(screen) is { } target)
		{
			_simulation.Schedule(new AttackCommand(_playerId, _view.Selected, target.Target));
		}
		else if (_camera.GroundPointAt(screen) is { } point)
		{
			_simulation.Schedule(new MoveCommand(_playerId, _view.Selected, ToFixed(point.X), ToFixed(point.Z)));
		}
	}

	private void UpdateHover(Vector2 screen)
	{
		string hover = _view.Selected.Count > 0 && TargetAt(screen) is { } target ? $"Right click: attack {target.Name}" : "";
		if (hover != _hover)
		{
			_hover = hover;
			UpdateInfo();
		}
	}

	/// <summary>
	/// The enemy thing under the cursor: a visible unit, then a plant or substation, then a power line.
	/// Plants and substations count by their marker on screen or by their real footprint on the ground.
	/// </summary>
	private (AttackTarget Target, string Name)? TargetAt(Vector2 screen)
	{
		foreach (var unit in _units.Units)
		{
			if (unit.OwnerId != _playerId && unit.IsAlive && _vision.CanSee(_playerId, unit)
				&& ScreenPosition(unit) is { } position && position.DistanceTo(screen) < PickRadius)
			{
				return (AttackTarget.OnUnit(unit.Id), $"enemy {unit.Type.Name}");
			}
		}

		var ground = _camera.GroundPointAt(screen);
		var camera = _camera.Camera;
		foreach (var node in _grid.Nodes)
		{
			if (node.CountryId == _playerId || !node.IsOperational)
			{
				continue;
			}

			float x = (float)node.X.ToDouble();
			float z = (float)node.Z.ToDouble();
			float footprint = node is PowerPlant ? 300f : 120f;
			var world = new Vector3(x, ground?.Y ?? 0, z);
			bool onMarker = !camera.IsPositionBehind(world) && camera.UnprojectPosition(world).DistanceTo(screen) < PickRadius * 1.5f;
			bool onGround = ground is { } g && new Vector2(g.X - x, g.Z - z).Length() < footprint;
			if (onMarker || onGround)
			{
				string name = node is PowerPlant plant ? $"{plant.Kind.ToString().ToLowerInvariant()} power plant" : "transformer station";
				return (AttackTarget.OnGridNode(node.Id), name);
			}
		}

		if (ground is { } hit)
		{
			// A line is hit if the cursor is within a few pixels' worth of ground from it.
			float tolerance = Mathf.Max(60f, _camera.Distance * 0.012f);
			foreach (var line in _grid.Lines)
			{
				var a = _grid.Nodes[line.FromNodeId];
				var b = _grid.Nodes[line.ToNodeId];
				if (a.CountryId == _playerId || !line.IsOperational)
				{
					continue;
				}

				var p = new Vector2(hit.X, hit.Z);
				var start = new Vector2((float)a.X.ToDouble(), (float)a.Z.ToDouble());
				var end = new Vector2((float)b.X.ToDouble(), (float)b.Z.ToDouble());
				var closest = Geometry2D.GetClosestPointToSegment(p, start, end);
				if (closest.DistanceTo(p) < tolerance)
				{
					return (AttackTarget.OnPowerLine(line.Id, ToFixed(closest.X), ToFixed(closest.Y)), "power line");
				}
			}
		}

		return null;
	}

	private static Fixed ToFixed(float metres) => Fixed.FromRaw((long)(metres * (1 << Fixed.FractionBits)));

	/// <summary>Call after changing the selection from elsewhere, such as the strategic map.</summary>
	public void RefreshInfo() => UpdateInfo();

	private void UpdateInfo()
	{
		if (_units == null)
		{
			return;
		}

		var selected = _view.Selected.Select(id => _units.Units[id]).Where(u => u.IsAlive).ToList();
		string what = selected.Count == 0
			? "Nothing selected"
			: "Selected: " + string.Join(", ", selected.GroupBy(u => u.Type.Name).Select(g => $"{g.Count()} {g.Key}"));
		string help = _hover != "" ? _hover : "Left click or drag: select (Shift adds)   Right click: move or attack   Right drag: pan   Wheel: zoom";
		_info.Text = what + "\n" + help;
	}
}
