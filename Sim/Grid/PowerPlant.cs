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
		: base(id, countryId, x, z, HitPointsOf(kind))
	{
		Kind = kind;
		CapacityMW = capacityMW;
	}

	public PowerPlantKind Kind { get; }

	/// <summary>Output in megawatts while operational.</summary>
	public int CapacityMW { get; }

	/// <summary>How much damage each kind takes before it stops. A wind farm is spread out, so it is hard to knock out.</summary>
	public static int HitPointsOf(PowerPlantKind kind) => kind switch
	{
		PowerPlantKind.Coal => 400,
		PowerPlantKind.Gas => 300,
		PowerPlantKind.Wind => 500,
		_ => 300,
	};
}
