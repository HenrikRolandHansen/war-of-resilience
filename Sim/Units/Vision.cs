using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Sim.Units;

/// <summary>
/// Which enemy units each country can see. A unit is seen when it is over the country's own land
/// (police, radar and the public report it) or within sight of one of the country's own units or
/// cities. Infrastructure (cities, plants, lines) is always known: it is on any map. Recomputed
/// every tick after units move.
/// </summary>
public sealed class Vision : ISimSystem
{
	/// <summary>How far a city's police, cameras and people report enemy units.</summary>
	public const int CitySightMetres = 12_000;

	private readonly UnitManager _units;
	private readonly WorldState _world;
	private readonly HashSet<int>[] _visible;

	public Vision(UnitManager units, WorldState world)
	{
		_units = units;
		_world = world;
		_visible = world.Countries.Select(_ => new HashSet<int>()).ToArray();
		Recalculate();
	}

	public bool CanSee(int countryId, Unit unit) =>
		unit.OwnerId == countryId || (countryId >= 0 && countryId < _visible.Length && _visible[countryId].Contains(unit.Id));

	/// <summary>Sight sources of a country: (x, z, radius in metres).</summary>
	public IEnumerable<(Fixed X, Fixed Z, int Radius)> SourcesOf(int countryId)
	{
		foreach (var unit in _units.Units)
		{
			if (unit.OwnerId == countryId && unit.IsAlive)
			{
				yield return (unit.X, unit.Z, unit.Type.SightMetres);
			}
		}

		foreach (var city in _world.CitiesOf(countryId))
		{
			yield return (city.X, city.Z, CitySightMetres);
		}
	}

	public void Update(SimContext context) => Recalculate();

	private void Recalculate()
	{
		for (int country = 0; country < _visible.Length; country++)
		{
			_visible[country].Clear();
			var sources = SourcesOf(country).ToList();
			foreach (var unit in _units.Units)
			{
				if (unit.OwnerId != country && unit.IsAlive
					&& (_world.Territory.OwnerAt(_units.Terrain, unit.X, unit.Z) == country
						|| sources.Any(s => Within(s.X - unit.X, s.Z - unit.Z, s.Radius))))
				{
					_visible[country].Add(unit.Id);
				}
			}
		}
	}

	/// <summary>Compares squared distances in km so the squares stay small.</summary>
	private static bool Within(Fixed dx, Fixed dz, int metres)
	{
		var km = Fixed.FromInt(1000);
		var x = dx / km;
		var z = dz / km;
		var r = Fixed.FromInt(metres) / km;
		return x * x + z * z <= r * r;
	}

	public void AddToChecksum(ref Checksum checksum)
	{
		foreach (var visible in _visible)
		{
			checksum.Add(visible.Count);
			foreach (var id in visible.OrderBy(id => id))
			{
				checksum.Add(id);
			}
		}
	}
}
