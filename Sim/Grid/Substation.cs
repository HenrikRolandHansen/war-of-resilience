using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Grid;

/// <summary>
/// Transformer station that steps high-voltage power down for a city. A city gets power only
/// through its substation.
/// </summary>
public sealed class Substation : GridNode
{
	public Substation(int id, int countryId, Fixed x, Fixed z, int cityId)
		: base(id, countryId, x, z)
	{
		CityId = cityId;
	}

	/// <summary>The city this substation feeds.</summary>
	public int CityId { get; }
}
