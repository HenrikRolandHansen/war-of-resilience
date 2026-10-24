using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.World;

/// <summary>A nation controlled by a player or the AI.</summary>
public sealed class Country
{
	public Country(int id, string name)
	{
		Id = id;
		Name = name;
	}

	/// <summary>Also the player id that controls this country.</summary>
	public int Id { get; }

	public string Name { get; }

	/// <summary>Treasury in credits.</summary>
	public Fixed Money { get; internal set; }

	/// <summary>Credits earned in the most recent tick, for display.</summary>
	public Fixed IncomeLastTick { get; internal set; }
}
