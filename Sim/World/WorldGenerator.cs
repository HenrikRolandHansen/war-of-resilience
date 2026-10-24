using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.World;

/// <summary>
/// Places two capitals on opposite sides of the map, grows each country's territory outward from
/// its capital over land (see <see cref="TerritoryGenerator"/>), then places a few more cities
/// inside each country. Deterministic for a given seed and terrain.
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

		/// <summary>Cities keep at least this far from another country's land.</summary>
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

		/// <summary>Each country must own at least this share of all land, or the capitals are placed again.</summary>
		public int MinLandSharePercent { get; init; } = 35;
	}

	private static readonly string[] CountryNames = { "Valmark", "Korvia" };

	private static readonly string[][] CityNames =
	{
		new[] { "Aldheim", "Brennvik", "Dalsund", "Eskeby", "Fjordhavn" },
		new[] { "Ostrava Nova", "Belgrin", "Tarnow", "Zelina", "Morrava" },
	};

	private const int MaxAttemptsPerCity = 2000;
	private const int MaxCapitalLayouts = 20;

	/// <summary>Capitals are placed within this share of the map width from their own edge.</summary>
	private static readonly Fixed CapitalZoneShare = Fixed.FromRatio(35, 100);

	public static WorldState Generate(ulong seed, Terrain terrain, Settings settings)
	{
		var random = new DeterministicRandom(seed ^ 0xC17E5C17E5UL);

		for (int layout = 0; layout < MaxCapitalLayouts; layout++)
		{
			var world = TryGenerate(random, terrain, settings);
			if (world != null)
			{
				return world;
			}
		}

		throw new InvalidOperationException($"Could not lay out two balanced countries in {MaxCapitalLayouts} attempts.");
	}

	private static WorldState? TryGenerate(DeterministicRandom random, Terrain terrain, Settings settings)
	{
		var countries = new List<Country>();
		var cities = new List<City>();
		var minZ = settings.EdgeMargin;
		var maxZ = terrain.SizeZ - settings.EdgeMargin;
		var zone = terrain.SizeX * CapitalZoneShare;

		// Capitals first: Valmark near the west edge, Korvia near the east edge.
		for (int countryId = 0; countryId < 2; countryId++)
		{
			countries.Add(new Country(countryId, CountryNames[countryId]) { Money = Fixed.FromInt(settings.StartMoney) });

			var minX = countryId == 0 ? settings.EdgeMargin : terrain.SizeX - zone;
			var maxX = countryId == 0 ? zone : terrain.SizeX - settings.EdgeMargin;
			var site = FindSite(random, terrain, settings, cities, minX, maxX, minZ, maxZ, accept: null);
			if (site == null)
			{
				return null;
			}

			cities.Add(new City(cities.Count, CityNames[countryId][0], countryId, site.Value.X, site.Value.Z,
				settings.CapitalPopulationThousands, isCapital: true, morale: 75) { BaseMorale = 80, TargetMorale = 80 });
		}

		var territory = TerritoryGenerator.Generate(terrain, cities);
		int totalLand = territory.AreaOf(0) + territory.AreaOf(1);
		if (countries.Any(c => territory.AreaOf(c.Id) * 100 < totalLand * settings.MinLandSharePercent))
		{
			return null;
		}

		foreach (var country in countries)
		{
			int cityCount = random.NextInt(settings.MinCitiesPerCountry, settings.MaxCitiesPerCountry + 1);
			for (int i = 0; i < cityCount; i++)
			{
				var site = FindSite(random, terrain, settings, cities,
					settings.EdgeMargin, terrain.SizeX - settings.EdgeMargin, minZ, maxZ,
					accept: (x, z) => IsDeepInside(territory, terrain, country.Id, x, z, settings.BorderMargin));
				if (site == null)
				{
					return null;
				}

				int population = random.NextInt(settings.MinCityPopulationThousands, settings.MaxCityPopulationThousands + 1);
				cities.Add(new City(cities.Count, CityNames[country.Id][i + 1], country.Id, site.Value.X, site.Value.Z,
					population, isCapital: false, morale: 70));
			}
		}

		return new WorldState(countries, cities, territory);
	}

	/// <summary>
	/// True if the point belongs to the country and no other country's land lies within the margin.
	/// Checks 16 points on two rings, which is plenty at the 1 km grid scale.
	/// </summary>
	public static bool IsDeepInside(Territory territory, Terrain terrain, int countryId, Fixed x, Fixed z, Fixed margin)
	{
		if (territory.OwnerAt(terrain, x, z) != countryId)
		{
			return false;
		}

		foreach (var (ox, oz) in RingOffsets)
		{
			foreach (var radius in new[] { margin, margin * Fixed.Half })
			{
				int owner = territory.OwnerAt(terrain, x + radius * ox, z + radius * oz);
				if (owner != -1 && owner != countryId)
				{
					return false;
				}
			}
		}

		return true;
	}

	/// <summary>Eight unit directions (cos/sin of 0°, 45°, ...) in fixed-point.</summary>
	private static readonly (Fixed X, Fixed Z)[] RingOffsets =
	{
		(Fixed.One, Fixed.Zero), (Fixed.FromRatio(7071, 10000), Fixed.FromRatio(7071, 10000)),
		(Fixed.Zero, Fixed.One), (Fixed.FromRatio(-7071, 10000), Fixed.FromRatio(7071, 10000)),
		(-Fixed.One, Fixed.Zero), (Fixed.FromRatio(-7071, 10000), Fixed.FromRatio(-7071, 10000)),
		(Fixed.Zero, -Fixed.One), (Fixed.FromRatio(7071, 10000), Fixed.FromRatio(-7071, 10000)),
	};

	private static (Fixed X, Fixed Z)? FindSite(
		DeterministicRandom random, Terrain terrain, Settings settings, List<City> existing,
		Fixed minX, Fixed maxX, Fixed minZ, Fixed maxZ, Func<Fixed, Fixed, bool>? accept)
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

			if (accept != null && !accept(x, z))
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
