using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Sim.Grid;

/// <summary>
/// Builds each country's starting power grid: a substation by every city, a coal, a gas and a
/// wind plant, and high-voltage lines joining them. Lines form the shortest network that
/// connects everything (a minimum spanning tree) plus a few extra lines so one cut does not
/// always black out a city. Lines never cross another country's land. Deterministic.
/// </summary>
public static class GridGenerator
{
	public sealed record Settings
	{
		/// <summary>Substations sit this far from their city's centre, on the outskirts.</summary>
		public Fixed SubstationOffset { get; init; } = Fixed.FromInt(6_000);

		/// <summary>Plants keep at least this far from any city centre.</summary>
		public Fixed MinPlantCityDistance { get; init; } = Fixed.FromInt(25_000);

		/// <summary>Plants are at most this far from their nearest own city.</summary>
		public Fixed MaxPlantCityDistance { get; init; } = Fixed.FromInt(180_000);

		/// <summary>Plants keep at least this far from each other.</summary>
		public Fixed MinPlantDistance { get; init; } = Fixed.FromInt(50_000);

		/// <summary>Plants keep at least this far from the map edge.</summary>
		public Fixed EdgeMargin { get; init; } = Fixed.FromInt(30_000);

		/// <summary>Plants keep at least this far from another country's land.</summary>
		public Fixed BorderMargin { get; init; } = Fixed.FromInt(30_000);

		/// <summary>
		/// Each plant's share of the country's demand, in percent. Together 120 %, so losing the
		/// smallest plant still leaves almost full power.
		/// </summary>
		public int CoalSharePercent { get; init; } = 50;

		public int GasSharePercent { get; init; } = 40;

		public int WindSharePercent { get; init; } = 30;

		/// <summary>Extra lines per country beyond the minimum needed to connect everything.</summary>
		public int ExtraLinesPerCountry { get; init; } = 2;
	}

	private const int MaxAttemptsPerPlant = 4000;

	/// <summary>Lines are checked against foreign land at this spacing.</summary>
	private static readonly Fixed LineCheckStep = Fixed.FromInt(4_000);

	private static readonly Fixed Km = Fixed.FromInt(1000);

	public static PowerGrid Generate(ulong seed, Terrain terrain, WorldState world, Settings settings)
	{
		var random = new DeterministicRandom(seed ^ 0x9A1D5EEDUL);
		var nodes = new List<GridNode>();
		var lines = new List<PowerLine>();

		foreach (var country in world.Countries)
		{
			var countryNodes = new List<GridNode>();
			var cities = world.CitiesOf(country.Id).ToList();

			foreach (var city in cities)
			{
				var (x, z) = SubstationSite(terrain, world.Territory, city, settings.SubstationOffset);
				var substation = new Substation(nodes.Count, country.Id, x, z, city.Id);
				nodes.Add(substation);
				countryNodes.Add(substation);
			}

			int demand = cities.Sum(PowerGrid.DemandMW);
			var plants = new[]
			{
				(PowerPlantKind.Coal, settings.CoalSharePercent),
				(PowerPlantKind.Gas, settings.GasSharePercent),
				(PowerPlantKind.Wind, settings.WindSharePercent),
			};
			foreach (var (kind, share) in plants)
			{
				var site = FindPlantSite(random, terrain, world, country.Id, kind, nodes, settings);
				if (site == null)
				{
					continue;
				}

				int capacity = Math.Max(10, (demand * share / 100 + 5) / 10 * 10);
				var plant = new PowerPlant(nodes.Count, country.Id, site.Value.X, site.Value.Z, kind, capacity);
				nodes.Add(plant);
				countryNodes.Add(plant);
			}

			ConnectCountry(terrain, world.Territory, country.Id, countryNodes, lines, settings.ExtraLinesPerCountry);
		}

		return new PowerGrid(world, nodes, lines);
	}

	/// <summary>First land spot of the city's own country on a ring around it, or the city centre.</summary>
	private static (Fixed X, Fixed Z) SubstationSite(Terrain terrain, Territory territory, City city, Fixed offset)
	{
		foreach (var (ox, oz) in Directions)
		{
			var x = city.X + offset * ox;
			var z = city.Z + offset * oz;
			if (territory.OwnerAt(terrain, x, z) == city.CountryId)
			{
				return (x, z);
			}
		}

		return (city.X, city.Z);
	}

