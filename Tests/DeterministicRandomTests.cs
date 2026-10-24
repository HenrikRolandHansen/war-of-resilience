using WarOfResilience.Sim.Core;
using Xunit;

namespace WarOfResilience.Tests;

public class DeterministicRandomTests
{
	[Fact]
	public void SameSeed_GivesSameSequence()
	{
		var a = new DeterministicRandom(123);
		var b = new DeterministicRandom(123);

		for (int i = 0; i < 1000; i++)
		{
			Assert.Equal(a.NextULong(), b.NextULong());
		}
	}

	[Fact]
	public void KnownSeed_GivesKnownFirstValue()
	{
		// Pins the algorithm: if this changes, saved games and replays break.
		var random = new DeterministicRandom(0);

		Assert.Equal(0xE220A8397B1DCDAFUL, random.NextULong());
	}

	[Fact]
	public void NextInt_StaysInRange()
	{
		var random = new DeterministicRandom(5);

		for (int i = 0; i < 10_000; i++)
		{
			int value = random.NextInt(-3, 4);
			Assert.InRange(value, -3, 3);
		}
	}

	[Fact]
	public void NextFixed_IsBetweenZeroAndOne()
	{
		var random = new DeterministicRandom(9);

		for (int i = 0; i < 10_000; i++)
		{
			var value = random.NextFixed();
			Assert.True(value >= Fixed.Zero && value < Fixed.One);
		}
	}
}
