using WarOfResilience.Sim.Core;
using Xunit;

namespace WarOfResilience.Tests;

public class FixedTests
{
	[Fact]
	public void Arithmetic_IsExact()
	{
		var a = Fixed.FromRatio(3, 2);
		var b = Fixed.FromInt(4);

		Assert.Equal(Fixed.FromRatio(11, 2), a + b);
		Assert.Equal(Fixed.FromRatio(-5, 2), a - b);
		Assert.Equal(Fixed.FromInt(6), a * b);
		Assert.Equal(Fixed.FromRatio(3, 8), a / b);
	}

	[Fact]
	public void Multiply_LargeValues_DoesNotOverflow()
	{
		var a = Fixed.FromInt(1_000_000);

		Assert.Equal(Fixed.FromInt(1_000_000), a * Fixed.One);
		Assert.Equal(1_000_000_000_000.0, (a * a).ToDouble());
	}

	[Fact]
	public void FloorToInt_RoundsDown()
	{
		Assert.Equal(1, Fixed.FromRatio(7, 4).FloorToInt());
		Assert.Equal(-2, Fixed.FromRatio(-7, 4).FloorToInt());
	}

	[Fact]
	public void TickLength_IsOneTenthSecond()
	{
		Assert.Equal(0.1, Simulation.TickLength.ToDouble(), precision: 4);
	}
}
