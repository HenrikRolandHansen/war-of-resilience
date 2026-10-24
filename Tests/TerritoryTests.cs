using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.World;
using Xunit;

namespace WarOfResilience.Tests;

public class TerritoryTests
{
	private static readonly Terrain Map = TerrainGenerator.Generate(1, new TerrainGenerator.Settings());
	private static readonly WorldState World = WorldGenerator.Generate(1, Map, new WorldGenerator.Settings());

	[Fact]
	public void Water_BelongsToNobody()
	{
		for (int z = 0; z < Map.Depth; z += 7)
		{
			for (int x = 0; x < Map.Width; x += 7)
			{
				if (Map.HeightAtPoint(x, z) < Map.SeaLevel)
				{
					Assert.Equal(-1, World.Territory.OwnerAtPoint(x, z));
				}
			}
		}
	}

	[Fact]
	public void BothCountries_OwnASubstantialShareOfLand()
	{
		int west = World.Territory.AreaOf(0);
		int east = World.Territory.AreaOf(1);
		int minPercent = new WorldGenerator.Settings().MinLandSharePercent;

		Assert.True(west * 100 >= (west + east) * minPercent, $"Valmark owns only {west} of {west + east} km².");
		Assert.True(east * 100 >= (west + east) * minPercent, $"Korvia owns only {east} of {west + east} km².");
	}

	[Fact]
	public void Border_IsNotAStraightLine()
	{
		// For each row, find where Valmark land meets Korvia land; a natural border wanders.
		var crossings = new List<int>();
		for (int z = 0; z < Map.Depth; z++)
		{
			for (int x = 1; x < Map.Width; x++)
			{
				if (World.Territory.OwnerAtPoint(x - 1, z) == 0 && World.Territory.OwnerAtPoint(x, z) == 1)
				{
					crossings.Add(x);
					break;
				}
			}
		}

		Assert.NotEmpty(crossings);
		Assert.True(crossings.Max() - crossings.Min() > 50, $"Border only spans {crossings.Max() - crossings.Min()} km east-west.");
	}

	[Fact]
	public void Capitals_OwnTheirOwnSite()
	{
		foreach (var capital in World.Cities.Where(c => c.IsCapital))
		{
			Assert.Equal(capital.CountryId, World.Territory.OwnerAt(Map, capital.X, capital.Z));
		}
	}

	[Fact]
	public void Generate_SameInput_GivesSameTerritory()
	{
		var capitals = World.Cities.Where(c => c.IsCapital).ToList();
		var a = TerritoryGenerator.Generate(Map, capitals);
		var b = TerritoryGenerator.Generate(Map, capitals);

		Assert.Equal(ChecksumOf(a), ChecksumOf(b));
	}

	private static ulong ChecksumOf(Territory territory)
	{
		var checksum = new Checksum();
		territory.AddToChecksum(ref checksum);
		return checksum.Value;
	}
}
