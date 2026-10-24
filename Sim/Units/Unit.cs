using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Units;

/// <summary>One unit on the map. Changed only by <see cref="UnitManager"/> and commands.</summary>
public sealed class Unit
{
	internal Unit(int id, UnitType type, int ownerId, Fixed x, Fixed z, Fixed y)
	{
		Id = id;
		Type = type;
		OwnerId = ownerId;
		X = x;
		Z = z;
		Y = y;
		HitPoints = type.HitPoints;
		FacingX = Fixed.Zero;
		FacingZ = -Fixed.One;
	}

	public int Id { get; }

	public UnitType Type { get; }

	/// <summary>Country (and player) that owns the unit.</summary>
	public int OwnerId { get; }

	/// <summary>World position in metres.</summary>
	public Fixed X { get; internal set; }

	/// <summary>Height in metres: the ground for ground units, flying height for air units.</summary>
	public Fixed Y { get; internal set; }

	/// <summary>World position in metres.</summary>
	public Fixed Z { get; internal set; }

	/// <summary>Unit vector the unit faces, on the ground plane.</summary>
	public Fixed FacingX { get; internal set; }

	public Fixed FacingZ { get; internal set; }

	public int HitPoints { get; internal set; }

	public bool IsAlive => HitPoints > 0;

	/// <summary>What a kamikaze unit is flying at, or null.</summary>
	public AttackTarget? Target { get; internal set; }

	/// <summary>Where the unit is heading, or null when it has arrived or was stopped.</summary>
	public (Fixed X, Fixed Z)? Destination { get; internal set; }

	public bool IsMoving => Destination != null;
}