	private static (Fixed X, Fixed Z)? FindPlantSite(
		DeterministicRandom random, Terrain terrain, WorldState world, int countryId, PowerPlantKind kind,
		List<GridNode> existing, Settings settings)
	{
		// Wind farms go on hills; thermal plants on low ground, where cooling water is.
		var minHeight = Fixed.FromInt(kind == PowerPlantKind.Wind ? 250 : 5);
		var maxHeight = Fixed.FromInt(kind == PowerPlantKind.Wind ? 1200 : 300);
		var ownCities = world.CitiesOf(countryId).ToList();

		for (int attempt = 0; attempt < MaxAttemptsPerPlant; attempt++)
		{
			// Every 4th attempt also accepts any land height, in case the country has no suitable hills or lowland.
			bool anyHeight = attempt % 4 == 3;
			var x = settings.EdgeMargin + (terrain.SizeX - settings.EdgeMargin * Fixed.FromInt(2)) * random.NextFixed();
			var z = settings.EdgeMargin + (terrain.SizeZ - settings.EdgeMargin * Fixed.FromInt(2)) * random.NextFixed();

			var aboveSea = terrain.HeightAt(x, z) - terrain.SeaLevel;
			if (aboveSea < Fixed.FromInt(5) || (!anyHeight && (aboveSea < minHeight || aboveSea > maxHeight)))
			{
				continue;
			}

			if (!WorldGenerator.IsDeepInside(world.Territory, terrain, countryId, x, z, settings.BorderMargin))
			{
				continue;
			}

			if (world.Cities.Any(c => DistanceSquaredKm(c.X - x, c.Z - z) < SquareKm(settings.MinPlantCityDistance)))
			{
				continue;
			}

			if (!ownCities.Any(c => DistanceSquaredKm(c.X - x, c.Z - z) <= SquareKm(settings.MaxPlantCityDistance)))
			{
				continue;
			}

			if (existing.OfType<PowerPlant>().Any(p => DistanceSquaredKm(p.X - x, p.Z - z) < SquareKm(settings.MinPlantDistance)))
			{
				continue;
			}

			return (x, z);
		}

		return null;
	}

	/// <summary>Prim's minimum spanning tree over allowed lines, then the shortest remaining lines as backups.</summary>
	private static void ConnectCountry(Terrain terrain, Territory territory, int countryId, List<GridNode> nodes,
		List<PowerLine> lines, int extraLines)
	{
		int count = nodes.Count;
		if (count < 2)
		{
			return;
		}

		var allowed = new bool[count, count];
		var length = new Fixed[count, count];
		for (int a = 0; a < count; a++)
		{
			for (int b = a + 1; b < count; b++)
			{
				length[a, b] = length[b, a] = DistanceSquaredKm(nodes[a].X - nodes[b].X, nodes[a].Z - nodes[b].Z);
				allowed[a, b] = allowed[b, a] = StaysInside(terrain, territory, countryId, nodes[a], nodes[b]);
			}
		}

		var inTree = new bool[count];
		var used = new bool[count, count];
		inTree[0] = true;
		for (int added = 1; added < count; added++)
		{
			int bestA = -1, bestB = -1;
			for (int a = 0; a < count; a++)
			{
				if (!inTree[a])
				{
					continue;
				}

				for (int b = 0; b < count; b++)
				{
					if (!inTree[b] && allowed[a, b] && (bestA < 0 || length[a, b] < length[bestA, bestB]))
					{
						bestA = a;
						bestB = b;
					}
				}
			}

			if (bestA < 0)
			{
				// The rest cannot be reached without crossing foreign land.
				break;
			}

			inTree[bestB] = true;
			used[bestA, bestB] = used[bestB, bestA] = true;
			lines.Add(new PowerLine(lines.Count, nodes[bestA].Id, nodes[bestB].Id));
		}

		for (int extra = 0; extra < extraLines; extra++)
		{
			int bestA = -1, bestB = -1;
			for (int a = 0; a < count; a++)
			{
				for (int b = a + 1; b < count; b++)
				{
					if (allowed[a, b] && !used[a, b] && (bestA < 0 || length[a, b] < length[bestA, bestB]))
					{
						bestA = a;
						bestB = b;
					}
				}
			}

			if (bestA < 0)
			{
				break;
			}

			used[bestA, bestB] = used[bestB, bestA] = true;
			lines.Add(new PowerLine(lines.Count, nodes[bestA].Id, nodes[bestB].Id));
		}
	}

	/// <summary>True if the straight line between two nodes never touches another country's land.</summary>
	private static bool StaysInside(Terrain terrain, Territory territory, int countryId, GridNode a, GridNode b)
	{
		var dx = b.X - a.X;
		var dz = b.Z - a.Z;
		var lengthKm = Fixed.Max(Fixed.Abs(dx), Fixed.Abs(dz)) / Km;
		int steps = Math.Max(1, (lengthKm / (LineCheckStep / Km)).FloorToInt() + 1);
		for (int i = 0; i <= steps; i++)
		{
			var t = Fixed.FromRatio(i, steps);
			int owner = territory.OwnerAt(terrain, a.X + dx * t, a.Z + dz * t);
			if (owner != -1 && owner != countryId)
			{
				return false;
			}
		}

		return true;
	}

	private static Fixed SquareKm(Fixed metres)
	{
		var km = metres / Km;
		return km * km;
	}

	private static Fixed DistanceSquaredKm(Fixed dx, Fixed dz)
	{
		var xKm = dx / Km;
		var zKm = dz / Km;
		return xKm * xKm + zKm * zKm;
	}

	/// <summary>Eight unit directions, starting east and going round.</summary>
	private static readonly (Fixed X, Fixed Z)[] Directions =
	{
		(Fixed.One, Fixed.Zero), (Fixed.FromRatio(7071, 10000), Fixed.FromRatio(7071, 10000)),
		(Fixed.Zero, Fixed.One), (Fixed.FromRatio(-7071, 10000), Fixed.FromRatio(7071, 10000)),
		(-Fixed.One, Fixed.Zero), (Fixed.FromRatio(-7071, 10000), Fixed.FromRatio(-7071, 10000)),
		(Fixed.Zero, -Fixed.One), (Fixed.FromRatio(7071, 10000), Fixed.FromRatio(-7071, 10000)),
	};
}
