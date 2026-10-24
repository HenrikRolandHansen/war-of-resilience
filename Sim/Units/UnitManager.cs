using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Sim.Units;

/// <summary>
/// All units and their movement. Units drive or fly in a straight line to their destination.
/// Ground units stop at the water's edge (roads and path finding come later); air units keep
/// their cruise altitude above the ground or sea, climbing and sinking smoothly.
/// </summary>
public sealed class UnitManager : ISimSystem
{
	/// <summary>Spacing between units sent to the same spot, so they do not stack.</summary>
	public static readonly Fixed FormationSpacing = Fixed.FromInt(80);

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
			if (unit.Destination is { } destination)
			{
				Move(unit, destination);
			}
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
		if (unit.Type.Domain == UnitDomain.Air)
		{
			var maxChange = step * ClimbRate;
			var change = wanted - unit.Y;
			unit.Y += Fixed.Max(-maxChange, Fixed.Min(maxChange, change));

			// Never fly into a hill, whatever the climb rate.
			unit.Y = Fixed.Max(unit.Y, Fixed.Max(Terrain.HeightAt(nextX, nextZ), Terrain.SeaLevel) + Fixed.FromInt(20));
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
		}
	}
}
