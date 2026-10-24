using System.Text.Json;
using System.Text.Json.Serialization;
using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Units;

/// <summary>All unit types plus the game's time scale, parsed from the JSON in Data/units.json.</summary>
public sealed class UnitCatalog
{
	private readonly Dictionary<string, UnitType> _byId;

	private UnitCatalog(int timeScale, IReadOnlyList<UnitType> types)
	{
		TimeScale = timeScale;
		Types = types;
		_byId = types.ToDictionary(t => t.Id);
	}

	/// <summary>Game seconds that pass per real second.</summary>
	public int TimeScale { get; }

	public IReadOnlyList<UnitType> Types { get; }

	public UnitType this[string id] => _byId.TryGetValue(id, out var type)
		? type
		: throw new KeyNotFoundException($"Unknown unit type '{id}'.");

	/// <summary>Metres a unit of this type moves in one tick, at top speed.</summary>
	public Fixed StepPerTick(UnitType type) => StepPerTick(type.SpeedKmh);

	/// <summary>Metres something moving at a real-world speed covers in one tick.</summary>
	public Fixed StepPerTick(int speedKmh)
	{
		// km/h -> m/s is * 1000 / 3600; one division at the end keeps it exact where possible.
		long numerator = (long)speedKmh * 1000 * TimeScale;
		long denominator = 3600L * Simulation.TicksPerSecond;
		return Fixed.FromRaw((numerator << Fixed.FractionBits) / denominator);
	}

	public static UnitCatalog Parse(string json)
	{
		var file = JsonSerializer.Deserialize<CatalogFile>(json, Options)
			?? throw new FormatException("Unit catalog is empty.");
		if (file.TimeScale <= 0)
		{
			throw new FormatException("timeScale must be positive.");
		}

		var types = new List<UnitType>();
		foreach (var entry in file.Units)
		{
			if (string.IsNullOrWhiteSpace(entry.Id) || entry.SpeedKmh <= 0 || entry.HitPoints <= 0)
			{
				throw new FormatException($"Unit '{entry.Id}' needs an id, a positive speed and positive hit points.");
			}

			types.Add(new UnitType
			{
				Id = entry.Id,
				Name = entry.Name ?? entry.Id,
				Domain = entry.Domain,
				SpeedKmh = entry.SpeedKmh,
				CruiseAltitudeMetres = entry.CruiseAltitudeMetres,
				HitPoints = entry.HitPoints,
				SightMetres = entry.SightMetres,
				Cost = entry.Cost,
				AttackDamage = entry.AttackDamage,
				AirDefence = entry.AirDefence is { } ad ? Validate(entry.Id, ad) : null,
				Index = types.Count,
			});
		}

		if (types.Select(t => t.Id).Distinct().Count() != types.Count)
		{
			throw new FormatException("Unit ids must be unique.");
		}

		return new UnitCatalog(file.TimeScale, types);
	}

	private static AirDefenceStats Validate(string id, AirDefenceStats stats)
	{
		if (stats.RangeMetres <= 0 || stats.Magazine <= 0 || stats.ReloadTicks <= 0 || stats.RearmTicks <= 0
			|| stats.MissileSpeedKmh <= 0 || stats.MissileDamage <= 0 || stats.HitChancePercent is < 0 or > 100)
		{
			throw new FormatException($"Unit '{id}' has invalid air defence stats.");
		}

		return stats;
	}

	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
	};

	private sealed class CatalogFile
	{
		public int TimeScale { get; set; }

		public List<Entry> Units { get; set; } = new();
	}

	private sealed class Entry
	{
		public string Id { get; set; } = "";

		public string? Name { get; set; }

		public UnitDomain Domain { get; set; }

		public int SpeedKmh { get; set; }

		public int CruiseAltitudeMetres { get; set; }

		public int HitPoints { get; set; }

		public int SightMetres { get; set; }

		public int Cost { get; set; }

		public int AttackDamage { get; set; }

		public AirDefenceStats? AirDefence { get; set; }
	}
}
