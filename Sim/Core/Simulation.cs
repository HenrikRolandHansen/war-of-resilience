namespace WarOfResilience.Sim.Core;

/// <summary>
/// Root of the game simulation. Advances in fixed ticks, independent of rendering.
/// </summary>
public sealed class Simulation
{
	public const int TicksPerSecond = 10;

	public long Tick { get; private set; }

	public void Step()
	{
		Tick++;
	}
}
