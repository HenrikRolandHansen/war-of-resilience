using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Units;

public enum TargetKind
{
	Unit,
	GridNode,
	PowerLine,
}

/// <summary>
/// Something to attack: a unit, a power plant or substation, or a power line. For a line, X and Z
/// are the spot along it to hit (a pylon); for the others they are ignored.
/// </summary>
public readonly record struct AttackTarget(TargetKind Kind, int Id, Fixed X, Fixed Z)
{
	public static AttackTarget OnUnit(int unitId) => new(TargetKind.Unit, unitId, Fixed.Zero, Fixed.Zero);

	public static AttackTarget OnGridNode(int nodeId) => new(TargetKind.GridNode, nodeId, Fixed.Zero, Fixed.Zero);

	public static AttackTarget OnPowerLine(int lineId, Fixed x, Fixed z) => new(TargetKind.PowerLine, lineId, x, z);
}
