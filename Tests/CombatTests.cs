using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.Units;
using WarOfResilience.Sim.World;
using Xunit;

namespace WarOfResilience.Tests;

public class CombatTests
{
	private static readonly Terrain Map = TerrainGenerator.Generate(1, new TerrainGenerator.Settings());
	private static readonly UnitCatalog Catalog = UnitCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "units.json")));

	private sealed record Game(Simulation Sim, WorldState World, PowerGrid Grid, UnitManager Units, Vision Vision);

	private static Game NewGame()
	{
		var world = WorldGenerator.Generate(1, Map, new WorldGenerator.Settings());
		var grid = GridGenerator.Generate(1, Map, world, new GridGenerator.Settings());
		var units = new UnitManager(Map, Catalog);
		StartingForces.Place(units, world);
		var vision = new Vision(units, world);
		var sim = new Simulation(seed: 1);
		sim.AddSystem(grid);
		sim.AddSystem(world);
		sim.AddSystem(units);
		sim.AddSystem(vision);
		return new Game(sim, world, grid, units, vision);
	}

	private static IEnumerable<Unit> Kamikazes(Game game, int owner) =>
		game.Units.Units.Where(u => u.OwnerId == owner && u.Type.Id == "kamikaze_drone");

	/// <summary>Enough ticks to fly across the whole map.</summary>
	private static int FlightTicks(Game game) =>
		(int)(1_800_000 / Catalog.StepPerTick(Catalog["kamikaze_drone"]).ToDouble()) + 20;

	[Fact]
	public void KamikazeDrone_DestroysASubstation_AndItsCityLosesPower()
	{
		var game = NewGame();
		var substation = game.Grid.Substations.First(s => s.CountryId == 1 && !game.World.Cities[s.CityId].IsCapital);
		var city = game.World.Cities[substation.CityId];
		var drone = Kamikazes(game, 0).First();
		Assert.Equal(100, city.PowerPercent);

		game.Sim.Schedule(new AttackCommand(0, new[] { drone.Id }, AttackTarget.OnGridNode(substation.Id)));
		game.Sim.Step(FlightTicks(game));

		Assert.False(drone.IsAlive);
		Assert.False(substation.IsOperational);
		Assert.Equal(0, city.PowerPercent);
		Assert.True(city.TargetMorale < city.BaseMorale);
	}

	[Fact]
	public void PowerPlant_TakesSeveralHits()
	{
		var game = NewGame();
		var plant = game.Grid.Plants.First(p => p.CountryId == 1 && p.Kind == PowerPlantKind.Coal);
		var drones = Kamikazes(game, 0).ToList();
		int damage = drones[0].Type.AttackDamage;
		int needed = (plant.MaxHitPoints + damage - 1) / damage;
		Assert.True(needed > 1);

		game.Sim.Schedule(new AttackCommand(0, drones.Take(needed - 1).Select(d => d.Id), AttackTarget.OnGridNode(plant.Id)));
		game.Sim.Step(FlightTicks(game));

		Assert.True(plant.IsOperational);
		Assert.Equal(plant.MaxHitPoints - (needed - 1) * damage, plant.HitPoints);
		Assert.True(game.Grid.PowerOf(1).ProducedMW > 0);
	}

	[Fact]
	public void CuttingAPowerLine_ChangesTheGrid()
	{
		var game = NewGame();
		var line = game.Grid.Lines.First(l => game.Grid.Nodes[l.FromNodeId].CountryId == 1);
		var a = game.Grid.Nodes[line.FromNodeId];
		var b = game.Grid.Nodes[line.ToNodeId];
		var middle = ((a.X + b.X) * Fixed.Half, (a.Z + b.Z) * Fixed.Half);

		// Aim a little off the line: the command snaps the hit to the nearest point on it.
		var drone = Kamikazes(game, 0).First();
		game.Sim.Schedule(new AttackCommand(0, new[] { drone.Id },
			AttackTarget.OnPowerLine(line.Id, middle.Item1 + Fixed.FromInt(300), middle.Item2)));
		game.Sim.Step(FlightTicks(game));

		Assert.False(drone.IsAlive);
		Assert.False(line.IsOperational);
		Assert.False(line.IsEnergized);
	}

	[Fact]
	public void KamikazeDrone_DestroysAVisibleEnemyUnit()
	{
		var game = NewGame();
		var enemyJeep = game.Units.Units.First(u => u.OwnerId == 1 && u.Type.Id == "jeep");
		var drones = Kamikazes(game, 0).ToList();

		// First fly a recon drone over the enemy so the jeep can be seen.
		var recon = game.Units.Units.First(u => u.OwnerId == 0 && u.Type.Id == "recon_drone");
		game.Sim.Schedule(new MoveCommand(0, new[] { recon.Id }, enemyJeep.X, enemyJeep.Z));
		game.Sim.Step(FlightTicks(game));
		Assert.True(game.Vision.CanSee(0, enemyJeep));

		game.Sim.Schedule(new AttackCommand(0, new[] { drones[0].Id }, AttackTarget.OnUnit(enemyJeep.Id)));
		game.Sim.Step(FlightTicks(game));

		Assert.False(enemyJeep.IsAlive);
		Assert.False(drones[0].IsAlive);
	}

	[Fact]
	public void AttackOnAnUnseenUnit_IsIgnored()
	{
		var game = NewGame();
		var enemyJeep = game.Units.Units.First(u => u.OwnerId == 1 && u.Type.Id == "jeep");
		var drone = Kamikazes(game, 0).First();
		game.Sim.Step();
		Assert.False(game.Vision.CanSee(0, enemyJeep));

		game.Sim.Schedule(new AttackCommand(0, new[] { drone.Id }, AttackTarget.OnUnit(enemyJeep.Id)));
		game.Sim.Step();

		Assert.Null(drone.Target);
		Assert.False(drone.IsMoving);
	}

	[Fact]
	public void PlayerCannotAttackOwnInfrastructure()
	{
		var game = NewGame();
		var own = game.Grid.Substations.First(s => s.CountryId == 0);
		var drone = Kamikazes(game, 0).First();

		game.Sim.Schedule(new AttackCommand(0, new[] { drone.Id }, AttackTarget.OnGridNode(own.Id)));
		game.Sim.Step(50);

		Assert.Null(drone.Target);
		Assert.True(own.IsOperational);
	}

	[Fact]
	public void NonAttackingUnits_InAnAttackGroup_JustMoveToTheTarget()
	{
		var game = NewGame();
		var target = game.Grid.Plants.First(p => p.CountryId == 1);
		var recon = game.Units.Units.First(u => u.OwnerId == 0 && u.Type.Id == "recon_drone");

		game.Sim.Schedule(new AttackCommand(0, new[] { recon.Id }, AttackTarget.OnGridNode(target.Id)));
		game.Sim.Step();

		Assert.Null(recon.Target);
		Assert.True(recon.IsMoving);
		Assert.True(recon.IsAlive);
	}

	[Fact]
	public void Vision_SeesEnemiesNearOwnUnitsAndCities_Only()
	{
		var game = NewGame();
		game.Sim.Step();

		// At the start the armies are far apart.
		Assert.DoesNotContain(game.Units.Units.Where(u => u.OwnerId == 1), u => game.Vision.CanSee(0, u));
		Assert.All(game.Units.Units.Where(u => u.OwnerId == 0), u => Assert.True(game.Vision.CanSee(0, u)));

		// An enemy unit flying over one of our cities is spotted.
		var enemy = game.Units.Units.First(u => u.OwnerId == 1 && u.Type.Id == "recon_drone");
		var city = game.World.CitiesOf(0).First();
		game.Sim.Schedule(new MoveCommand(1, new[] { enemy.Id }, city.X, city.Z));
		game.Sim.Step(FlightTicks(game));
		Assert.True(game.Vision.CanSee(0, enemy));
	}

	[Fact]
	public void Vision_RemembersWhereEnemiesWereLastSeen()
	{
		var game = NewGame();
		var enemy = game.Units.Units.First(u => u.OwnerId == 1 && u.Type.Id == "recon_drone");
		var city = game.World.CitiesOf(0).First();

		// The enemy drone flies over our city, then home again into the fog.
		game.Sim.Schedule(new MoveCommand(1, new[] { enemy.Id }, city.X, city.Z));
		game.Sim.Step(FlightTicks(game));
		var spotted = game.Vision.LastSeenBy(0)[enemy.Id];
		Assert.Equal(game.Sim.Tick, spotted.Tick);

		var home = game.World.CitiesOf(1).First(c => c.IsCapital);
		game.Sim.Schedule(new MoveCommand(1, new[] { enemy.Id }, home.X, home.Z));
		game.Sim.Step(FlightTicks(game));

		Assert.False(game.Vision.CanSee(0, enemy));
		var last = game.Vision.LastSeenBy(0)[enemy.Id];
		Assert.True(last.Tick < game.Sim.Tick);
		Assert.DoesNotContain(game.Units.Units.Where(u => u.OwnerId == 1 && u.Id != enemy.Id), u => game.Vision.LastSeenBy(0).ContainsKey(u.Id));
	}

	[Fact]
	public void Vision_ForgetsUnitsSeenBeingDestroyed()
	{
		var game = NewGame();
		var enemyJeep = game.Units.Units.First(u => u.OwnerId == 1 && u.Type.Id == "jeep");
		var recon = game.Units.Units.First(u => u.OwnerId == 0 && u.Type.Id == "recon_drone");
		game.Sim.Schedule(new MoveCommand(0, new[] { recon.Id }, enemyJeep.X, enemyJeep.Z));
		game.Sim.Step(FlightTicks(game));
		Assert.True(game.Vision.LastSeenBy(0).ContainsKey(enemyJeep.Id));

		game.Sim.Schedule(new AttackCommand(0, new[] { Kamikazes(game, 0).First().Id }, AttackTarget.OnUnit(enemyJeep.Id)));
		game.Sim.Step(FlightTicks(game));

		Assert.False(enemyJeep.IsAlive);
		Assert.False(game.Vision.LastSeenBy(0).ContainsKey(enemyJeep.Id));
	}

	[Fact]
	public void SameOrders_GiveSameBattle()
	{
		ulong Run()
		{
			var game = NewGame();
			var target = game.Grid.Plants.First(p => p.CountryId == 1);
			game.Sim.Schedule(3, new AttackCommand(0, Kamikazes(game, 0).Select(u => u.Id), AttackTarget.OnGridNode(target.Id)));
			game.Sim.Step(FlightTicks(game));
			return game.Sim.ComputeChecksum();
		}

		Assert.Equal(Run(), Run());
	}
}
