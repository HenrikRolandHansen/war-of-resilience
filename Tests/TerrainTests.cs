using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.World;
using Xunit;

namespace WarOfResilience.Tests;

public class TerrainTests
{
	private static readonly TerrainGenerator.Settings SmallMap = new()
	{
		Width = 65,
		Depth = 49,
		CellSize = Fixed.FromInt(4),
		MaxHeight = Fixed.FromInt(90),
		SeaLevel = Fixed.FromInt(12),
		FeatureSize = 32,
		Octaves = 5,
	};

	[Fact]
	public void Generate_SameSeed_GivesIdenticalTerrain()
	{
		var a = TerrainGenerator.Generate(7, SmallMap);
		var b = TerrainGenerator.Generate(7, SmallMap);

		Assert.Equal(ChecksumOf(a), ChecksumOf(b));
	}

	[Fact]
	public void Generate_KnownSeed_GivesKnownChecksum()
	{
		// Pins the generator: if this changes, every map made from a saved seed changes too.
		var terrain = TerrainGenerator.Generate(1, SmallMap);

		Assert.Equal(KnownChecksumSeed1, ChecksumOf(terrain));
	}

	[Fact]
	public void Generate_DifferentSeed_GivesDifferentTerrain()
	{
		var a = TerrainGenerator.Generate(7, SmallMap);
		var b = TerrainGenerator.Generate(8, SmallMap);

		Assert.NotEqual(ChecksumOf(a), ChecksumOf(b));
	}

	[Fact]
	public void Generate_HeightsStayWithinMaxHeight()
	{
		var terrain = TerrainGenerator.Generate(3, SmallMap);

		for (int z = 0; z < terrain.Depth; z++)
		{
			for (int x = 0; x < terrain.Width; x++)
			{
				var h = terrain.HeightAtPoint(x, z);
				Assert.True(h >= Fixed.Zero && h <= SmallMap.MaxHeight, $"Height {h} at ({x},{z}) is out of range.");
			}
		}
	}

	[Fact]
	public void Generate_HasBothLandAndWater()
	{
		var terrain = TerrainGenerator.Generate(1, new TerrainGenerator.Settings());
		int under = 0;
		int above = 0;

		for (int z = 0; z < terrain.Depth; z++)
		{
			for (int x = 0; x < terrain.Width; x++)
			{
				if (terrain.HeightAtPoint(x, z) < terrain.SeaLevel)
				{
					under++;
				}
				else
				{
					above++;
				}
			}
		}

		Assert.True(under > 0, "Expected some water.");
		Assert.True(above > under, "Expected mostly land.");
	}

	[Fact]
	public void HeightAt_GridPoint_ReturnsStoredHeight()
	{
		var terrain = FlatSlope();

		Assert.Equal(Fixed.FromInt(2), terrain.HeightAt(Fixed.FromInt(4), Fixed.Zero));
	}

	[Fact]
	public void HeightAt_BetweenPoints_Interpolates()
	{
		var terrain = FlatSlope();

		Assert.Equal(Fixed.FromInt(1), terrain.HeightAt(Fixed.FromInt(2), Fixed.FromInt(3)));
	}

	[Fact]
	public void HeightAt_OutsideMap_ClampsToEdge()
	{
		var terrain = FlatSlope();

		Assert.Equal(Fixed.Zero, terrain.HeightAt(Fixed.FromInt(-50), Fixed.Zero));
		Assert.Equal(Fixed.FromInt(4), terrain.HeightAt(Fixed.FromInt(500), Fixed.FromInt(500)));
	}

	[Fact]
	public void Terrain_IsPartOfSimulationChecksum()
	{
		var a = new Simulation(seed: 1);
		a.AddSystem(TerrainGenerator.Generate(1, SmallMap));
		var b = new Simulation(seed: 1);
		b.AddSystem(TerrainGenerator.Generate(2, SmallMap));

		Assert.NotEqual(a.ComputeChecksum(), b.ComputeChecksum());
	}

	/// <summary>3x2 grid, 4 m cells, height rising 2 m per cell along X.</summary>
	private static Terrain FlatSlope()
	{
		var heights = new[]
		{
			Fixed.FromInt(0), Fixed.FromInt(2), Fixed.FromInt(4),
			Fixed.FromInt(0), Fixed.FromInt(2), Fixed.FromInt(4),
		};
		return new Terrain(3, 2, Fixed.FromInt(4), Fixed.Zero, heights);
	}

	private static ulong ChecksumOf(Terrain terrain)
	{
		var checksum = new Checksum();
		terrain.AddToChecksum(ref checksum);
		return checksum.Value;
	}

	private const ulong KnownChecksumSeed1 = 15741341514057029468UL;
}
