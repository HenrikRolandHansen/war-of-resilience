using WarOfResilience.Sim.AI;
using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.Units;
using WarOfResilience.Sim.World;
using Xunit;

namespace WarOfResilience.Tests;

public class AiTests
{
	private static readonly Terrain Map = TerrainGenerator.Generate(1, new TerrainGenerator.Settings());
	private static readonly UnitCatalog Catalog = UnitCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "units.json")));

	/// <summary>Half an hour of real time.</summary>
	private const int HalfHour = 30 * 60 * Simulation.TicksPerSecond;

	private sealed record Game(Simulation Sim, WorldState World, PowerGrid Grid, UnitManager Units, Production Production);

	/// <summary>Country 1 is the AI; country 0 is a player who does nothing unless told.</summary>
	private static Game NewGame(ulong seed = 1, bool bothAi = false)
	{
		var world = WorldGenerator.Generate(1, Map, new WorldGenerator.Settings());
		var grid = GridGenerator.Generate(1, Map, world, new GridGenerator.Settings());
		var units = new UnitManager(Map, Catalog);
		StartingForces.Place(units, world);
		var vision = new Vision(units, world);
		var production = new Production(units, world);
		var sim = new Simulation(seed);
		sim.AddSystem(grid);
		sim.AddSystem(world);
		sim.AddSystem(units);
		sim.AddSystem(new AirDefence(units));
		sim.AddSystem(production);
		sim.AddSystem(vision);
		if (bothAi)
		{
			sim.AddSystem(new SimpleAi(0, units, world, grid, vision, production));
		}

		sim.AddSystem(new SimpleAi(1, units, world, grid, vision, production));
		return new Game(sim, world, grid, units, production);
	}

	[Fact]
	public void BuildCommand_PaysAndBuildsAtTheCapital()
	{
		var game = NewGame();
		var country = game.World.Countries[0];
		var type = Catalog["kamikaze_drone"];
		var before = country.Money;
		int count = game.Units.Units.Count;

		game.Sim.Schedule(new BuildCommand(0, type.Id));
		game.Sim.Step(1);
		Assert.True(country.Money < before);
		Assert.Single(game.Production.QueueOf(0));

		game.Sim.Step(type.BuildTicks);
		var built = game.Units.Units.Skip(count).Where(u => u.OwnerId == 0).ToList();
		Assert.Single(built);
		Assert.Equal(type, built[0].Type);
		var capital = game.World.Cities.First(c => c.CountryId == 0 && c.IsCapital);
		Assert.True(Fixed.Abs(built[0].X - capital.X) + Fixed.Abs(built[0].Z - capital.Z) < Fixed.FromInt(40_000));
	}

	[Fact]
	public void BuildCommand_IsRefusedWithoutMoney()
	{
		var game = NewGame();
		var type = Catalog["air_defence"];
		for (int i = 0; i < 5; i++)
		{
			game.Sim.Schedule(new BuildCommand(0, type.Id));
		}

		game.Sim.Step(1);
		Assert.True(game.Production.QueueOf(0).Count < 5);
		Assert.True(game.World.Countries[0].Money >= Fixed.Zero);
	}

	[Fact]
	public void Ai_BuildsScoutsAndDefendsItsCapital()
	{
		var game = NewGame();
		game.Sim.Step(5 * 60 * Simulation.TicksPerSecond);

		var own = game.Units.Units.Where(u => u.OwnerId == 1 && u.IsAlive).ToList();
		Assert.True(own.Count > 10, "the AI should have built units");
		Assert.Contains(own, u => u.Type.Id == "recon_drone" && u.IsMoving);

		var capitalStation = game.Grid.Substations.First(s => s.CountryId == 1 && game.World.Cities[s.CityId].IsCapital);
		Assert.Contains(own, u => u.Type.AirDefence != null
			&& Fixed.Abs(u.X - capitalStation.X) + Fixed.Abs(u.Z - capitalStation.Z) < Fixed.FromInt(3_000));
	}

	[Fact]
	public void Ai_BlacksOutThePassivePlayersCapital()
	{
		var game = NewGame();
		var capital = game.World.Cities.First(c => c.CountryId == 0 && c.IsCapital);
		int ticks = 0;
		while (capital.PowerPercent > 0 && ticks < HalfHour)
		{
			game.Sim.Step(100);
			ticks += 100;
		}

		Assert.Equal(0, capital.PowerPercent);
		Assert.True(game.World.Countries[0].IncomeLastTick < game.World.Countries[1].IncomeLastTick);
	}

	[Fact]
	public void TwoAis_GiveTheSameGameForTheSameSeed()
	{
		ulong Run()
		{
			var game = NewGame(seed: 7, bothAi: true);
			game.Sim.Step(6_000);
			return game.Sim.ComputeChecksum();
		}

		Assert.Equal(Run(), Run());
	}
}
