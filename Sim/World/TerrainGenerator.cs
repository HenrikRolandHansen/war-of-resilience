using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.World;

/// <summary>
/// Generates a landscape from a seed using layered value noise, computed entirely in fixed-point
/// so every machine produces the same map.
/// </summary>
public static class TerrainGenerator
{
	public sealed record Settings
	{
		/// <summary>Grid points along X.</summary>
		public int Width { get; init; } = 257;

		/// <summary>Grid points along Z.</summary>
		public int Depth { get; init; } = 257;

		/// <summary>Metres between grid points.</summary>
		public Fixed CellSize { get; init; } = Fixed.FromInt(4);

		/// <summary>Height of the highest possible peak in metres.</summary>
		public Fixed MaxHeight { get; init; } = Fixed.FromInt(90);

		/// <summary>Water level in metres.</summary>
		public Fixed SeaLevel { get; init; } = Fixed.FromInt(12);

		/// <summary>Size of the largest landscape features, in grid cells.</summary>
		public int FeatureSize { get; init; } = 96;

		/// <summary>Number of noise layers. More layers give finer detail.</summary>
		public int Octaves { get; init; } = 5;
	}

	public static Terrain Generate(ulong seed, Settings settings)
	{
		var heights = new Fixed[settings.Width * settings.Depth];

		for (int z = 0; z < settings.Depth; z++)
		{
			for (int x = 0; x < settings.Width; x++)
			{
				var noise = LayeredNoise(seed, x, z, settings.FeatureSize, settings.Octaves);

				// Squaring flattens lowlands and sharpens peaks, which reads more like real terrain.
				heights[z * settings.Width + x] = noise * noise * settings.MaxHeight;
			}
		}

		return new Terrain(settings.Width, settings.Depth, settings.CellSize, settings.SeaLevel, heights);
	}

	/// <summary>Sum of noise octaves, normalised to [0, 1).</summary>
	private static Fixed LayeredNoise(ulong seed, int x, int z, int featureSize, int octaves)
	{
		var sum = Fixed.Zero;
		var totalAmplitude = Fixed.Zero;
		var amplitude = Fixed.One;
		int spacing = featureSize;

		for (int octave = 0; octave < octaves && spacing >= 1; octave++)
		{
			ulong octaveSeed = seed + (ulong)octave * 0x9E3779B97F4A7C15UL;
			sum += ValueNoise(octaveSeed, x, z, spacing) * amplitude;
			totalAmplitude += amplitude;
			amplitude *= Fixed.Half;
			spacing /= 2;
		}

		return sum / totalAmplitude;
	}

	/// <summary>Smoothly interpolated random values on a lattice with the given spacing. Returns [0, 1).</summary>
	private static Fixed ValueNoise(ulong seed, int x, int z, int spacing)
	{
		int cellX = FloorDiv(x, spacing);
		int cellZ = FloorDiv(z, spacing);
		var tx = SmoothStep(Fixed.FromRatio(x - cellX * spacing, spacing));
		var tz = SmoothStep(Fixed.FromRatio(z - cellZ * spacing, spacing));

		var v00 = LatticeValue(seed, cellX, cellZ);
		var v10 = LatticeValue(seed, cellX + 1, cellZ);
		var v01 = LatticeValue(seed, cellX, cellZ + 1);
		var v11 = LatticeValue(seed, cellX + 1, cellZ + 1);

		var top = v00 + (v10 - v00) * tx;
		var bottom = v01 + (v11 - v01) * tx;
		return top + (bottom - top) * tz;
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

	private static int FloorDiv(int a, int b)
	{
		int q = a / b;
		return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
	}
}
