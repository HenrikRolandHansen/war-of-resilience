using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Sim.Units;

/// <summary>
/// All units, their movement and kamikaze attacks. Units drive or fly in a straight line to their
/// destination. Ground units stop at the water's edge (roads and path finding come later); air
/// units keep their cruise altitude above the ground or sea, climbing and sinking smoothly. A
/// kamikaze unit follows its target, dives in the last stretch and is destroyed when it hits,
/// damaging the target.
/// </summary>
public sealed class UnitManager : ISimSystem
{
	/// <summary>Spacing between units sent to the same spot, so they do not stack.</summary>
	public static readonly Fixed FormationSpacing = Fixed.FromInt(80);

	/// <summary>A kamikaze unit starts diving at its target from this far away.</summary>
	private static readonly Fixed DiveDistance = Fixed.FromInt(1_500);

	/// <summary>Air units change height at most this share of their forward step per tick.</summary>
	private static readonly Fixed ClimbRate = Fixed.FromRatio(1, 2);

	private readonly List<Unit> _units = new();

	public UnitManager(Terrain terrain, UnitCatalog catalog)
	{
		Terrain = terrain;
		Catalog = catalog;
	}

	public Terrain Terrain { get; }

	public UnitCatalog Catalog { get; }

	public IReadOnlyList<Unit> Units => _units;

	public Unit? Find(int id) => id >= 0 && id < _units.Count ? _units[id] : null;

	/// <summary>Adds a unit at a position. Air units start at cruise altitude.</summary>
	public Unit Spawn(UnitType type, int ownerId, Fixed x, Fixed z)
	{
		var unit = new Unit(_units.Count, type, ownerId, x, z, CruiseHeight(type, x, z));
		_units.Add(unit);
		return unit;
	}

	/// <summary>Height the unit wants to be at: on the ground, or cruise altitude above ground or sea.</summary>
	public Fixed CruiseHeight(UnitType type, Fixed x, Fixed z)
	{
		var surface = Fixed.Max(Terrain.HeightAt(x, z), Terrain.SeaLevel);
		return type.Domain == UnitDomain.Air
			? surface + Fixed.FromInt(type.CruiseAltitudeMetres)
			: Terrain.HeightAt(x, z);
	}

	public void Update(SimContext context)
	{
		foreach (var unit in _units)
		{
			if (!unit.IsAlive)
			{
				continue;
			}

			if (unit.Target is { } target)
			{
				if (PositionOf(context, target) is not { } position)
				{
					// Target gone: fly on to where it was last seen and wait there.
					unit.Target = null;
				}
				else
				{
					unit.Destination = position;
				}
			}

			if (unit.Destination is { } destination)
			{
				Move(unit, destination);
				if (unit.Target is { } hit && unit.X == destination.X && unit.Z == destination.Z)
				{
					Detonate(context, unit, hit);
				}
			}
		}
	}

	/// <summary>Where a target is now, or null if it is already destroyed.</summary>
	public (Fixed X, Fixed Z)? PositionOf(SimContext context, AttackTarget target)
	{
		switch (target.Kind)
		{
			case TargetKind.Unit:
				return Find(target.Id) is { IsAlive: true } unit ? (unit.X, unit.Z) : null;
			case TargetKind.GridNode:
				var grid = context.FindSystem<PowerGrid>();
				return grid != null && target.Id < grid.Nodes.Count && grid.Nodes[target.Id] is { IsOperational: true } node
					? (node.X, node.Z) : null;
			case TargetKind.PowerLine:
				var lines = context.FindSystem<PowerGrid>();
				return lines != null && target.Id < lines.Lines.Count && lines.Lines[target.Id].IsOperational
					? (target.X, target.Z) : null;
			default:
				return null;
		}
	}

	private void Detonate(SimContext context, Unit attacker, AttackTarget target)
	{
		int damage = attacker.Type.AttackDamage;
		switch (target.Kind)
		{
			case TargetKind.Unit when Find(target.Id) is { IsAlive: true } victim:
				Damage(victim, damage);
				break;
			case TargetKind.GridNode:
				context.FindSystem<PowerGrid>()?.Nodes[target.Id].Damage(damage);
				break;
			case TargetKind.PowerLine:
				context.FindSystem<PowerGrid>()?.Lines[target.Id].Damage(damage);
				break;
		}

		Damage(attacker, attacker.HitPoints);
	}

	internal static void Damage(Unit unit, int amount)
	{
		unit.HitPoints = Math.Max(0, unit.HitPoints - amount);
		if (!unit.IsAlive)
		{
			unit.Destination = null;
			unit.Target = null;
		}
	}

	private void Move(Unit unit, (Fixed X, Fixed Z) destination)
	{
		var step = Catalog.StepPerTick(unit.Type);
		var dx = destination.X - unit.X;
		var dz = destination.Z - unit.Z;
		var distance = Fixed.Sqrt(dx * dx + dz * dz);

		Fixed nextX, nextZ;
		if (distance <= step)
		{
			nextX = destination.X;
			nextZ = destination.Z;
		}
		else
		{
			unit.FacingX = dx / distance;
			unit.FacingZ = dz / distance;
			nextX = unit.X + unit.FacingX * step;
			nextZ = unit.Z + unit.FacingZ * step;
		}

		if (unit.Type.Domain == UnitDomain.Ground && Terrain.IsUnderWater(nextX, nextZ))
		{
			unit.Destination = null;
			return;
		}

		unit.X = nextX;
		unit.Z = nextZ;

		var wanted = CruiseHeight(unit.Type, nextX, nextZ);
		if (unit.Target != null && distance <= DiveDistance)
		{
			// Dive onto the target.
			wanted = Fixed.Max(Terrain.HeightAt(nextX, nextZ), Terrain.SeaLevel) + Fixed.FromInt(5);
		}

		if (unit.Type.Domain == UnitDomain.Air)
		{
			var maxChange = step * ClimbRate;
			var change = wanted - unit.Y;
			unit.Y += Fixed.Max(-maxChange, Fixed.Min(maxChange, change));

			// Never fly into a hill, whatever the climb rate.
			var floor = unit.Target != null ? Fixed.FromInt(5) : Fixed.FromInt(20);
			unit.Y = Fixed.Max(unit.Y, Fixed.Max(Terrain.HeightAt(nextX, nextZ), Terrain.SeaLevel) + floor);
		}
		else
		{
			unit.Y = wanted;
		}

		if (nextX == destination.X && nextZ == destination.Z)
		{
			unit.Destination = null;
		}
	}

	public void AddToChecksum(ref Checksum checksum)
	{
		foreach (var unit in _units)
		{
			checksum.Add(unit.Id);
			checksum.Add(unit.Type.Index);
			checksum.Add(unit.OwnerId);
			checksum.Add(unit.X);
			checksum.Add(unit.Y);
			checksum.Add(unit.Z);
			checksum.Add(unit.HitPoints);
			checksum.Add(unit.Destination?.X ?? Fixed.FromInt(-1));
			checksum.Add(unit.Destination?.Z ?? Fixed.FromInt(-1));
			checksum.Add(unit.Target is { } target ? (int)target.Kind * 1_000_000 + target.Id : -1);
		}
	}
}
