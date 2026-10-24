using System.Collections.Generic;
using System.Linq;
using Godot;
using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Units;

namespace WarOfResilience.Game;

/// <summary>
/// Mouse orders for the local player: left click selects a unit, left drag selects all own units in
/// a box (Shift adds to the selection), and a right click sends the selection there. Orders go to
/// the simulation as commands, never by changing units directly.
/// </summary>
public partial class SelectionController : CanvasLayer
{
	/// <summary>Pixels the mouse must move with the left button held before it counts as a box.</summary>
	private const float DragThreshold = 6f;

	/// <summary>A click this close to a unit's marker picks it.</summary>
	private const float PickRadius = 18f;

	private Simulation _simulation = null!;
	private UnitManager _units = null!;
	private UnitView _view = null!;
	private RtsCamera _camera = null!;
	private int _playerId;

	private SelectionBox _box = null!;
	private Label _info = null!;
	private bool _leftDown;
	private Vector2 _dragStart;
	private bool _boxing;

	public void Setup(Simulation simulation, UnitManager units, UnitView view, RtsCamera camera, int playerId)
	{
		_simulation = simulation;
		_units = units;
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

	private IEnumerable<Unit> OwnUnits() => _units.Units.Where(u => u.OwnerId == _playerId && u.HitPoints > 0);

	private Vector2? ScreenPosition(Unit unit)
	{
		var world = _view.DrawnPosition(unit.Id);
		var camera = _camera.Camera;
		return camera.IsPositionBehind(world) ? null : camera.UnprojectPosition(world);
	}

	private void OnRightClicked(Vector2 screen)
	{
		if (_view.Selected.Count == 0 || _camera.GroundPointAt(screen) is not { } target)
		{
			return;
		}

		_simulation.Schedule(new MoveCommand(_playerId, _view.Selected, ToFixed(target.X), ToFixed(target.Z)));
	}

	private static Fixed ToFixed(float metres) => Fixed.FromRaw((long)(metres * (1 << Fixed.FractionBits)));

	private void UpdateInfo()
	{
		if (_units == null)
		{
			return;
		}

		var selected = _view.Selected.Select(id => _units.Units[id]).ToList();
		string what = selected.Count == 0
			? "Nothing selected"
			: "Selected: " + string.Join(", ", selected.GroupBy(u => u.Type.Name).Select(g => $"{g.Count()} {g.Key}"));
		_info.Text = what + "\nLeft click or drag: select (Shift adds)   Right click: move   Right drag: pan   Wheel: zoom";
	}
}
