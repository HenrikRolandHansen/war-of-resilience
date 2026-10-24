namespace WarOfResilience.Sim.Core;

/// <summary>
/// Order-dependent 64-bit hash (FNV-1a) used to compare simulation state between runs or machines.
/// </summary>
public struct Checksum
{
	private const ulong OffsetBasis = 14695981039346656037UL;
	private const ulong Prime = 1099511628211UL;

	private ulong _hash;
	private bool _started;

	public ulong Value => _started ? _hash : OffsetBasis;

	public void Add(ulong value)
	{
		if (!_started)
		{
			_hash = OffsetBasis;
			_started = true;
		}

		for (int i = 0; i < 8; i++)
		{
			_hash ^= (byte)(value >> (i * 8));
			_hash *= Prime;
		}
	}

	public void Add(long value) => Add((ulong)value);
	public void Add(int value) => Add((ulong)(long)value);
	public void Add(Fixed value) => Add(value.Raw);
}
