using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Sim.Grid;

/// <summary>
/// Shares power between everything that is connected. Each tick the grid is split into islands of
/// working nodes joined by working lines; every city on an island gets the same share of its demand
/// (supply / demand, at most 100 %). Cities short of power aim for lower morale and earn less.
/// </summary>
public sealed class PowerGrid : ISimSystem
{
	/// <summary>Average demand of 1,000 people, in megawatts.</summary>
	public const int MegawattsPerThousandPeople = 1;

	/// <summary>Morale points a city's target drops when it has no power at all.</summary>
	public const int BlackoutMoralePenalty = 40;

	private readonly WorldState _world;
	private readonly List<GridNode> _nodes;
	private readonly List<PowerLine> _lines;
	private readonly int[] _island;
	private readonly CountryPower[] _countryPower;

	public PowerGrid(WorldState world, IEnumerable<GridNode> nodes, IEnumerable<PowerLine> lines)
	{
		_world = world;
		_nodes = nodes.OrderBy(n => n.Id).ToList();
		_lines = lines.OrderBy(l => l.Id).ToList();
		_island = new int[_nodes.Count];
		_countryPower = new CountryPower[world.Countries.Count];

		for (int i = 0; i < _nodes.Count; i++)
		{
			if (_nodes[i].Id != i)
			{
				throw new ArgumentException("Grid node ids must run from 0 without gaps.", nameof(nodes));
			}
		}

		Recalculate();
	}

	public IReadOnlyList<GridNode> Nodes => _nodes;

	public IEnumerable<PowerPlant> Plants => _nodes.OfType<PowerPlant>();

	public IEnumerable<Substation> Substations => _nodes.OfType<Substation>();

	public IReadOnlyList<PowerLine> Lines => _lines;

	/// <summary>Produced and needed power for a country, from the latest tick.</summary>
	public CountryPower PowerOf(int countryId) => _countryPower[countryId];

	public static int DemandMW(City city) => city.PopulationThousands * MegawattsPerThousandPeople;

	public void Update(SimContext context) => Recalculate();

	private void Recalculate()
	{
		// Union-find over working nodes and lines. Lower ids win, so the result does not depend on line order.
		for (int i = 0; i < _island.Length; i++)
		{
			_island[i] = i;
		}

		foreach (var line in _lines)
		{
			if (line.IsOperational && _nodes[line.FromNodeId].IsOperational && _nodes[line.ToNodeId].IsOperational)
			{
				int a = Find(line.FromNodeId);
				int b = Find(line.ToNodeId);
				if (a != b)
				{
					_island[Math.Max(a, b)] = Math.Min(a, b);
				}
			}
		}

		var supply = new long[_nodes.Count];
		var demand = new long[_nodes.Count];
		foreach (var node in _nodes)
		{
			if (!node.IsOperational)
			{
				continue;
			}

			int island = Find(node.Id);
			switch (node)
			{
				case PowerPlant plant:
					supply[island] += plant.CapacityMW;
					break;
				case Substation substation:
					demand[island] += DemandMW(_world.Cities[substation.CityId]);
					break;
			}
		}

		Array.Clear(_countryPower);
		foreach (var plant in Plants)
		{
			if (plant.IsOperational)
			{
				_countryPower[plant.CountryId].ProducedMW += plant.CapacityMW;
			}
		}

		foreach (var node in _nodes)
		{
			node.IsEnergized = node.IsOperational && supply[Find(node.Id)] > 0;
		}

		foreach (var line in _lines)
		{
			line.IsEnergized = line.IsOperational && _nodes[line.FromNodeId].IsEnergized && _nodes[line.ToNodeId].IsEnergized
				&& Find(line.FromNodeId) == Find(line.ToNodeId);
		}

		foreach (var city in _world.Cities)
		{
			city.PowerPercent = 0;
		}

		foreach (var substation in Substations)
		{
			if (!substation.IsOperational)
			{
				continue;
			}

			int island = Find(substation.Id);
			long need = demand[island];
			int percent = need == 0 ? 100 : (int)Math.Min(100, supply[island] * 100 / need);
			var city = _world.Cities[substation.CityId];
			city.PowerPercent = Math.Max(city.PowerPercent, percent);
		}

		foreach (var city in _world.Cities)
		{
			city.TargetMorale = Math.Max(0, city.BaseMorale - (100 - city.PowerPercent) * BlackoutMoralePenalty / 100);
			_countryPower[city.CountryId].DemandMW += DemandMW(city);
			_countryPower[city.CountryId].SuppliedMW += DemandMW(city) * city.PowerPercent / 100;
		}
	}

	private int Find(int node)
	{
		while (_island[node] != node)
		{
			_island[node] = _island[_island[node]];
			node = _island[node];
		}

		return node;
	}

	public void AddToChecksum(ref Checksum checksum)
	{
		foreach (var node in _nodes)
		{
			checksum.Add(node.Id);
			checksum.Add(node.CountryId);
			checksum.Add(node.X);
			checksum.Add(node.Z);
			checksum.Add(node.IsOperational ? 1 : 0);
			checksum.Add(node.HitPoints);
			if (node is PowerPlant plant)
			{
				checksum.Add((int)plant.Kind);
				checksum.Add(plant.CapacityMW);
			}
		}

		foreach (var line in _lines)
		{
			checksum.Add(line.FromNodeId);
			checksum.Add(line.ToNodeId);
			checksum.Add(line.IsOperational ? 1 : 0);
			checksum.Add(line.HitPoints);
		}
	}

	public struct CountryPower
	{
		/// <summary>Output of all working plants.</summary>
		public int ProducedMW;

		/// <summary>What all cities would use with full power.</summary>
		public int DemandMW;

		/// <summary>What cities actually receive.</summary>
		public int SuppliedMW;
	}
}
