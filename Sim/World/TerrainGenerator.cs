using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.World;

/// <summary>
/// Generates a landscape from a seed using layered value noise, computed entirely in fixed-point
/// so every machine produces the same map. The default is a 1200 x 1200 km region with 1 km between height points.
/// </summary>
public static class TerrainGenerator
{
	public sealed record Settings
	{
		/// <summary>Grid points along X.</summary>
		public int Width { get; init; } = 1201;

		/// <summary>Grid points along Z.</summary>
		public int Depth { get; init; } = 1201;

		/// <summary>Metres between grid points.</summary>
		public Fixed CellSize { get; init; } = Fixed.FromInt(1000);

		/// <summary>Height of the highest possible peak in metres.</summary>
		public Fixed MaxHeight { get; init; } = Fixed.FromInt(2500);

		/// <summary>Water level in metres.</summary>
		public Fixed SeaLevel { get; init; } = Fixed.FromInt(330);

		/// <summary>Size of the largest landscape features, in grid cells.</summary>
		public int FeatureSize { get; init; } = 400;

		/// <summary>Number of noise layers. More layers give finer detail.</summary>
		public int Octaves { get; init; } = 9;
	}

	public static Terrain Generate(ulong seed, Settings settings)
	{
		int width = settings.Width;
		int depth = settings.Depth;
		var sum = new Fixed[width * depth];
		var totalAmplitude = Fixed.Zero;
		var amplitude = Fixed.One;
		int spacing = settings.FeatureSize;

		// Layered value noise: each octave halves the feature size and the amplitude.
		for (int octave = 0; octave < settings.Octaves && spacing >= 1; octave++)
		{
			ulong octaveSeed = seed + (ulong)octave * 0x9E3779B97F4A7C15UL;
			AddOctave(sum, width, depth, octaveSeed, spacing, amplitude);
			totalAmplitude += amplitude;
			amplitude *= Fixed.Half;
			spacing /= 2;
		}

		var heights = new Fixed[width * depth];
		for (int i = 0; i < heights.Length; i++)
		{
			var noise = sum[i] / totalAmplitude;

			// Squaring flattens lowlands and sharpens peaks, which reads more like real terrain.
			heights[i] = noise * noise * settings.MaxHeight;
		}

		return new Terrain(width, depth, settings.CellSize, settings.SeaLevel, heights);
	}

	private static void AddOctave(Fixed[] sum, int width, int depth, ulong seed, int spacing, Fixed amplitude)
	{
		// Lattice values are cached per octave; computing them per point is far slower on big maps.
		int latticeWidth = width / spacing + 2;
		int latticeDepth = depth / spacing + 2;
		var lattice = new Fixed[latticeWidth * latticeDepth];
		for (int z = 0; z < latticeDepth; z++)
		{
			for (int x = 0; x < latticeWidth; x++)
			{
				lattice[z * latticeWidth + x] = LatticeValue(seed, x, z);
			}
		}

		for (int z = 0; z < depth; z++)
		{
			int cellZ = z / spacing;
			var tz = SmoothStep(Fixed.FromRatio(z - cellZ * spacing, spacing));
			for (int x = 0; x < width; x++)
			{
				int cellX = x / spacing;
				var tx = SmoothStep(Fixed.FromRatio(x - cellX * spacing, spacing));

				var v00 = lattice[cellZ * latticeWidth + cellX];
				var v10 = lattice[cellZ * latticeWidth + cellX + 1];
				var v01 = lattice[(cellZ + 1) * latticeWidth + cellX];
				var v11 = lattice[(cellZ + 1) * latticeWidth + cellX + 1];

				var top = v00 + (v10 - v00) * tx;
				var bottom = v01 + (v11 - v01) * tx;
				sum[z * width + x] += (top + (bottom - top) * tz) * amplitude;
			}
		}
	}

	private static Fixed LatticeValue(ulong seed, int x, int z)
	{
		ulong hash = seed;
		hash ^= (ulong)(uint)x * 0xC2B2AE3D27D4EB4FUL;
		hash ^= (ulong)(uint)z * 0x165667B19E3779F9UL;
		var random = new DeterministicRandom(hash);
		return random.NextFixed();
	}

	private static Fixed SmoothStep(Fixed t)
	{
		var three = Fixed.FromInt(3);
		var two = Fixed.FromInt(2);
		return t * t * (three - two * t);
	}
}
