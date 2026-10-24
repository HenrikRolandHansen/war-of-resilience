using System.Collections.Generic;
using System.Linq;
using Godot;
using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.Units;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Game;

/// <summary>
/// Full-screen strategic map (M or Tab): everything the player knows, fog or not. Cities, power
/// plants, transformer stations and lines are always shown; enemy units where they are seen now or
/// where they were last seen. On the right, quick selection buttons and a list of targets ranked by
/// how much they matter. Orders go to the simulation as commands, like everywhere else.
/// </summary>
public partial class StrategicMap : CanvasLayer
{
	private const double ListRefreshSeconds = 1.0;

	private Simulation _simulation = null!;
	private WorldState _world = null!;
	private PowerGrid _grid = null!;
	private UnitManager _units = null!;
	private Vision _vision = null!;
	private UnitView _unitView = null!;
	private SelectionController _selection = null!;
	private RtsCamera _camera = null!;
	private int _playerId;

	private Control _root = null!;
	private StrategicMapCanvas _canvas = null!;
	private VBoxContainer _targets = null!;
	private Button _openButton = null!;
	private double _sinceRefresh;

	public bool IsOpen => _root.Visible;

	public void Setup(Simulation simulation, Terrain terrain, WorldState world, PowerGrid grid, UnitManager units, Vision vision,
		UnitView unitView, SelectionController selection, RtsCamera camera, int playerId)
	{
		_simulation = simulation;
		_world = world;
		_grid = grid;
		_units = units;
		_vision = vision;
		_unitView = unitView;
		_selection = selection;
		_camera = camera;
		_playerId = playerId;
		Layer = 5;

		BuildUi();
		_canvas.Setup(this, terrain, world, grid, units, vision, camera, playerId);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false } key)
		{
			if (key.Keycode is Key.M or Key.Tab)
			{
				Toggle();
				GetViewport().SetInputAsHandled();
			}
			else if (key.Keycode == Key.Escape && IsOpen)
			{
				Toggle();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	public void Toggle()
	{
		_root.Visible = !_root.Visible;
		_openButton.Visible = !_root.Visible;
		if (_root.Visible)
		{
			RefreshTargets();
		}
	}

	public override void _Process(double delta)
	{
		if (!IsOpen)
		{
			return;
		}

		_canvas.QueueRedraw();
		_sinceRefresh += delta;
		if (_sinceRefresh >= ListRefreshSeconds)
		{
			RefreshTargets();
		}
	}

	// --- Orders, used by the canvas and the list ---

	public IReadOnlyCollection<int> Selected => _unitView.Selected;

	public void Select(IEnumerable<int> unitIds, bool add)
	{
		var selection = add ? _unitView.Selected.Concat(unitIds) : unitIds;
		_unitView.SetSelected(selection.Distinct().ToList());
		_selection.RefreshInfo();
	}

	public void Attack(AttackTarget target)
	{
		if (Selected.Count > 0)
		{
			_simulation.Schedule(new AttackCommand(_playerId, Selected, target));
		}
	}

	public void MoveTo(Vector2 world)
	{
		if (Selected.Count > 0)
		{
			_simulation.Schedule(new MoveCommand(_playerId, Selected, ToFixed(world.X), ToFixed(world.Y)));
		}
	}

	public void LookAt(Vector2 world) => _camera.JumpTo(world.X, world.Y, Mathf.Min(_camera.Distance, 150_000f));

	public static Fixed ToFixed(float metres) => Fixed.FromRaw((long)(metres * (1 << Fixed.FractionBits)));

	/// <summary>Real-time minutes and seconds since a tick, the same clock as the HUD.</summary>
	public string Ago(long tick)
	{
		long seconds = (_simulation.Tick - tick) / Simulation.TicksPerSecond;
		return seconds < 60 ? $"{seconds} s ago" : $"{seconds / 60}:{seconds % 60:00} ago";
	}

	// --- UI ---

	private void BuildUi()
	{
		_root = new Control { Name = "StrategicMap", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		var background = new ColorRect { Color = new Color(0.04f, 0.06f, 0.09f, 0.93f), MouseFilter = Control.MouseFilterEnum.Ignore };
		background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(background);

		var layout = new HBoxContainer();
		layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layout.OffsetLeft = 16;
		layout.OffsetTop = 16;
		layout.OffsetRight = -16;
		layout.OffsetBottom = -16;
		layout.AddThemeConstantOverride("separation", 16);
		_root.AddChild(layout);

		_canvas = new StrategicMapCanvas
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		layout.AddChild(_canvas);

		var side = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
		side.AddThemeConstantOverride("separation", 8);
		layout.AddChild(side);

		side.AddChild(Title("Strategic map"));
		side.AddChild(Hint("Left click: look there · drag: select units\nRight click: attack target or move · M / Tab / Esc: close"));

		side.AddChild(Title("Select"));
		var buttons = new HFlowContainer();
		buttons.AddThemeConstantOverride("h_separation", 6);
		buttons.AddThemeConstantOverride("v_separation", 6);
		side.AddChild(buttons);
		AddSelectButton(buttons, "Kamikaze", u => u.Type.Id == "kamikaze_drone");
		AddSelectButton(buttons, "Recon", u => u.Type.Id == "recon_drone");
		AddSelectButton(buttons, "Jeeps", u => u.Type.Id == "jeep");
		AddSelectButton(buttons, "All", _ => true);
		var none = new Button { Text = "None" };
		none.Pressed += () => Select(System.Array.Empty<int>(), add: false);
		buttons.AddChild(none);

		side.AddChild(Title("Targets"));
		var scroll = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		side.AddChild(scroll);
		_targets = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_targets.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(_targets);

		var close = new Button { Text = "Close map (M)" };
		close.Pressed += Toggle;
		side.AddChild(close);

		// Always-visible button to open the map, top right.
		var open = _openButton = new Button { Text = "Strategic map (M)", FocusMode = Control.FocusModeEnum.None };
		open.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		open.OffsetLeft = -170;
		open.OffsetTop = 12;
		open.OffsetRight = -12;
		open.Pressed += Toggle;
		AddChild(open);
		MoveChild(open, 0);
	}

	private void AddSelectButton(Container parent, string text, System.Func<Unit, bool> filter)
	{
		var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
		button.Pressed += () => Select(_units.Units.Where(u => u.OwnerId == _playerId && u.IsAlive && filter(u)).Select(u => u.Id), add: false);
		parent.AddChild(button);
	}

	/// <summary>
	/// Enemy targets ranked by value: substations by the people they feed, plants by output, then
	/// enemy units seen now and those last seen in the fog.
	/// </summary>
	private void RefreshTargets()
	{
		_sinceRefresh = 0;
		foreach (var child in _targets.GetChildren())
		{
			child.QueueFree();
		}

		bool canOrder = Selected.Count > 0;

		_targets.AddChild(Subtitle("Transformer stations"));
		foreach (var substation in _grid.Substations.Where(s => s.CountryId != _playerId).OrderByDescending(s => _world.Cities[s.CityId].PopulationThousands))
		{
			var city = _world.Cities[substation.CityId];
			string state = substation.IsOperational ? $"{city.PopulationThousands:N0}k people" : "destroyed";
			AddRow($"{city.Name} · {state}", PositionOf(substation),
				substation.IsOperational && canOrder ? AttackTarget.OnGridNode(substation.Id) : null);
		}

		_targets.AddChild(Subtitle("Power plants"));
		foreach (var plant in _grid.Plants.Where(p => p.CountryId != _playerId).OrderByDescending(p => p.CapacityMW))
		{
			string state = plant.IsOperational
				? $"{plant.CapacityMW:N0} MW · {plant.HitPoints * 100 / plant.MaxHitPoints}%"
				: "destroyed";
			AddRow($"{plant.Kind} · {state}", PositionOf(plant), plant.IsOperational && canOrder ? AttackTarget.OnGridNode(plant.Id) : null);
		}

		_targets.AddChild(Subtitle("Enemy units"));
		int listed = 0;
		foreach (var unit in _units.Units.Where(u => u.OwnerId != _playerId && u.IsAlive && _vision.CanSee(_playerId, u)))
		{
			AddRow($"{unit.Type.Name} · in sight", new Vector2((float)unit.X.ToDouble(), (float)unit.Z.ToDouble()),
				canOrder ? AttackTarget.OnUnit(unit.Id) : null);
			listed++;
		}

		foreach (var (id, sighting) in _vision.LastSeenBy(_playerId))
		{
			var unit = _units.Units[id];
			if (_vision.CanSee(_playerId, unit))
			{
				continue;
			}

			var position = new Vector2((float)sighting.X.ToDouble(), (float)sighting.Z.ToDouble());
			AddRow($"{unit.Type.Name} · seen {Ago(sighting.Tick)}", position, null, moveInstead: canOrder);
			listed++;
		}

		if (listed == 0)
		{
			_targets.AddChild(Hint("None seen yet. Send a recon drone."));
		}
	}

	private static Vector2 PositionOf(GridNode node) => new((float)node.X.ToDouble(), (float)node.Z.ToDouble());

	private void AddRow(string text, Vector2 position, AttackTarget? target, bool moveInstead = false)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);
		var label = new Label { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
		label.AddThemeFontSizeOverride("font_size", 14);
		row.AddChild(label);

		var show = new Button { Text = "Show", FocusMode = Control.FocusModeEnum.None };
		show.Pressed += () => LookAt(position);
		row.AddChild(show);

		if (target is { } t)
		{
			var attack = new Button { Text = "Attack", FocusMode = Control.FocusModeEnum.None };
			attack.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.5f));
			attack.Pressed += () => Attack(t);
			row.AddChild(attack);
		}
		else if (moveInstead)
		{
			var go = new Button { Text = "Go", FocusMode = Control.FocusModeEnum.None };
			go.Pressed += () => MoveTo(position);
			row.AddChild(go);
		}

		_targets.AddChild(row);
	}

	private static Label Title(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 20);
		return label;
	}

	private static Label Subtitle(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 15);
		label.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 1f));
		return label;
	}

	private static Label Hint(string text)
	{
		var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		label.AddThemeFontSizeOverride("font_size", 13);
		label.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
		return label;
	}
}
