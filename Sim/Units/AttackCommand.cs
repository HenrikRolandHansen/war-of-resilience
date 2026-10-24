using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;

namespace WarOfResilience.Sim.Units;

/// <summary>
/// Sends the player's attack-capable units (kamikaze drones) at a target. Other units in the
/// group simply move to it. Ignored if the target belongs to the player, is already destroyed, or
/// is an enemy unit the player cannot currently see.
/// </summary>
public sealed class AttackCommand : ICommand
{
	public AttackCommand(int playerId, IEnumerable<int> unitIds, AttackTarget target)
	{
		PlayerId = playerId;
		UnitIds = unitIds.Distinct().OrderBy(id => id).ToArray();
		Target = target;
	}

	public int PlayerId { get; }

	public IReadOnlyList<int> UnitIds { get; }

	public AttackTarget Target { get; }

	public void Apply(SimContext context)
	{
		var manager = context.GetSystem<UnitManager>();
		if (!IsValidTarget(context, manager))
		{
			return;
		}

		var target = Target;
		if (target.Kind == TargetKind.PowerLine)
		{
			var grid = context.GetSystem<PowerGrid>();
			var (x, z) = PowerGridGeometry.ClosestPointOnLine(grid, grid.Lines[target.Id], target.X, target.Z);
			target = target with { X = x, Z = z };
		}

		var position = manager.PositionOf(context, target);
		var others = new List<int>();
		foreach (var id in UnitIds)
		{
			var unit = manager.Find(id);
			if (unit == null || unit.OwnerId != PlayerId || !unit.IsAlive)
			{
				continue;
			}

			if (unit.Type.CanAttack)
			{
				unit.Target = target;
				unit.Destination = position;
			}
			else
			{
				others.Add(id);
			}
		}

		if (others.Count > 0 && position is { } p)
		{
			new MoveCommand(PlayerId, others, p.X, p.Z).Apply(context);
		}
	}

	private bool IsValidTarget(SimContext context, UnitManager manager)
	{
		switch (Target.Kind)
		{
			case TargetKind.Unit:
				var unit = manager.Find(Target.Id);
				var vision = context.FindSystem<Vision>();
				return unit is { IsAlive: true } && unit.OwnerId != PlayerId
					&& (vision == null || vision.CanSee(PlayerId, unit));
			case TargetKind.GridNode:
				var grid = context.FindSystem<PowerGrid>();
				return grid != null && Target.Id >= 0 && Target.Id < grid.Nodes.Count
					&& grid.Nodes[Target.Id] is { IsOperational: true } node && node.CountryId != PlayerId;
			case TargetKind.PowerLine:
				var lines = context.FindSystem<PowerGrid>();
				return lines != null && Target.Id >= 0 && Target.Id < lines.Lines.Count
					&& lines.Lines[Target.Id].IsOperational
					&& lines.Nodes[lines.Lines[Target.Id].FromNodeId].CountryId != PlayerId;
			default:
				return false;
		}
	}
}
