using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.World;
using Xunit;

namespace WarOfResilience.Tests;

public class WorldTests
{
	// The full-size map is used on purpose: placement rules are tuned for it.
	private static readonly Terrain Map = TerrainGenerator.Generate(1, new TerrainGenerator.Settings());
	private static readonly WorldGenerator.Settings Defaults = new();

	[Fact]
	public void Generate_GivesTwoCountriesWithOneCapitalAndTwoToThreeCities()
	{
		var world = WorldGenerator.Generate(1, Map, Defaults);

		Assert.Equal(2, world.Countries.Count);
		foreach (var country in world.Countries)
		{
			var cities = world.CitiesOf(country.Id).ToList();
			Assert.Single(cities, c => c.IsCapital);
			Assert.InRange(cities.Count(c => !c.IsCapital), 2, 3);
		}
	}

	[Fact]
	public void Generate_PlacesCitiesOnHabitableLandOnTheirOwnSide()
	{
		foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
		{
			var world = WorldGenerator.Generate(seed, Map, Defaults);

			foreach (var city in world.Cities)
			{
				var aboveSea = Map.HeightAt(city.X, city.Z) - Map.SeaLevel;
				Assert.True(aboveSea >= Defaults.MinHeightAboveSea, $"{city.Name} is too low ({aboveSea} m above sea).");
				Assert.True(aboveSea <= Defaults.MaxHeightAboveSea, $"{city.Name} is too high ({aboveSea} m above sea).");

				if (city.CountryId == 0)
				{
					Assert.True(city.X <= world.BorderX - Defaults.BorderMargin, $"{city.Name} is too close to the border.");
				}
				else
				{
					Assert.True(city.X >= world.BorderX + Defaults.BorderMargin, $"{city.Name} is too close to the border.");
				}
			}
		}
	}

	[Fact]
	public void Generate_KeepsCitiesApart()
	{
		var world = WorldGenerator.Generate(1, Map, Defaults);
		double minKm = Defaults.MinCityDistance.ToDouble() / 1000;

		foreach (var a in world.Cities)
		{
			foreach (var b in world.Cities.Where(b => b.Id > a.Id))
			{
				double dx = (a.X - b.X).ToDouble() / 1000;
				double dz = (a.Z - b.Z).ToDouble() / 1000;
				Assert.True(Math.Sqrt(dx * dx + dz * dz) >= minKm, $"{a.Name} and {b.Name} are too close.");
			}
		}
	}

	[Fact]
	public void Generate_SameSeed_GivesSameWorld()
	{
		var a = WorldGenerator.Generate(9, Map, Defaults);
		var b = WorldGenerator.Generate(9, Map, Defaults);

		Assert.Equal(ChecksumOf(a), ChecksumOf(b));
	}

	[Fact]
	public void Cities_EarnIncomeForTheirCountry()
	{
		var world = WorldGenerator.Generate(1, Map, Defaults);
		var sim = new Simulation(seed: 1);
		sim.AddSystem(world);
		var startMoney = world.Countries.Select(c => c.Money).ToList();

		sim.Step(Simulation.TicksPerSecond * 10);

		for (int i = 0; i < world.Countries.Count; i++)
		{
			Assert.True(world.Countries[i].Money > startMoney[i], $"{world.Countries[i].Name} earned nothing.");
		}
	}

	[Fact]
	public void Income_ScalesWithPopulationAndMorale()
	{
		var city = new City(0, "Test", 0, Fixed.Zero, Fixed.Zero, populationThousands: 1000, isCapital: false, morale: 50);

		// 1,000 thousand people at 1 credit/s per 10 thousand, 50 % morale = 50 credits/s = 5 per tick.
		Assert.Equal(Fixed.FromInt(5), WorldState.IncomePerTick(city));
	}

	[Fact]
	public void Morale_MovesOneStepTowardTarget()
	{
		var city = new City(0, "Test", 0, Fixed.Zero, Fixed.Zero, 100, isCapital: false, morale: 70) { TargetMorale = 40 };
		var world = new WorldState(new[] { new Country(0, "Test") }, new[] { city }, Fixed.Zero);
		var sim = new Simulation(seed: 1);
		sim.AddSystem(world);

		sim.Step(WorldState.MoraleStepTicks * 5);

		Assert.Equal(65, city.Morale);
	}

	[Fact]
	public void World_IsPartOfSimulationChecksum()
	{
		var a = new Simulation(seed: 1);
		a.AddSystem(WorldGenerator.Generate(1, Map, Defaults));
		var b = new Simulation(seed: 1);
		b.AddSystem(WorldGenerator.Generate(2, Map, Defaults));

		Assert.NotEqual(a.ComputeChecksum(), b.ComputeChecksum());
	}

	private static ulong ChecksumOf(WorldState world)
	{
		var checksum = new Checksum();
		world.AddToChecksum(ref checksum);
		return checksum.Value;
	}
}
