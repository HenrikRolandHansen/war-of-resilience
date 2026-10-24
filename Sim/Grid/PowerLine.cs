namespace WarOfResilience.Sim.Grid;

/// <summary>High-voltage line between two grid nodes. Power flows both ways.</summary>
public sealed class PowerLine
{
	public PowerLine(int id, int fromNodeId, int toNodeId)
	{
		Id = id;
		FromNodeId = fromNodeId;
		ToNodeId = toNodeId;
	}

	public int Id { get; }

	public int FromNodeId { get; }

	public int ToNodeId { get; }

	/// <summary>Damage a line takes before it is cut. Bringing down one pylon is enough.</summary>
	public const int MaxHitPoints = 80;

	/// <summary>False once cut.</summary>
	public bool IsOperational { get; internal set; } = true;

	public int HitPoints { get; internal set; } = MaxHitPoints;

	/// <summary>Takes damage; at 0 hit points the line is cut.</summary>
	internal void Damage(int amount)
	{
		HitPoints = Math.Max(0, HitPoints - amount);
		if (HitPoints == 0)
		{
			IsOperational = false;
		}
	}

	/// <summary>True if the line is intact and connected to a working power plant.</summary>
	public bool IsEnergized { get; internal set; }
}
