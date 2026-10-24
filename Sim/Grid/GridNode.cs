using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Grid;

/// <summary>A point in a country's power grid that lines connect to: a power plant or a substation.</summary>
public abstract class GridNode
{
	protected GridNode(int id, int countryId, Fixed x, Fixed z)
	{
		Id = id;
		CountryId = countryId;
		X = x;
		Z = z;
	}

	/// <summary>Index in <see cref="PowerGrid.Nodes"/>.</summary>
	public int Id { get; }

	public int CountryId { get; }

	/// <summary>World position in metres.</summary>
	public Fixed X { get; }

	/// <summary>World position in metres.</summary>
	public Fixed Z { get; }

	/// <summary>False once destroyed or switched off. Later steps let attacks change this.</summary>
	public bool IsOperational { get; internal set; } = true;

	/// <summary>True if this node is connected to at least one working power plant.</summary>
	public bool IsEnergized { get; internal set; }
}
