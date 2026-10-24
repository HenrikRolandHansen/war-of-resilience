using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.World;

/// <summary>
/// Which country owns each terrain grid point. Water and unreachable land belong to nobody.
/// Uses the same grid as <see cref="Terrain"/>.
/// </summary>
public sealed class Territory
{
	public const byte Nobody = byte.MaxValue;

	private readonly byte[] _owners;

	public Territory(int width, int depth, byte[] owners)
	{
		if (owners.Length != width * depth)
		{
			throw new ArgumentException($"Expected {width * depth} owners, got {owners.Length}.", nameof(owners));
		}

		Width = width;
		Depth = depth;
		_owners = owners;
	}

	public int Width { get; }

	public int Depth { get; }

	/// <summary>Owner country id at a grid point, or -1 for nobody. Indices outside the grid return -1.</summary>
	public int OwnerAtPoint(int x, int z)
	{
		if (x < 0 || z < 0 || x >= Width || z >= Depth)
		{
			return -1;
		}

		byte owner = _owners[z * Width + x];
		return owner == Nobody ? -1 : owner;
	}

	/// <summary>Owner at a world position (nearest grid point), or -1 for nobody.</summary>
	public int OwnerAt(Terrain terrain, Fixed x, Fixed z)
	{
		int gx = (x / terrain.CellSize + Fixed.Half).FloorToInt();
		int gz = (z / terrain.CellSize + Fixed.Half).FloorToInt();
		return OwnerAtPoint(gx, gz);
	}

	/// <summary>Number of grid points (square kilometres on the default map) a country owns.</summary>
	public int AreaOf(int countryId)
	{
		int count = 0;
		foreach (byte owner in _owners)
		{
			if (owner == countryId)
			{
				count++;
			}
		}

		return count;
	}

	public void AddToChecksum(ref Checksum checksum)
	{
		checksum.Add(Width);
		checksum.Add(Depth);

		// Pack eight owners per value to keep the checksum fast on large maps.
		ulong packed = 0;
		for (int i = 0; i < _owners.Length; i++)
		{
			packed = (packed << 8) | _owners[i];
			if (i % 8 == 7)
			{
				checksum.Add(packed);
				packed = 0;
			}
		}

		checksum.Add(packed);
	}
}
