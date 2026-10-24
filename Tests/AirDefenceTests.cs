using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.Units;
using WarOfResilience.Sim.World;
using Xunit;

namespace WarOfResilience.Tests;

public class AirDefenceTests
{
	private static readonly Terrain Map = TerrainGenerator.Generate(1, new TerrainGenerator.Settings());
	private static readonly UnitCatalog Catalog = UnitCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "units.json")));

	private sealed record Game(Simulation Sim, WorldState World, PowerGrid Grid, UnitManager Units, AirDefence AirDefence);

	private static Game NewGame(bool startingForces = true)
	{
		var world = WorldGenerator.Generate(1, Map, new WorldGenerator.Settings());
		var grid = GridGenerator.Generate(1, Map, world, new GridGenerator.Settings());
		var units = new UnitManager(Map, Catalog);
		if (startingForces)
		{
			StartingForces.Place(units, world);
		}

		var airDefence = new AirDefence(units);
		var sim = new Simulation(seed: 1);
		sim.AddSystem(grid);
		sim.AddSystem(world);
		sim.AddSystem(units);
		sim.AddSystem(airDefence);
		sim.AddSystem(new Vision(units, world));
		return new Game(sim, world, grid, units, airDefence);
	}

	private static int FlightTicks() => (int)(1_800_000 / Catalog.StepPerTick(Catalog["kamikaze_drone"]).ToDouble()) + 20;

	/// <summary>An enemy substation well away from the enemy capital, where its starting air defence waits.</summary>
	private static Substation RemoteEnemySubstation(Game game)
	{
		var capital = game.World.Cities.First(c => c.CountryId == 1 && c.IsCapital);
		return game.Grid.Substations
			.Where(s => s.CountryId == 1)
			.OrderByDescending(s => Fixed.Abs(s.X - capital.X) + Fixed.Abs(s.Z - capital.Z))
			.First();
	}

	[Fact]
	public void Catalog_HasAnAirDefenceUnit()
	{
		var stats = Catalog["air_defence"].AirDefence;
		Assert.NotNull(stats);
		Assert.True(stats!.RangeMetres <= Catalog["air_defence"].SightMetres);
		Assert.Null(Catalog["jeep"].AirDefence);
	}

	[Fact]
	public void UnprotectedSubstation_IsDestroyedByADrone()
	{
		var game = NewGame();
		var substation = RemoteEnemySubstation(game);
		var drone = game.Units.Units.First(u => u.OwnerId == 0 && u.Type.Id == "kamikaze_drone");

		game.Sim.Schedule(new AttackCommand(0, new[] { drone.Id }, AttackTarget.OnGridNode(substation.Id)));
		game.Sim.Step(FlightTicks());

		Assert.False(substation.IsOperational);
		Assert.Equal(0, game.AirDefence.Launched);
	}

	[Fact]
	public void ProtectedSubstation_SurvivesADroneAttack()
	{
		var game = NewGame();
		var substation = RemoteEnemySubstation(game);
		var launcher = game.Units.Spawn(Catalog["air_defence"], 1, substation.X + Fixed.FromInt(800), substation.Z);
		var drone = game.Units.Units.First(u => u.OwnerId == 0 && u.Type.Id == "kamikaze_drone");

		game.Sim.Schedule(new AttackCommand(0, new[] { drone.Id }, AttackTarget.OnGridNode(substation.Id)));
		game.Sim.Step(FlightTicks());

		Assert.True(substation.IsOperational);
		Assert.Equal(substation.MaxHitPoints, substation.HitPoints);
		Assert.False(drone.IsAlive);
		Assert.True(game.AirDefence.Launched > 0);
		Assert.True(launcher.IsAlive);
	}

	[Fact]
	public void IgnoresGroundUnitsAndOwnAircraft()
	{
		var game = NewGame(startingForces: false);
		var x = Fixed.FromInt(600_000);
		var z = Fixed.FromInt(600_000);
		var launcher = game.Units.Spawn(Catalog["air_defence"], 1, x, z);
		game.Units.Spawn(Catalog["jeep"], 0, x + Fixed.FromInt(1_000), z);
		game.Units.Spawn(Catalog["recon_drone"], 1, x, z + Fixed.FromInt(1_000));

		game.Sim.Step(100);

		Assert.Equal(0, game.AirDefence.Launched);
		Assert.Equal(Catalog["air_defence"].AirDefence!.Magazine, game.AirDefence.AmmoOf(launcher));
	}

	[Fact]
	public void ASwarm_EmptiesTheMagazine_AndSomeGetThrough()
	{
		var game = NewGame(startingForces: false);
		var stats = Catalog["air_defence"].AirDefence!;
		var x = Fixed.FromInt(600_000);
		var z = Fixed.FromInt(600_000);
		var launcher = game.Units.Spawn(Catalog["air_defence"], 1, x, z);
		var swarm = Enumerable.Range(0, stats.Magazine + 3)
			.Select(i => game.Units.Spawn(Catalog["recon_drone"], 0, x + Fixed.FromInt(2_000 + 100 * i), z))
			.ToList();

		// Long enough to fire the whole magazine, too short to rearm a missile.
		game.Sim.Step(stats.Magazine * stats.ReloadTicks + 20);

		Assert.Equal(stats.Magazine, game.AirDefence.Launched);
		Assert.Equal(0, game.AirDefence.AmmoOf(launcher));
		Assert.Contains(swarm, d => d.IsAlive);
		Assert.Contains(swarm, d => !d.IsAlive);
	}

	[Fact]
	public void SpentMissiles_AreRearmedOverTime()
	{
		var game = NewGame(startingForces: false);
		var stats = Catalog["air_defence"].AirDefence!;
		var x = Fixed.FromInt(600_000);
		var z = Fixed.FromInt(600_000);
		var launcher = game.Units.Spawn(Catalog["air_defence"], 1, x, z);
		game.Units.Spawn(Catalog["kamikaze_drone"], 0, x + Fixed.FromInt(3_000), z);

		game.Sim.Step(stats.ReloadTicks * 4);
		int afterFight = game.AirDefence.AmmoOf(launcher);
		Assert.True(afterFight < stats.Magazine);
		Assert.Empty(game.AirDefence.Missiles);

		game.Sim.Step(stats.RearmTicks * stats.Magazine);
		Assert.Equal(stats.Magazine, game.AirDefence.AmmoOf(launcher));
	}

	[Fact]
	public void SameSeed_GivesTheSameBattle()
	{
		ulong Run()
		{
			var game = NewGame();
			var substation = RemoteEnemySubstation(game);
			game.Units.Spawn(Catalog["air_defence"], 1, substation.X + Fixed.FromInt(800), substation.Z);
			var drones = game.Units.Units.Where(u => u.OwnerId == 0 && u.Type.Id == "kamikaze_drone").Select(u => u.Id).ToList();
			game.Sim.Schedule(new AttackCommand(0, drones, AttackTarget.OnGridNode(substation.Id)));
			game.Sim.Step(FlightTicks());
			return game.Sim.ComputeChecksum();
		}

		Assert.Equal(Run(), Run());
	}
}
