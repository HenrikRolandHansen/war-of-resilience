namespace WarOfResilience.Sim.Core;

/// <summary>
/// What commands and systems can see and use during a tick.
/// </summary>
public sealed class SimContext
{
	private readonly IReadOnlyList<ISimSystem> _systems;

	internal SimContext(DeterministicRandom random, IReadOnlyList<ISimSystem> systems)
	{
		Random = random;
		_systems = systems;
	}

	public long Tick { get; internal set; }

	public DeterministicRandom Random { get; }

	public T GetSystem<T>() where T : ISimSystem
	{
		foreach (var system in _systems)
		{
			if (system is T match)
			{
				return match;
			}
		}

		throw new InvalidOperationException($"No system of type {typeof(T).Name} has been added.");
	}
}
