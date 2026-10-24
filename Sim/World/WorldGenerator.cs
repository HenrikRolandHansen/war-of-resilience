using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.World;

/// <summary>
/// Splits the map into two countries with a north-south border and places a capital and
/// a few cities for each on habitable land. Deterministic for a given seed and terrain.
/// </summary>
public static class WorldGenerator
{
	public sealed record Settings
	{
		public int MinCitiesPerCountry { get; init; } = 2;

		/// <summary>Not counting the capital.</summary>
		public int MaxCitiesPerCountry { get; init; } = 3;

		/// <summary>Minimum distance between any two cities.</summary>
		public Fixed MinCityDistance { get; init; } = Fixed.FromInt(120_000);

		/// <summary>Cities keep at least this far from the border.</summary>
		public Fixed BorderMargin { get; init; } = Fixed.FromInt(60_000);

		/// <summary>Cities keep at least this far from the map edge.</summary>
		public Fixed EdgeMargin { get; init; } = Fixed.FromInt(40_000);

		/// <summary>Cities need ground at least this far above sea level.</summary>
		public Fixed MinHeightAboveSea { get; init; } = Fixed.FromInt(10);

		/// <summary>Cities are not placed higher than this above sea level (no mountain-top cities).</summary>
		public Fixed MaxHeightAboveSea { get; init; } = Fixed.FromInt(600);

		public int CapitalPopulationThousands { get; init; } = 2000;

		public int MinCityPopulationThousands { get; init; } = 250;

		public int MaxCityPopulationThousands { get; init; } = 900;

		public int StartMoney { get; init; } = 5000;
	}

	private static readonly string[] CountryNames = { "Valmark", "Korvia" };

	private static readonly string[][] CityNames =
	{
		new[] { "Aldheim", "Brennvik", "Dalsund", "Eskeby", "Fjordhavn" },
		new[] { "Ostrava Nova", "Belgrin", "Tarnow", "Zelina", "Morrava" },
	};

	private const int MaxAttemptsPerCity = 2000;

	public static WorldState Generate(ulong seed, Terrain terrain, Settings settings)
	{
		var random = new DeterministicRandom(seed ^ 0xC17E5C17E5UL);
		var borderX = terrain.SizeX * Fixed.Half;
		var countries = new List<Country>();
		var cities = new List<City>();

		for (int countryId = 0; countryId < 2; countryId++)
		{
			var country = new Country(countryId, CountryNames[countryId]) { Money = Fixed.FromInt(settings.StartMoney) };
			countries.Add(country);

			// West country spans [0, border); east country spans (border, size].
			var minX = countryId == 0 ? settings.EdgeMargin : borderX + settings.BorderMargin;
			var maxX = countryId == 0 ? borderX - settings.BorderMargin : terrain.SizeX - settings.EdgeMargin;
			var minZ = settings.EdgeMargin;
			var maxZ = terrain.SizeZ - settings.EdgeMargin;

			// The capital sits in the half of the country furthest from the border.
			var width = maxX - minX;
			var capitalMinX = countryId == 0 ? minX : minX + width * Fixed.Half;
			var capitalMaxX = countryId == 0 ? minX + width * Fixed.Half : maxX;

			var capitalPos = FindSite(random, terrain, settings, cities, capitalMinX, capitalMaxX, minZ, maxZ)
				?? throw new InvalidOperationException($"Could not find a site for the capital of {country.Name}.");
			cities.Add(new City(cities.Count, CityNames[countryId][0], countryId, capitalPos.X, capitalPos.Z,
				settings.CapitalPopulationThousands, isCapital: true, morale: 75) { TargetMorale = 80 });

			int cityCount = random.NextInt(settings.MinCitiesPerCountry, settings.MaxCitiesPerCountry + 1);
			for (int i = 0; i < cityCount; i++)
			{
				var pos = FindSite(random, terrain, settings, cities, minX, maxX, minZ, maxZ)
					?? throw new InvalidOperationException($"Could not find a site for city {i + 1} of {country.Name}.");
				int population = random.NextInt(settings.MinCityPopulationThousands, settings.MaxCityPopulationThousands + 1);
				cities.Add(new City(cities.Count, CityNames[countryId][i + 1], countryId, pos.X, pos.Z,
					population, isCapital: false, morale: 70));
			}
		}

		return new WorldState(countries, cities, borderX);
	}

	private static (Fixed X, Fixed Z)? FindSite(
		DeterministicRandom random, Terrain terrain, Settings settings, List<City> existing,
		Fixed minX, Fixed maxX, Fixed minZ, Fixed maxZ)
	{
		for (int attempt = 0; attempt < MaxAttemptsPerCity; attempt++)
		{
			var x = minX + (maxX - minX) * random.NextFixed();
			var z = minZ + (maxZ - minZ) * random.NextFixed();

			var aboveSea = terrain.HeightAt(x, z) - terrain.SeaLevel;
			if (aboveSea < settings.MinHeightAboveSea || aboveSea > settings.MaxHeightAboveSea)
			{
				continue;
			}

			if (existing.Any(c => IsCloserThan(c.X - x, c.Z - z, settings.MinCityDistance)))
			{
				continue;
			}

			return (x, z);
		}

		return null;
	}

	/// <summary>Compares squared distances in kilometres so the squares stay small.</summary>
	private static bool IsCloserThan(Fixed dx, Fixed dz, Fixed distance)
	{
		var km = Fixed.FromInt(1000);
		var dxKm = dx / km;
		var dzKm = dz / km;
		var dKm = distance / km;
		return dxKm * dxKm + dzKm * dzKm < dKm * dKm;
	}
}
