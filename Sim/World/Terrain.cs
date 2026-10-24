using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.World;

/// <summary>
/// The landscape as a regular height grid. World X/Z run from 0 to (Width-1) * CellSize;
/// heights are in metres. Static after generation, but part of the checksum so two machines
/// can confirm they generated the same map.
/// </summary>
public sealed class Terrain : ISimSystem
{
	private readonly Fixed[] _heights;

	public Terrain(int width, int depth, Fixed cellSize, Fixed seaLevel, Fixed[] heights)
	{
		if (width < 2 || depth < 2)
		{
			throw new ArgumentOutOfRangeException(nameof(width), "Terrain needs at least 2x2 points.");
		}

		if (heights.Length != width * depth)
		{
			throw new ArgumentException($"Expected {width * depth} heights, got {heights.Length}.", nameof(heights));
		}

		Width = width;
		Depth = depth;
		CellSize = cellSize;
		SeaLevel = seaLevel;
		_heights = heights;
	}

	/// <summary>Number of grid points along X.</summary>
	public int Width { get; }

	/// <summary>Number of grid points along Z.</summary>
	public int Depth { get; }

	/// <summary>Distance between grid points in metres.</summary>
	public Fixed CellSize { get; }

	/// <summary>Height of the water surface. Ground below it is under water.</summary>
	public Fixed SeaLevel { get; }

	public Fixed SizeX => CellSize * Fixed.FromInt(Width - 1);

	public Fixed SizeZ => CellSize * Fixed.FromInt(Depth - 1);

	/// <summary>Height at a grid point. Indices are clamped to the grid.</summary>
	public Fixed HeightAtPoint(int x, int z)
	{
		x = Math.Clamp(x, 0, Width - 1);
		z = Math.Clamp(z, 0, Depth - 1);
		return _heights[z * Width + x];
	}

	/// <summary>Ground height at a world position, bilinearly interpolated. Positions outside the map are clamped to its edge.</summary>
	public Fixed HeightAt(Fixed x, Fixed z)
	{
		var gx = Fixed.Max(Fixed.Zero, Fixed.Min(x / CellSize, Fixed.FromInt(Width - 1)));
		var gz = Fixed.Max(Fixed.Zero, Fixed.Min(z / CellSize, Fixed.FromInt(Depth - 1)));

		int ix = gx.FloorToInt();
		int iz = gz.FloorToInt();
		var fx = gx - Fixed.FromInt(ix);
		var fz = gz - Fixed.FromInt(iz);

		var h00 = HeightAtPoint(ix, iz);
		var h10 = HeightAtPoint(ix + 1, iz);
		var h01 = HeightAtPoint(ix, iz + 1);
		var h11 = HeightAtPoint(ix + 1, iz + 1);

		var top = h00 + (h10 - h00) * fx;
		var bottom = h01 + (h11 - h01) * fx;
		return top + (bottom - top) * fz;
	}

	public bool IsUnderWater(Fixed x, Fixed z) => HeightAt(x, z) < SeaLevel;

	public void Update(SimContext context)
	{
		// Static for now. Craters or flooding can change heights here later.
	}

	public void AddToChecksum(ref Checksum checksum)
	{
		checksum.Add(Width);
		checksum.Add(Depth);
		checksum.Add(CellSize);
		checksum.Add(SeaLevel);
		foreach (var height in _heights)
		{
			checksum.Add(height);
		}
	}
}
