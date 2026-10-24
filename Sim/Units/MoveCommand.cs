using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Units;

/// <summary>
/// Sends a group of the player's own units to a point. The group keeps a square formation around
/// the point so units do not pile up. Units the player does not own are ignored.
/// </summary>
public sealed class MoveCommand : ICommand
{
	public MoveCommand(int playerId, IEnumerable<int> unitIds, Fixed x, Fixed z)
	{
		PlayerId = playerId;
		UnitIds = unitIds.Distinct().OrderBy(id => id).ToArray();
		X = x;
		Z = z;
	}

	public int PlayerId { get; }

	public IReadOnlyList<int> UnitIds { get; }

	public Fixed X { get; }

	public Fixed Z { get; }

	public void Apply(SimContext context)
	{
		var manager = context.GetSystem<UnitManager>();
		var units = UnitIds.Select(manager.Find).Where(u => u != null && u.OwnerId == PlayerId && u.IsAlive).Cast<Unit>().ToList();
		if (units.Count == 0)
		{
			return;
		}

		int columns = (int)Math.Ceiling(Math.Sqrt(units.Count));
		var x = Fixed.Max(Fixed.Zero, Fixed.Min(X, manager.Terrain.SizeX));
		var z = Fixed.Max(Fixed.Zero, Fixed.Min(Z, manager.Terrain.SizeZ));
		var half = Fixed.FromInt(columns - 1) * Fixed.Half;

		for (int i = 0; i < units.Count; i++)
		{
			var offsetX = (Fixed.FromInt(i % columns) - half) * UnitManager.FormationSpacing;
			var offsetZ = (Fixed.FromInt(i / columns) - half) * UnitManager.FormationSpacing;
			units[i].Destination = (x + offsetX, z + offsetZ);
			units[i].Target = null;
		}
	}
}
