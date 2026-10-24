using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Grid;

/// <summary>A point in a country's power grid that lines connect to: a power plant or a substation.</summary>
public abstract class GridNode
{
	protected GridNode(int id, int countryId, Fixed x, Fixed z, int hitPoints)
	{
		Id = id;
		CountryId = countryId;
		X = x;
		Z = z;
		MaxHitPoints = hitPoints;
		HitPoints = hitPoints;
	}

	/// <summary>Index in <see cref="PowerGrid.Nodes"/>.</summary>
	public int Id { get; }

	public int CountryId { get; }

	/// <summary>World position in metres.</summary>
	public Fixed X { get; }

	/// <summary>World position in metres.</summary>
	public Fixed Z { get; }

	/// <summary>False once destroyed or switched off.</summary>
	public bool IsOperational { get; internal set; } = true;

	public int MaxHitPoints { get; }

	/// <summary>Damage left before the node is destroyed and stops working.</summary>
	public int HitPoints { get; internal set; }

	/// <summary>Takes damage; at 0 hit points the node is destroyed.</summary>
	internal void Damage(int amount)
	{
		HitPoints = Math.Max(0, HitPoints - amount);
		if (HitPoints == 0)
		{
			IsOperational = false;
		}
	}

	/// <summary>True if this node is connected to at least one working power plant.</summary>
	public bool IsEnergized { get; internal set; }
}
