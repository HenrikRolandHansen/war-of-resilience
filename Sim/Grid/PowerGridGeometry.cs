using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Grid;

/// <summary>Distances to grid nodes and lines, in fixed-point.</summary>
public static class PowerGridGeometry
{
	private static readonly Fixed Km = Fixed.FromInt(1000);

	/// <summary>The point on a line nearest to (x, z).</summary>
	public static (Fixed X, Fixed Z) ClosestPointOnLine(PowerGrid grid, PowerLine line, Fixed x, Fixed z)
	{
		var a = grid.Nodes[line.FromNodeId];
		var b = grid.Nodes[line.ToNodeId];

		// Work in km so the squares stay small.
		var abx = (b.X - a.X) / Km;
		var abz = (b.Z - a.Z) / Km;
		var apx = (x - a.X) / Km;
		var apz = (z - a.Z) / Km;
		var lengthSquared = abx * abx + abz * abz;
		if (lengthSquared == Fixed.Zero)
		{
			return (a.X, a.Z);
		}

		var t = Fixed.Max(Fixed.Zero, Fixed.Min(Fixed.One, (apx * abx + apz * abz) / lengthSquared));
		return (a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
	}
}
