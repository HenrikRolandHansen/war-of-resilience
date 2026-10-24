using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.World;
using Xunit;

namespace WarOfResilience.Tests;

public class PowerGridTests
{
	private static readonly Terrain Map = TerrainGenerator.Generate(1, new TerrainGenerator.Settings());

	private static (WorldState World, PowerGrid Grid) Generate(ulong seed)
	{
		var world = WorldGenerator.Generate(seed, Map, new WorldGenerator.Settings());
		return (world, GridGenerator.Generate(seed, Map, world, new GridGenerator.Settings()));
	}

	[Fact]
	public void Generate_GivesEveryCityASubstationAndEveryCountryThreePlants()
	{
		foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
		{
			var (world, grid) = Generate(seed);

			Assert.Equal(world.Cities.Count, grid.Substations.Count());
			foreach (var country in world.Countries)
			{
				var kinds = grid.Plants.Where(p => p.CountryId == country.Id).Select(p => p.Kind).OrderBy(k => k);
				Assert.Equal(new[] { PowerPlantKind.Coal, PowerPlantKind.Gas, PowerPlantKind.Wind }, kinds);
			}
		}
	}

	[Fact]
	public void Generate_PlacesPlantsInOwnCountry_AndLinesStayAtHome()
	{
		foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
		{
			var (world, grid) = Generate(seed);

			foreach (var plant in grid.Plants)
			{
				Assert.Equal(plant.CountryId, world.Territory.OwnerAt(Map, plant.X, plant.Z));
			}

			foreach (var line in grid.Lines)
			{
				Assert.Equal(grid.Nodes[line.FromNodeId].CountryId, grid.Nodes[line.ToNodeId].CountryId);
			}
		}
	}

	[Fact]
	public void AtStart_EveryCityHasFullPower()
	{
		foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
		{
			var (world, grid) = Generate(seed);

			Assert.All(world.Cities, c => Assert.Equal(100, c.PowerPercent));
			Assert.All(grid.Lines, l => Assert.True(l.IsEnergized));
			foreach (var country in world.Countries)
			{
				var power = grid.PowerOf(country.Id);
				Assert.True(power.ProducedMW > power.DemandMW, $"{country.Name} makes {power.ProducedMW} MW for {power.DemandMW} MW.");
			}
		}
	}

	[Fact]
	public void LosingAllPlants_BlacksOutTheCountry_AndLowersMorale()
	{
		var (world, grid) = Generate(1);
		var sim = new Simulation(seed: 1);
		sim.AddSystem(grid);
		sim.AddSystem(world);

		foreach (var plant in grid.Plants.Where(p => p.CountryId == 0))
		{
			plant.IsOperational = false;
		}

		sim.Step(WorldState.MoraleStepTicks * 10);

		foreach (var city in world.Cities)
		{
			if (city.CountryId == 0)
			{
				Assert.Equal(0, city.PowerPercent);
				Assert.Equal(city.BaseMorale - PowerGrid.BlackoutMoralePenalty, city.TargetMorale);
				Assert.True(city.Morale < city.BaseMorale, $"{city.Name} kept morale {city.Morale}.");
			}
			else
			{
				Assert.Equal(100, city.PowerPercent);
			}
		}

		Assert.Equal(0, grid.PowerOf(0).SuppliedMW);
		Assert.All(grid.Lines.Where(l => grid.Nodes[l.FromNodeId].CountryId == 0), l => Assert.False(l.IsEnergized));
	}

	[Fact]
	public void CuttingLinesToASubstation_BlacksOutOnlyThatCity()
	{
		var (world, grid) = Generate(1);
		var target = grid.Substations.First(s => !world.Cities[s.CityId].IsCapital);
		foreach (var line in grid.Lines.Where(l => l.FromNodeId == target.Id || l.ToNodeId == target.Id))
		{
			line.IsOperational = false;
		}

		grid.Update(null!);

		foreach (var city in world.Cities)
		{
			Assert.Equal(city.Id == target.CityId ? 0 : 100, city.PowerPercent);
		}
	}

	[Fact]
	public void TooLittlePower_IsSharedEvenly()
	{
		var world = new WorldState(
			new[] { new Country(0, "Test") },
			new[]
			{
				new City(0, "A", 0, Fixed.Zero, Fixed.Zero, populationThousands: 300, isCapital: true, morale: 70),
				new City(1, "B", 0, Fixed.Zero, Fixed.Zero, populationThousands: 100, isCapital: false, morale: 70),
			},
			new Territory(1, 1, new byte[] { 0 }));
		var nodes = new GridNode[]
		{
			new Substation(0, 0, Fixed.Zero, Fixed.Zero, cityId: 0),
			new Substation(1, 0, Fixed.Zero, Fixed.Zero, cityId: 1),
			new PowerPlant(2, 0, Fixed.Zero, Fixed.Zero, PowerPlantKind.Gas, capacityMW: 200),
		};
		var lines = new[] { new PowerLine(0, 0, 2), new PowerLine(1, 1, 2) };

		var grid = new PowerGrid(world, nodes, lines);

		Assert.Equal(50, world.Cities[0].PowerPercent);
		Assert.Equal(50, world.Cities[1].PowerPercent);
		Assert.Equal(70 - PowerGrid.BlackoutMoralePenalty / 2, world.Cities[0].TargetMorale);
		Assert.Equal(200, grid.PowerOf(0).SuppliedMW);
	}

	[Fact]
	public void Blackout_HalvesIncome()
	{
		var city = new City(0, "Test", 0, Fixed.Zero, Fixed.Zero, populationThousands: 1000, isCapital: false, morale: 50);
		var full = WorldState.IncomePerTick(city);

		city.PowerPercent = 0;

		Assert.Equal(full * Fixed.Half, WorldState.IncomePerTick(city));
	}

	[Fact]
	public void Generate_SameSeed_GivesSameGrid()
	{
		var a = Generate(7).Grid;
		var b = Generate(7).Grid;

		var checksumA = new Checksum();
		a.AddToChecksum(ref checksumA);
		var checksumB = new Checksum();
		b.AddToChecksum(ref checksumB);
		Assert.Equal(checksumA.Value, checksumB.Value);
	}
}
