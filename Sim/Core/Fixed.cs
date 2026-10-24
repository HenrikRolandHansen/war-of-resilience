namespace WarOfResilience.Sim.Core;

/// <summary>
/// Fixed-point number with 16 fractional bits. Gives bit-identical results on every machine,
/// which float and double do not guarantee. Use for all positions, speeds and quantities in Sim.
/// </summary>
public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
{
	public const int FractionBits = 16;
	private const long OneRaw = 1L << FractionBits;

	public static readonly Fixed Zero = new(0);
	public static readonly Fixed One = new(OneRaw);
	public static readonly Fixed Half = new(OneRaw / 2);

	public long Raw { get; }

	private Fixed(long raw)
	{
		Raw = raw;
	}

	public static Fixed FromRaw(long raw) => new(raw);

	public static Fixed FromInt(int value) => new((long)value << FractionBits);

	/// <summary>Exact ratio numerator / denominator, e.g. FromRatio(3, 2) = 1.5.</summary>
	public static Fixed FromRatio(int numerator, int denominator)
	{
		return new(((long)numerator << FractionBits) / denominator);
	}

	/// <summary>For rendering only. Never feed the result back into Sim.</summary>
	public double ToDouble() => (double)Raw / OneRaw;

	/// <summary>Rounds toward negative infinity.</summary>
	public int FloorToInt() => (int)(Raw >> FractionBits);

	public static Fixed operator +(Fixed a, Fixed b) => new(a.Raw + b.Raw);
	public static Fixed operator -(Fixed a, Fixed b) => new(a.Raw - b.Raw);
	public static Fixed operator -(Fixed a) => new(-a.Raw);
	public static Fixed operator *(Fixed a, Fixed b) => new((long)(((Int128)a.Raw * b.Raw) >> FractionBits));
	public static Fixed operator /(Fixed a, Fixed b) => new((long)(((Int128)a.Raw << FractionBits) / b.Raw));

	public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
	public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
	public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
	public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
	public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
	public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

	public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;
	public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;
	public static Fixed Abs(Fixed a) => a.Raw < 0 ? -a : a;

	/// <summary>Square root, rounded down. Exact integer maths, so every machine gets the same result.</summary>
	public static Fixed Sqrt(Fixed a)
	{
		if (a.Raw <= 0)
		{
			return Zero;
		}

		// sqrt(raw / 2^16) * 2^16 = sqrt(raw * 2^16).
		UInt128 value = (UInt128)(ulong)a.Raw << FractionBits;
		UInt128 root = 0;
		UInt128 bit = (UInt128)1 << 126;
		while (bit > value)
		{
			bit >>= 2;
		}

		while (bit != 0)
		{
			if (value >= root + bit)
			{
				value -= root + bit;
				root = (root >> 1) + bit;
			}
			else
			{
				root >>= 1;
			}

			bit >>= 2;
		}

		return new((long)root);
	}

	public bool Equals(Fixed other) => Raw == other.Raw;
	public override bool Equals(object? obj) => obj is Fixed other && Equals(other);
	public override int GetHashCode() => Raw.GetHashCode();
	public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);
	public override string ToString() => ToDouble().ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
