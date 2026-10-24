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

	/// <summary>Ticks the factory needs to build one.</summary>
	public int BuildTicks { get; init; }

	/// <summary>Damage done when a kamikaze unit hits its target (it is destroyed doing so). 0 = cannot attack.</summary>
	public int AttackDamage { get; init; }

	public bool CanAttack => AttackDamage > 0;

	/// <summary>Surface-to-air missiles this unit carries, or null if it has none.</summary>
	public AirDefenceStats? AirDefence { get; init; }

	/// <summary>Index in the catalog; used in the checksum instead of the string id.</summary>
	public int Index { get; init; }
}

/// <summary>What an air defence unit can do. Times are in simulation ticks (10 per real second).</summary>
public sealed record AirDefenceStats
{
	/// <summary>Enemy aircraft closer than this, measured along the ground, are fired at.</summary>
	public int RangeMetres { get; init; }

	/// <summary>Missiles ready to fire when full.</summary>
	public int Magazine { get; init; }

	/// <summary>Ticks between two launches.</summary>
	public int ReloadTicks { get; init; }

	/// <summary>Ticks to get one spent missile back.</summary>
	public int RearmTicks { get; init; }

	/// <summary>Chance that a missile reaching its target destroys it.</summary>
	public int HitChancePercent { get; init; }

	public int MissileSpeedKmh { get; init; }

	public int MissileDamage { get; init; }
}
