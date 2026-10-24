namespace WarOfResilience.Sim.Core;

/// <summary>
/// Holds commands until the tick they are scheduled for. Commands for the same tick always run
/// in the same order: by player id, then by the order they were scheduled.
/// </summary>
public sealed class CommandQueue
{
	private readonly SortedDictionary<long, List<ScheduledCommand>> _byTick = new();
	private long _nextSequence;

	public int Count { get; private set; }

	public void Schedule(long tick, ICommand command)
	{
		if (!_byTick.TryGetValue(tick, out var list))
		{
			list = new List<ScheduledCommand>();
			_byTick.Add(tick, list);
		}

		list.Add(new ScheduledCommand(command, _nextSequence++));
		Count++;
	}

	/// <summary>Removes and returns the commands for the given tick, in execution order.</summary>
	public IReadOnlyList<ICommand> TakeForTick(long tick)
	{
		if (!_byTick.Remove(tick, out var list))
		{
			return Array.Empty<ICommand>();
		}

		list.Sort(static (a, b) =>
		{
			int byPlayer = a.Command.PlayerId.CompareTo(b.Command.PlayerId);
			return byPlayer != 0 ? byPlayer : a.Sequence.CompareTo(b.Sequence);
		});

		Count -= list.Count;
		return list.ConvertAll(static s => s.Command);
	}

	private readonly record struct ScheduledCommand(ICommand Command, long Sequence);
}
