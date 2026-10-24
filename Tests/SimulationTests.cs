using WarOfResilience.Sim.Core;
using Xunit;

namespace WarOfResilience.Tests;

public class SimulationTests
{
	[Fact]
	public void Step_AdvancesTickByOne()
	{
		var sim = new Simulation(seed: 1);

		sim.Step();
		sim.Step();

		Assert.Equal(2, sim.Tick);
	}

	[Fact]
	public void SameSeedAndCommands_GiveIdenticalState()
	{
		var first = RunScenario(seed: 42);
		var second = RunScenario(seed: 42);

		Assert.Equal(first.ComputeChecksum(), second.ComputeChecksum());
		Assert.Equal(first.GetSystem<CounterSystem>().Value, second.GetSystem<CounterSystem>().Value);
	}

	[Fact]
	public void DifferentSeed_GivesDifferentState()
	{
		var first = RunScenario(seed: 42);
		var second = RunScenario(seed: 43);

		Assert.NotEqual(first.ComputeChecksum(), second.ComputeChecksum());
	}

	[Fact]
	public void DifferentCommands_GiveDifferentState()
	{
		var first = RunScenario(seed: 42);
		var second = RunScenario(seed: 42, extraCommand: true);

		Assert.NotEqual(first.ComputeChecksum(), second.ComputeChecksum());
	}

	[Fact]
	public void SchedulingOrderAcrossPlayers_DoesNotChangeResult()
	{
		var first = new Simulation(seed: 7);
		first.AddSystem(new CounterSystem());
		first.Schedule(5, new MultiplyCommand(playerId: 2, factor: 3));
		first.Schedule(5, new AddCommand(playerId: 1, amount: 10));
		first.Step(10);

		var second = new Simulation(seed: 7);
		second.AddSystem(new CounterSystem());
		second.Schedule(5, new AddCommand(playerId: 1, amount: 10));
		second.Schedule(5, new MultiplyCommand(playerId: 2, factor: 3));
		second.Step(10);

		Assert.Equal(first.ComputeChecksum(), second.ComputeChecksum());
	}

	[Fact]
	public void Command_RunsAtItsScheduledTick()
	{
		var sim = new Simulation(seed: 1);
		var counter = new CounterSystem { RandomStep = false };
		sim.AddSystem(counter);
		sim.Schedule(3, new AddCommand(playerId: 1, amount: 5));

		sim.Step(2);
		Assert.Equal(0, counter.Value);
		Assert.Equal(1, sim.PendingCommandCount);

		sim.Step();
		Assert.Equal(5, counter.Value);
		Assert.Equal(0, sim.PendingCommandCount);
	}

	[Fact]
	public void Schedule_InThePast_Throws()
	{
		var sim = new Simulation(seed: 1);
		sim.Step(3);

		Assert.Throws<ArgumentOutOfRangeException>(() => sim.Schedule(3, new AddCommand(1, 1)));
	}

	private static Simulation RunScenario(ulong seed, bool extraCommand = false)
	{
		var sim = new Simulation(seed);
		sim.AddSystem(new CounterSystem());
		sim.Schedule(10, new AddCommand(playerId: 1, amount: 100));
		sim.Schedule(20, new MultiplyCommand(playerId: 2, factor: 2));
		if (extraCommand)
		{
			sim.Schedule(30, new AddCommand(playerId: 1, amount: 1));
		}

		sim.Step(1000);
		return sim;
	}

	private sealed class CounterSystem : ISimSystem
	{
		public long Value { get; set; }

		public bool RandomStep { get; init; } = true;

		public void Update(SimContext context)
		{
			if (RandomStep)
			{
				Value += context.Random.NextInt(0, 10);
			}
		}

		public void AddToChecksum(ref Checksum checksum) => checksum.Add(Value);
	}

	private sealed record AddCommand(int PlayerId, long Amount) : ICommand
	{
		public AddCommand(int playerId, int amount) : this(playerId, (long)amount) { }

		public void Apply(SimContext context) => context.GetSystem<CounterSystem>().Value += Amount;
	}

	private sealed record MultiplyCommand(int PlayerId, long Factor) : ICommand
	{
		public MultiplyCommand(int playerId, int factor) : this(playerId, (long)factor) { }

		public void Apply(SimContext context) => context.GetSystem<CounterSystem>().Value *= Factor;
	}
}
