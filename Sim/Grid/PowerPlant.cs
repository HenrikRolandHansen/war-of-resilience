using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Grid;

public enum PowerPlantKind
{
	Coal,
	Gas,
	Wind,
}

/// <summary>Produces electricity for every city it is connected to.</summary>
public sealed class PowerPlant : GridNode
{
	public PowerPlant(int id, int countryId, Fixed x, Fixed z, PowerPlantKind kind, int capacityMW)
		: base(id, countryId, x, z)
	{
		Kind = kind;
		CapacityMW = capacityMW;
	}

	public PowerPlantKind Kind { get; }

	/// <summary>Output in megawatts while operational.</summary>
	public int CapacityMW { get; }
}
