using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Units;
using WarOfResilience.Sim.World;
using Xunit;

namespace WarOfResilience.Tests;

public class UnitTests
{
	private static readonly Terrain Map = TerrainGenerator.Generate(1, new TerrainGenerator.Settings());
	private static readonly UnitCatalog Catalog = UnitCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "units.json")));

	[Theory]
	[InlineData(0, 0)]
	[InlineData(1, 1)]
	[InlineData(4, 2)]
	[InlineData(2, 1.41421)]
	[InlineData(1_000_000, 1000)]
	[InlineData(1_440_000_000_000, 1_200_000)]
	public void Sqrt_IsAccurate(double value, double expected)
	{
		var result = Fixed.Sqrt(Fixed.FromRaw((long)(value * 65536))).ToDouble();

		Assert.InRange(result, expected - 0.0001 * Math.Max(1, expected), expected + 0.0001 * Math.Max(1, expected));
	}

	[Fact]
	public void Catalog_LoadsTheGamesUnitTypes()
	{
		Assert.Equal(UnitDomain.Ground, Catalog["jeep"].Domain);
		Assert.Equal(UnitDomain.Air, Catalog["recon_drone"].Domain);
		Assert.Equal(UnitDomain.Air, Catalog["kamikaze_drone"].Domain);
		Assert.True(Catalog["kamikaze_drone"].SpeedKmh > Catalog["jeep"].SpeedKmh);
		Assert.Throws<KeyNotFoundException>(() => Catalog["battleship"]);
	}

	[Fact]
	public void Catalog_RejectsBadData()
	{
		Assert.Throws<FormatException>(() => UnitCatalog.Parse("""{"timeScale": 0, "units": []}"""));
		Assert.Throws<FormatException>(() => UnitCatalog.Parse("""{"timeScale": 1, "units": [{"id": "x", "speedKmh": 0, "hitPoints": 1}]}"""));
		Assert.Throws<FormatException>(() => UnitCatalog.Parse(
			"""{"timeScale": 1, "units": [{"id": "x", "speedKmh": 1, "hitPoints": 1}, {"id": "x", "speedKmh": 1, "hitPoints": 1}]}"""));
	}

	[Fact]
	public void StepPerTick_FollowsSpeedAndTimeScale()
	{
		var catalog = UnitCatalog.Parse("""{"timeScale": 10, "units": [{"id": "car", "speedKmh": 36, "hitPoints": 1}]}""");

		// 36 km/h = 10 m/s, times 10 = 100 m per real second = 10 m per tick.
		Assert.Equal(Fixed.FromInt(10), catalog.StepPerTick(catalog["car"]));
	}

	[Fact]
	public void Unit_MovesToItsDestination_AndStops()
	{
		var (sim, units) = NewGame();
		var jeep = units.Units.First(u => u.Type.Id == "jeep" && u.OwnerId == 0);
		var target = (X: jeep.X + Fixed.FromInt(2000), Z: jeep.Z);
		Assume.LandAlong(jeep.X, jeep.Z, target.X, target.Z);

		sim.Schedule(new MoveCommand(0, new[] { jeep.Id }, target.X, target.Z));
		int ticks = TicksToTravel(2000, jeep.Type) + 5;
		sim.Step(ticks);

		Assert.False(jeep.IsMoving);
		Assert.Equal(target.X, jeep.X);
		Assert.Equal(target.Z, jeep.Z);
		Assert.Equal(Map.HeightAt(jeep.X, jeep.Z), jeep.Y);
	}

	[Fact]
	public void Unit_MovesAtItsSpeed()
	{
		var (sim, units) = NewGame();
		var drone = units.Units.First(u => u.Type.Id == "recon_drone" && u.OwnerId == 0);
		var startX = drone.X;

		sim.Schedule(new MoveCommand(0, new[] { drone.Id }, drone.X + Fixed.FromInt(100_000), drone.Z));
		// The command applies at the start of the next tick, and the unit moves in that same tick.
		sim.Step(Simulation.TicksPerSecond);

		var moved = (drone.X - startX).ToDouble();
		var expected = Catalog.StepPerTick(drone.Type).ToDouble() * Simulation.TicksPerSecond;
		Assert.InRange(moved, expected * 0.99, expected * 1.01);
	}

	[Fact]
	public void GroundUnit_StopsAtWater_ButDronesFlyOver()
	{
		var (sim, units) = NewGame();
		var jeep = units.Units.First(u => u.Type.Id == "jeep" && u.OwnerId == 0);
		var drone = units.Units.First(u => u.Type.Id == "recon_drone" && u.OwnerId == 0);
		var water = FindWaterNear(jeep.X, jeep.Z);

		sim.Schedule(new MoveCommand(0, new[] { jeep.Id, drone.Id }, water.X, water.Z));
		sim.Step(TicksToTravel(400_000, jeep.Type));

		Assert.False(jeep.IsMoving);
		Assert.False(Map.IsUnderWater(jeep.X, jeep.Z));
		Assert.False(drone.IsMoving);
		Assert.True(Map.IsUnderWater(drone.X, drone.Z), "The drone should have reached the water.");
		Assert.True(drone.Y >= Map.SeaLevel + Fixed.FromInt(drone.Type.CruiseAltitudeMetres) - Fixed.One);
	}

	[Fact]
	public void AirUnit_FliesAboveTheGround()
	{
		var (sim, units) = NewGame();
		var drone = units.Units.First(u => u.Type.Id == "kamikaze_drone" && u.OwnerId == 0);
		sim.Schedule(new MoveCommand(0, new[] { drone.Id }, Map.SizeX / Fixed.FromInt(2), Map.SizeZ / Fixed.FromInt(2)));

		for (int i = 0; i < 2000; i++)
		{
			sim.Step();
			var surface = Fixed.Max(Map.HeightAt(drone.X, drone.Z), Map.SeaLevel);
			Assert.True(drone.Y > surface, $"Drone hit the ground at tick {sim.Tick}.");
		}
	}

	[Fact]
	public void MoveCommand_IgnoresOtherPlayersUnits()
	{
		var (sim, units) = NewGame();
		var enemy = units.Units.First(u => u.OwnerId == 1);

		sim.Schedule(new MoveCommand(0, new[] { enemy.Id }, Fixed.Zero, Fixed.Zero));
		sim.Step();

		Assert.False(enemy.IsMoving);
	}

	[Fact]
	public void MoveCommand_SpreadsAGroupOut()
	{
		var (sim, units) = NewGame();
		var group = units.Units.Where(u => u.OwnerId == 0).ToList();

		sim.Schedule(new MoveCommand(0, group.Select(u => u.Id), group[0].X + Fixed.FromInt(50_000), group[0].Z));
		sim.Step();

		var destinations = group.Select(u => u.Destination).ToList();
		Assert.All(destinations, d => Assert.NotNull(d));
		Assert.Equal(group.Count, destinations.Distinct().Count());
	}

	[Fact]
	public void StartingForces_StandOnOwnLandNearTheCapital()
	{
		var (_, units) = NewGame();
		var world = WorldGenerator.Generate(1, Map, new WorldGenerator.Settings());

		int perCountry = StartingForces.Default.Sum(f => f.Count);
		foreach (var country in world.Countries)
		{
			var own = units.Units.Where(u => u.OwnerId == country.Id).ToList();
			Assert.Equal(perCountry, own.Count);
			var capital = world.CitiesOf(country.Id).First(c => c.IsCapital);
			foreach (var unit in own)
			{
				Assert.False(Map.IsUnderWater(unit.X, unit.Z));
				Assert.True((unit.X - capital.X).ToDouble() is > -20_000 and < 20_000);
				Assert.True((unit.Z - capital.Z).ToDouble() is > -20_000 and < 20_000);
			}
		}
	}

	[Fact]
	public void SameCommands_GiveSameUnitState()
	{
		ulong Run()
		{
			var (sim, units) = NewGame();
			sim.Schedule(5, new MoveCommand(0, units.Units.Where(u => u.OwnerId == 0).Select(u => u.Id),
				Fixed.FromInt(600_000), Fixed.FromInt(600_000)));
			sim.Step(300);
			return sim.ComputeChecksum();
		}

		Assert.Equal(Run(), Run());
	}

	private static (Simulation Sim, UnitManager Units) NewGame()
	{
		var world = WorldGenerator.Generate(1, Map, new WorldGenerator.Settings());
		var units = new UnitManager(Map, Catalog);
		StartingForces.Place(units, world);
		var sim = new Simulation(seed: 1);
		sim.AddSystem(units);
		return (sim, units);
	}

	private static int TicksToTravel(int metres, UnitType type) =>
		(int)(metres / Catalog.StepPerTick(type).ToDouble()) + 2;

	/// <summary>Nearest water straight east or west of a point.</summary>
	private static (Fixed X, Fixed Z) FindWaterNear(Fixed x, Fixed z)
	{
		for (int km = 1; km < 1200; km++)
		{
			foreach (int sign in new[] { -1, 1 })
			{
				var wx = x + Fixed.FromInt(sign * km * 1000);
				if (wx >= Fixed.Zero && wx <= Map.SizeX && Map.IsUnderWater(wx, z) && Map.IsUnderWater(wx + Fixed.FromInt(sign * 2000), z))
				{
					return (wx + Fixed.FromInt(sign * 2000), z);
				}
			}
		}

		throw new InvalidOperationException("No water found.");
	}

	private static class Assume
	{
		public static void LandAlong(Fixed x0, Fixed z0, Fixed x1, Fixed z1)
		{
			for (int i = 0; i <= 20; i++)
			{
				var t = Fixed.FromRatio(i, 20);
				Assert.False(Map.IsUnderWater(x0 + (x1 - x0) * t, z0 + (z1 - z0) * t), "Test route crosses water; pick another.");
			}
		}
	}
}
