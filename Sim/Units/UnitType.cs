using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Units;

public enum UnitDomain
{
	Ground,
	Air,
}

/// <summary>Stats shared by every unit of one kind, loaded from Data/units.json.</summary>
public sealed record UnitType
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public UnitDomain Domain { get; init; }

	/// <summary>Real-world top speed.</summary>
	public int SpeedKmh { get; init; }

	/// <summary>Height above the ground or sea that air units fly at. 0 for ground units.</summary>
	public int CruiseAltitudeMetres { get; init; }

	public int HitPoints { get; init; }

	public int SightMetres { get; init; }

	public int Cost { get; init; }

	/// <summary>Damage done when a kamikaze unit hits its target (it is destroyed doing so). 0 = cannot attack.</summary>
	public int AttackDamage { get; init; }

	public bool CanAttack => AttackDamage > 0;

	/// <summary>Index in the catalog; used in the checksum instead of the string id.</summary>
	public int Index { get; init; }
}
