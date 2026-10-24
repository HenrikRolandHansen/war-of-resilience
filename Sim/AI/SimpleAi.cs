using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.Units;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Sim.AI;

/// <summary>
/// A simple opponent for one country. Every couple of seconds it orders units, posts its air
/// defence at its most valuable stations and plants, sends recon drones over enemy cities and
/// plants, and launches kamikaze waves at the enemy target worth most per drone needed. Air
/// defence it has seen near a target makes it send a bigger wave. Once the enemy capital is dark it
/// sends its jeeps there. It only knows what the country knows: infrastructure is on any map,
/// enemy units only where its <see cref="Vision"/> has seen them. It acts through the same commands
/// as a player, applied inside the tick, so it stays deterministic.
/// </summary>
public sealed class SimpleAi : ISimSystem
{
	public const int ThinkEveryTicks = 20;

	/// <summary>Recon drones and air defence units it wants before building kamikaze drones.</summary>
	private const int WantedRecon = 2;
	private const int WantedAirDefence = 4;

	/// <summary>Orders it keeps in the factory queue.</summary>
	private const int QueueLength = 2;

	/// <summary>Extra drones sent per enemy air defence unit known near the target.</summary>
	private const int DronesPerAirDefence = 4;

	/// <summary>With this many idle kamikaze drones it attacks the best target even if it cannot afford the full wave.</summary>
	private const int AllInWave = 12;

	private readonly UnitManager _units;
	private readonly WorldState _world;
	private readonly PowerGrid _grid;
	private readonly Vision _vision;
	private readonly Production _production;
	private int _reconCursor;

	public SimpleAi(int countryId, UnitManager units, WorldState world, PowerGrid grid, Vision vision, Production production)
	{
		CountryId = countryId;
		_units = units;
		_world = world;
		_grid = grid;
		_vision = vision;
		_production = production;
	}

	public int CountryId { get; }

	public void Update(SimContext context)
	{
		if (context.Tick % ThinkEveryTicks != CountryId % ThinkEveryTicks)
		{
			return;
		}

		Build(context);
		Defend(context);
		Scout(context);
		Attack(context);
		Advance(context);
	}

	private IEnumerable<Unit> Own(string typeId) => _units.Units.Where(u => u.OwnerId == CountryId && u.IsAlive && u.Type.Id == typeId);

	private static bool Idle(Unit unit) => unit.Destination == null && unit.Target == null;

	private void Build(SimContext context)
	{
		var queue = _production.QueueOf(CountryId);
		if (queue.Count >= QueueLength)
		{
			return;
		}

		int Count(string id) => Own(id).Count() + queue.Count(t => t.Id == id);
		string next = Count("recon_drone") < WantedRecon ? "recon_drone"
			: Count("air_defence") < WantedAirDefence ? "air_defence"
			: "kamikaze_drone";
		new BuildCommand(CountryId, next).Apply(context);
	}

	/// <summary>Own stations and plants, most valuable first.</summary>
	private List<GridNode> OwnAssets() => _grid.Nodes
		.Where(n => n.CountryId == CountryId && n.IsOperational)
		.OrderByDescending(Value)
		.ThenBy(n => n.Id)
		.ToList();

	/// <summary>What losing a node costs its owner: people fed for a substation, output for a plant.</summary>
	private int Value(GridNode node) => node switch
	{
		Substation substation => _world.Cities[substation.CityId].PopulationThousands,
		PowerPlant plant => plant.CapacityMW / 2,
		_ => 0,
	};

	private void Defend(SimContext context)
	{
		var posts = OwnAssets();
		if (posts.Count == 0)
		{
			return;
		}

		int index = 0;
		foreach (var launcher in Own("air_defence").OrderBy(u => u.Id))
		{
			var asset = posts[index % posts.Count];
			var offset = Fixed.FromInt(600 + 400 * (index / posts.Count));
			index++;
			var x = asset.X + offset;
			var z = asset.Z + offset;
			bool there = Fixed.Abs(launcher.X - x) + Fixed.Abs(launcher.Z - z) < Fixed.FromInt(300);
			bool going = launcher.Destination is { } d && d.X == x && d.Z == z;
			if (!there && !going && !(Idle(launcher) && launcher.Type.Domain == UnitDomain.Ground && Stuck(launcher, x, z)))
			{
				new MoveCommand(CountryId, new[] { launcher.Id }, x, z).Apply(context);
			}
		}
	}

