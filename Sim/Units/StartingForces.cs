using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Sim.Units;

/// <summary>Gives each country a few units next to its capital at the start of a game.</summary>
public static class StartingForces
{
	/// <summary>Unit type ids and how many of each every country starts with.</summary>
	public static readonly (string TypeId, int Count)[] Default =
	{
		("jeep", 3),
		("recon_drone", 2),
		("kamikaze_drone", 3),
	};

	/// <summary>Units start this far from the capital's centre, just outside the built-up area.</summary>
	private static readonly Fixed Offset = Fixed.FromInt(14_000);

	public static void Place(UnitManager units, WorldState world)
	{
		foreach (var country in world.Countries)
		{
			var capital = world.CitiesOf(country.Id).First(c => c.IsCapital);
			var (x, z) = StagingPoint(units.Terrain, world.Territory, capital);

			int index = 0;
			foreach (var (typeId, count) in Default)
			{
				for (int i = 0; i < count; i++, index++)
				{
					// A 4-wide grid of parking spots.
					var px = x + Fixed.FromInt(index % 4) * UnitManager.FormationSpacing;
					var pz = z + Fixed.FromInt(index / 4) * UnitManager.FormationSpacing;
					units.Spawn(units.Catalog[typeId], country.Id, px, pz);
				}
			}
		}
	}

	/// <summary>First dry spot of the country's own land around the capital, else the capital itself.</summary>
	private static (Fixed X, Fixed Z) StagingPoint(Terrain terrain, Territory territory, City capital)
	{
		var directions = new[] { (0, 1), (-1, 0), (1, 0), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) };
		foreach (var (dx, dz) in directions)
		{
			var x = capital.X + Offset * Fixed.FromInt(dx);
			var z = capital.Z + Offset * Fixed.FromInt(dz);
			bool dry = true;
			for (int i = 0; i < 4 && dry; i++)
			{
				for (int j = 0; j < 4 && dry; j++)
				{
					var px = x + Fixed.FromInt(i) * UnitManager.FormationSpacing;
					var pz = z + Fixed.FromInt(j) * UnitManager.FormationSpacing;
					dry = !terrain.IsUnderWater(px, pz) && territory.OwnerAt(terrain, px, pz) == capital.CountryId;
				}
			}

			if (dry)
			{
				return (x, z);
			}
		}

		return (capital.X, capital.Z);
	}
}
