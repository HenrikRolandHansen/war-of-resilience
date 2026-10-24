using System.Collections.Generic;
using System.Linq;
using Godot;
using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Units;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Game;

/// <summary>
/// The player's factory, bottom right: one button per unit type with its price, greyed out when
/// the player cannot afford it, and what is being built. Orders go to the simulation as
/// <see cref="BuildCommand"/>s.
/// </summary>
public partial class BuildPanel : CanvasLayer
{
	private Simulation _simulation = null!;
	private Production _production = null!;
	private WorldState _world = null!;
	private int _playerId;
	private readonly List<(Button Button, UnitType Type)> _buttons = new();
	private Label _status = null!;

	public void Setup(Simulation simulation, UnitCatalog catalog, Production production, WorldState world, int playerId)
	{
		_simulation = simulation;
		_production = production;
		_world = world;
		_playerId = playerId;

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		panel.GrowHorizontal = Control.GrowDirection.Begin;
		panel.GrowVertical = Control.GrowDirection.Begin;
		panel.OffsetRight = -12;
		panel.OffsetBottom = -66; // above the selection and help lines
		AddChild(panel);

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 6);
		panel.AddChild(box);

		var title = new Label { Text = "Factory" };
		title.AddThemeFontSizeOverride("font_size", 16);
		box.AddChild(title);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		box.AddChild(row);
		foreach (var type in catalog.Types)
		{
			var button = new Button
			{
				Text = $"{type.Name}\n{type.Cost:N0} cr · {type.BuildTicks / Simulation.TicksPerSecond} s",
				FocusMode = Control.FocusModeEnum.None,
				CustomMinimumSize = new Vector2(118, 0),
			};
			button.AddThemeFontSizeOverride("font_size", 13);
			button.Pressed += () => _simulation.Schedule(new BuildCommand(_playerId, type.Id));
			row.AddChild(button);
			_buttons.Add((button, type));
		}

		_status = new Label();
		_status.AddThemeFontSizeOverride("font_size", 13);
		box.AddChild(_status);
	}

	public override void _Process(double delta)
	{
		if (_production == null)
		{
			return;
		}

		foreach (var (button, type) in _buttons)
		{
			button.Disabled = !_production.CanOrder(_playerId, type);
		}

		var queue = _production.QueueOf(_playerId);
		if (queue.Count == 0)
		{
			_status.Text = "Idle. New units appear next to the capital.";
			return;
		}

		string waiting = queue.Count > 1 ? $" · {queue.Count - 1} queued" : "";
		_status.Text = $"Building {queue.First().Name} {_production.ProgressOf(_playerId) * 100:0}%{waiting}";
	}
}