	/// <summary>A ground unit that stopped short of its post (water in the way) stays where it is.</summary>
	private bool Stuck(Unit unit, Fixed x, Fixed z) =>
		_units.Terrain.IsUnderWater(unit.X + (x - unit.X) / Fixed.FromInt(50), unit.Z + (z - unit.Z) / Fixed.FromInt(50));

	private void Scout(SimContext context)
	{
		var waypoints = _world.Cities.Where(c => c.CountryId != CountryId).Select(c => (c.X, c.Z))
			.Concat(_grid.Plants.Where(p => p.CountryId != CountryId).Select(p => (p.X, p.Z)))
			.ToList();
		if (waypoints.Count == 0)
		{
			return;
		}

		foreach (var drone in Own("recon_drone").Where(Idle).OrderBy(u => u.Id))
		{
			var (x, z) = waypoints[_reconCursor++ % waypoints.Count];
			new MoveCommand(CountryId, new[] { drone.Id }, x, z).Apply(context);
		}
	}

	private void Attack(SimContext context)
	{
		var idle = Own("kamikaze_drone").Where(Idle).OrderBy(u => u.Id).ToList();
		if (idle.Count == 0)
		{
			return;
		}

		var busyTargets = _units.Units
			.Where(u => u.OwnerId == CountryId && u.IsAlive && u.Target is { Kind: TargetKind.GridNode })
			.Select(u => u.Target!.Value.Id)
			.ToHashSet();
		var enemyLaunchers = _vision.LastSeenBy(CountryId)
			.Where(s => _units.Units[s.Key] is { IsAlive: true, Type.AirDefence: not null })
			.Select(s => (s.Value.X, s.Value.Z, Range: _units.Units[s.Key].Type.AirDefence!.RangeMetres))
			.ToList();
		int damage = idle[0].Type.AttackDamage;

		GridNode? best = null;
		int bestNeed = 0;
		long bestScore = -1;
		GridNode? mostValuable = null;
		foreach (var node in _grid.Nodes)
		{
			if (node.CountryId == CountryId || !node.IsOperational || busyTargets.Contains(node.Id))
			{
				continue;
			}

			int threats = enemyLaunchers.Count(l => Within(l.X - node.X, l.Z - node.Z, l.Range + 1_500));
			int need = (node.HitPoints + damage - 1) / damage + DronesPerAirDefence * threats;
			long score = (long)Value(node) * 1000 / need;
			if (mostValuable == null || Value(node) > Value(mostValuable))
			{
				mostValuable = node;
			}

			if (need <= idle.Count && score > bestScore)
			{
				best = node;
				bestNeed = need;
				bestScore = score;
			}
		}

		if (best == null && idle.Count >= AllInWave && mostValuable != null)
		{
			best = mostValuable;
			bestNeed = idle.Count;
		}

		if (best != null)
		{
			var wave = idle.Take(bestNeed).Select(u => u.Id);
			new AttackCommand(CountryId, wave, AttackTarget.OnGridNode(best.Id)).Apply(context);
		}
	}

	/// <summary>Once the enemy capital is dark, the jeeps head there.</summary>
	private void Advance(SimContext context)
	{
		var target = _world.Cities.FirstOrDefault(c => c.CountryId != CountryId && c.IsCapital && c.PowerPercent == 0);
		if (target == null)
		{
			return;
		}

		var jeeps = Own("jeep").Where(Idle).Where(j => Fixed.Abs(j.X - target.X) + Fixed.Abs(j.Z - target.Z) > Fixed.FromInt(3_000))
			.Select(j => j.Id).ToList();
		if (jeeps.Count > 0)
		{
			new MoveCommand(CountryId, jeeps, target.X, target.Z).Apply(context);
		}
	}

	private static bool Within(Fixed dx, Fixed dz, int metres)
	{
		var r = Fixed.FromInt(metres);
		return dx * dx + dz * dz <= r * r;
	}

	public void AddToChecksum(ref Checksum checksum)
	{
		checksum.Add(CountryId);
		checksum.Add(_reconCursor);
	}
}
