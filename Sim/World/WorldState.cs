using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.World;

/// <summary>
/// Countries and their cities. Each tick every city pays income to its country, scaled by
/// population and morale, and morale moves one step toward its target.
/// </summary>
public sealed class WorldState : ISimSystem
{
	/// <summary>Credits per second earned by 1,000 people at 100 % morale is 1 / this value.</summary>
	public const int ThousandsPerCreditPerSecond = 10;

	/// <summary>Ticks between each one-point morale change.</summary>
	public const int MoraleStepTicks = Simulation.TicksPerSecond * 2;

	private readonly List<Country> _countries;
	private readonly List<City> _cities;

	public WorldState(IEnumerable<Country> countries, IEnumerable<City> cities, Fixed borderX)
	{
		_countries = countries.OrderBy(c => c.Id).ToList();
		_cities = cities.OrderBy(c => c.Id).ToList();
		BorderX = borderX;
	}

	public IReadOnlyList<Country> Countries => _countries;

	public IReadOnlyList<City> Cities => _cities;

	/// <summary>The border runs north-south at this X. Country 0 is west of it, country 1 east.</summary>
	public Fixed BorderX { get; }

	public Country GetCountry(int id) => _countries.First(c => c.Id == id);

	public IEnumerable<City> CitiesOf(int countryId) => _cities.Where(c => c.CountryId == countryId);

	public void Update(SimContext context)
	{
		foreach (var country in _countries)
		{
			country.IncomeLastTick = Fixed.Zero;
		}

		bool moraleStep = context.Tick % MoraleStepTicks == 0;
		foreach (var city in _cities)
		{
			if (moraleStep && city.Morale != city.TargetMorale)
			{
				city.Morale += Math.Sign(city.TargetMorale - city.Morale);
			}

			var income = IncomePerTick(city);
			var country = GetCountry(city.CountryId);
			country.Money += income;
			country.IncomeLastTick += income;
		}
	}

	public static Fixed IncomePerTick(City city)
	{
		// One division at the end keeps the result exact where possible.
		long numerator = (long)city.PopulationThousands * city.Morale;
		long denominator = (long)ThousandsPerCreditPerSecond * City.MaxMorale * Simulation.TicksPerSecond;
		return Fixed.FromRaw((numerator << Fixed.FractionBits) / denominator);
	}

	public void AddToChecksum(ref Checksum checksum)
	{
		checksum.Add(BorderX);
		foreach (var country in _countries)
		{
			checksum.Add(country.Id);
			checksum.Add(country.Money);
		}

		foreach (var city in _cities)
		{
			checksum.Add(city.Id);
			checksum.Add(city.CountryId);
			checksum.Add(city.X);
			checksum.Add(city.Z);
			checksum.Add(city.PopulationThousands);
			checksum.Add(city.Morale);
			checksum.Add(city.TargetMorale);
		}
	}
}
