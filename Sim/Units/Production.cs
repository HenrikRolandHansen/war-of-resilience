using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Sim.Units;

/// <summary>
/// One factory per country, next to its capital. Orders are paid when placed and built one at a
/// time; finished units appear at the capital's staging point. A country whose capital is gone
/// cannot build.
/// </summary>
public sealed class Production : ISimSystem
{
	/// <summary>Orders a factory accepts at once.</summary>
	public const int MaxQueue = 8;

	private readonly UnitManager _units;
	private readonly WorldState _world;
	private readonly Queue<UnitType>[] _queues;
	private readonly int[] _progress;
	private readonly int[] _built;

	public Production(UnitManager units, WorldState world)
	{
		_units = units;
		_world = world;
		_queues = world.Countries.Select(_ => new Queue<UnitType>()).ToArray();
		_progress = new int[world.Countries.Count];
		_built = new int[world.Countries.Count];
	}

	public IReadOnlyCollection<UnitType> QueueOf(int countryId) => _queues[countryId];

	/// <summary>How far the unit at the front of the queue is, 0 to 1.</summary>
	public double ProgressOf(int countryId) =>
		_queues[countryId].TryPeek(out var type) ? (double)_progress[countryId] / type.BuildTicks : 0;

	/// <summary>Whether a country can order a unit now.</summary>
	public bool CanOrder(int countryId, UnitType type) =>
		countryId >= 0 && countryId < _queues.Length
		&& _queues[countryId].Count < MaxQueue
		&& _world.Countries[countryId].Money >= Fixed.FromInt(type.Cost);

	/// <summary>Pays for and queues a unit. Returns false if the country cannot afford it or the queue is full.</summary>
	internal bool Order(int countryId, UnitType type)
	{
		if (!CanOrder(countryId, type))
		{
			return false;
		}

		_world.Countries[countryId].Money -= Fixed.FromInt(type.Cost);
		_queues[countryId].Enqueue(type);
		return true;
	}

	public void Update(SimContext context)
	{
		for (int country = 0; country < _queues.Length; country++)
		{
			if (!_queues[country].TryPeek(out var type) || ++_progress[country] < type.BuildTicks)
			{
				continue;
			}

			_queues[country].Dequeue();
			_progress[country] = 0;
			var capital = _world.CitiesOf(country).FirstOrDefault(c => c.IsCapital);
			if (capital == null)
			{
				continue;
			}

			// Finished units park in a 4 x 4 grid next to the starting forces, then start over.
			var (x, z) = StartingForces.StagingPoint(_units.Terrain, _world.Territory, capital);
			int spot = _built[country]++ % 16;
			var px = x + Fixed.FromInt(spot % 4) * UnitManager.FormationSpacing - Fixed.FromInt(400);
			var pz = z + Fixed.FromInt(spot / 4) * UnitManager.FormationSpacing;
			_units.Spawn(type, country, px, pz);
		}
	}

	public void AddToChecksum(ref Checksum checksum)
	{
		for (int country = 0; country < _queues.Length; country++)
		{
			checksum.Add(_progress[country]);
			checksum.Add(_built[country]);
			foreach (var type in _queues[country])
			{
				checksum.Add(type.Index);
			}
		}
	}
}

/// <summary>Orders one unit from the player's factory.</summary>
public sealed class BuildCommand : ICommand
{
	public BuildCommand(int playerId, string unitTypeId)
	{
		PlayerId = playerId;
		UnitTypeId = unitTypeId;
	}

	public int PlayerId { get; }

	public string UnitTypeId { get; }

	public void Apply(SimContext context)
	{
		var units = context.GetSystem<UnitManager>();
		if (units.Catalog.Types.FirstOrDefault(t => t.Id == UnitTypeId) is { } type)
		{
			context.GetSystem<Production>().Order(PlayerId, type);
		}
	}
}
