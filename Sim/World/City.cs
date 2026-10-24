using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.World;

/// <summary>A city that produces income for its country. Its morale and power supply scale that income.</summary>
public sealed class City
{
	public const int MaxMorale = 100;

	public City(int id, string name, int countryId, Fixed x, Fixed z, int populationThousands, bool isCapital, int morale)
	{
		Id = id;
		Name = name;
		CountryId = countryId;
		X = x;
		Z = z;
		PopulationThousands = populationThousands;
		IsCapital = isCapital;
		Morale = morale;
	}

	public int Id { get; }

	public string Name { get; }

	public int CountryId { get; internal set; }

	/// <summary>World position in metres.</summary>
	public Fixed X { get; }

	/// <summary>World position in metres.</summary>
	public Fixed Z { get; }

	public int PopulationThousands { get; internal set; }

	public bool IsCapital { get; }

	/// <summary>0 to 100. Drifts toward <see cref="TargetMorale"/>.</summary>
	public int Morale { get; internal set; }

	/// <summary>Where morale settles when the city lacks nothing.</summary>
	public int BaseMorale { get; internal set; } = 70;

	/// <summary>
	/// Where morale is heading: <see cref="BaseMorale"/> minus penalties such as blackouts.
	/// </summary>
	public int TargetMorale { get; internal set; } = 70;

	/// <summary>Share of the city's power demand that is met, 0 to 100. Set by the power grid.</summary>
	public int PowerPercent { get; internal set; } = 100;
}
