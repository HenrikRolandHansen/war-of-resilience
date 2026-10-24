namespace WarOfResilience.Sim.Core;

/// <summary>
/// Seeded random number generator (SplitMix64) that gives identical sequences on every machine.
/// Use this instead of System.Random anywhere in Sim.
/// </summary>
public sealed class DeterministicRandom
{
	private ulong _state;

	public DeterministicRandom(ulong seed)
	{
		_state = seed;
	}

	/// <summary>Current internal state, for checksums and save games.</summary>
	public ulong State => _state;

	public ulong NextULong()
	{
		ulong z = _state += 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return z ^ (z >> 31);
	}

	/// <summary>Returns an integer in [minInclusive, maxExclusive).</summary>
	public int NextInt(int minInclusive, int maxExclusive)
	{
		if (maxExclusive <= minInclusive)
		{
			throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
		}

		ulong range = (ulong)((long)maxExclusive - minInclusive);
		return (int)((long)minInclusive + (long)(NextULong() % range));
	}

	/// <summary>Returns true with the given probability, expressed in percent (0-100).</summary>
	public bool Chance(int percent)
	{
		return NextInt(0, 100) < percent;
	}

	/// <summary>Returns a fixed-point value in [0, 1).</summary>
	public Fixed NextFixed()
	{
		return Fixed.FromRaw((long)(NextULong() >> (64 - Fixed.FractionBits)));
	}
}
