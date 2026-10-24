namespace WarOfResilience.Sim.Core;

/// <summary>
/// Root of the game simulation. Advances in fixed ticks, independent of rendering.
/// Each tick first applies the commands scheduled for it, then updates every system in order.
/// Same seed + same systems + same commands always give the same state.
/// </summary>
public sealed class Simulation
{
	public const int TicksPerSecond = 10;

	/// <summary>Length of one tick in seconds, as a fixed-point value.</summary>
	public static readonly Fixed TickLength = Fixed.FromRatio(1, TicksPerSecond);

	private readonly List<ISimSystem> _systems = new();
	private readonly CommandQueue _commands = new();
	private readonly SimContext _context;

	public Simulation(ulong seed)
	{
		Seed = seed;
		Random = new DeterministicRandom(seed);
		_context = new SimContext(Random, _systems);
	}

	public ulong Seed { get; }

	/// <summary>Number of ticks completed so far.</summary>
	public long Tick { get; private set; }

	public DeterministicRandom Random { get; }

	public int PendingCommandCount => _commands.Count;

	public void AddSystem(ISimSystem system)
	{
		if (Tick > 0)
		{
			throw new InvalidOperationException("Systems must be added before the first tick.");
		}

		_systems.Add(system);
	}

	public T GetSystem<T>() where T : ISimSystem => _context.GetSystem<T>();

	/// <summary>Schedules a command to run at the start of the given tick (must be in the future).</summary>
	public void Schedule(long tick, ICommand command)
	{
		if (tick <= Tick)
		{
			throw new ArgumentOutOfRangeException(nameof(tick), $"Tick {tick} has already started; the next tick is {Tick + 1}.");
		}

		_commands.Schedule(tick, command);
	}

	/// <summary>Schedules a command for the next tick.</summary>
	public void Schedule(ICommand command) => Schedule(Tick + 1, command);

	public void Step()
	{
		Tick++;
		_context.Tick = Tick;

		foreach (var command in _commands.TakeForTick(Tick))
		{
			command.Apply(_context);
		}

		foreach (var system in _systems)
		{
			system.Update(_context);
		}
	}

	public void Step(int ticks)
	{
		for (int i = 0; i < ticks; i++)
		{
			Step();
		}
	}

	/// <summary>Hash of all deterministic state. Two runs that agree here are in sync.</summary>
	public ulong ComputeChecksum()
	{
		var checksum = new Checksum();
		checksum.Add(Tick);
		checksum.Add(Random.State);
		foreach (var system in _systems)
		{
			system.AddToChecksum(ref checksum);
		}

		return checksum.Value;
	}
}
