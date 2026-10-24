using WarOfResilience.Sim.Core;
using Xunit;

namespace WarOfResilience.Tests;

public class SimulationTests
{
	[Fact]
	public void Step_AdvancesTickByOne()
	{
		var sim = new Simulation();

		sim.Step();
		sim.Step();

		Assert.Equal(2, sim.Tick);
	}
}
