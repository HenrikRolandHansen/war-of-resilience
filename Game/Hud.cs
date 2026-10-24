using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Game;

/// <summary>
/// Top-left panel with game time and each country's money, income, average morale and power.
/// </summary>
public partial class Hud : CanvasLayer
{
	private const double RefreshSeconds = 0.25;

	private Simulation? _simulation;
	private WorldState? _world;
	private PowerGrid? _grid;
	private Label _label = null!;
	private double _sinceRefresh = RefreshSeconds;

	public override void _Ready()
	{
		var panel = new PanelContainer { Position = new Vector2(12, 12) };
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.07f, 0.1f, 0.8f),
			ContentMarginLeft = 12,
			ContentMarginRight = 12,
			ContentMarginTop = 8,
			ContentMarginBottom = 8,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
		});
		AddChild(panel);

		_label = new Label();
		_label.AddThemeFontSizeOverride("font_size", 16);
		panel.AddChild(_label);
	}

	private IReadOnlyCollection<int> _aiCountries = System.Array.Empty<int>();

	/// <param name="aiCountries">Countries run by the computer, marked "(AI)".</param>
	public void Setup(Simulation simulation, WorldState world, PowerGrid grid, IReadOnlyCollection<int>? aiCountries = null)
	{
		_aiCountries = aiCountries ?? System.Array.Empty<int>();
		_simulation = simulation;
		_world = world;
		_grid = grid;
		Refresh();
	}

	public override void _Process(double delta)
	{
		_sinceRefresh += delta;
		if (_sinceRefresh >= RefreshSeconds)
		{
			_sinceRefresh = 0;
			Refresh();
		}
	}

	private void Refresh()
	{
		if (_simulation == null || _world == null || _grid == null)
		{
			return;
		}

		long seconds = _simulation.Tick / Simulation.TicksPerSecond;
		var text = new StringBuilder();
		text.Append($"Time {seconds / 60:00}:{seconds % 60:00}");

		foreach (var country in _world.Countries)
		{
			var cities = _world.CitiesOf(country.Id).ToList();
			double money = country.Money.ToDouble();
			double incomePerSecond = country.IncomeLastTick.ToDouble() * Simulation.TicksPerSecond;
			double morale = cities.Count == 0 ? 0 : cities.Average(c => c.Morale);
			var power = _grid.PowerOf(country.Id);
			int powered = power.DemandMW == 0 ? 100 : power.SuppliedMW * 100 / power.DemandMW;
			string name = _aiCountries.Contains(country.Id) ? $"{country.Name} (AI)" : country.Name;
			text.Append($"\n{name,-13}  {money,9:N0} cr   +{incomePerSecond:N0}/s   morale {morale:0}%   {cities.Count} cities");
			text.Append($"\n          power {power.ProducedMW:N0} MW made, {power.DemandMW:N0} MW needed, {powered}% supplied");
		}

		_label.Text = text.ToString();
	}
}
