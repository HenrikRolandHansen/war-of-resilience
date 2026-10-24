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

	/// <summary>False once cut. Later steps let attacks change this.</summary>
	public bool IsOperational { get; internal set; } = true;

	/// <summary>True if the line is intact and connected to a working power plant.</summary>
	public bool IsEnergized { get; internal set; }
}
