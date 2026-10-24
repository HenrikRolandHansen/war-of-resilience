using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.World;

/// <summary>
/// Grows each country outward from its capital over land, like a flood fill where crossing
/// mountains and steep ground costs more. Where two countries meet becomes the border, so borders
/// tend to follow ridges and lakes instead of straight lines. Water is never claimed.
/// </summary>
public static class TerritoryGenerator
{
	/// <summary>Cost of one straight step between neighbouring grid points.</summary>
	private const long StepCost = 100;

	/// <summary>Cost of one diagonal step (about StepCost × √2).</summary>
	private const long DiagonalCost = 141;

	/// <summary>Extra cost per metre of climb or descent on a step.</summary>
	private const long SlopeCostPerMetre = 3;

	/// <summary>Extra cost per metre of altitude above sea level, so high ground tends to become border land.</summary>
	private const long AltitudeCostDivisor = 8;

	private static readonly (int Dx, int Dz, long Cost)[] Neighbours =
	{
		(1, 0, StepCost), (-1, 0, StepCost), (0, 1, StepCost), (0, -1, StepCost),
		(1, 1, DiagonalCost), (-1, 1, DiagonalCost), (1, -1, DiagonalCost), (-1, -1, DiagonalCost),
	};

	public static Territory Generate(Terrain terrain, IReadOnlyList<City> capitals)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		int count = width * depth;

		var heights = new int[count];
		var isLand = new bool[count];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				var h = terrain.HeightAtPoint(x, z);
				int i = z * width + x;
				heights[i] = h.FloorToInt();
				isLand[i] = h >= terrain.SeaLevel;
			}
		}

		int seaLevel = terrain.SeaLevel.FloorToInt();
		var owners = new byte[count];
		Array.Fill(owners, Territory.Nobody);
		var cost = new long[count];
		Array.Fill(cost, long.MaxValue);

		// Priority = cost * count + index, so ties always break the same way on every machine.
		var queue = new PriorityQueue<int, long>();
		foreach (var capital in capitals)
		{
			int cx = Math.Clamp((capital.X / terrain.CellSize).FloorToInt(), 0, width - 1);
			int cz = Math.Clamp((capital.Z / terrain.CellSize).FloorToInt(), 0, depth - 1);
			int start = cz * width + cx;
			cost[start] = 0;
			owners[start] = (byte)capital.CountryId;
			queue.Enqueue(start, start);
		}

		while (queue.TryDequeue(out int index, out long priority))
		{
			long current = priority / count;
			if (current != cost[index])
			{
				continue;
			}

			int x = index % width;
			int z = index / width;
			foreach (var (dx, dz, stepCost) in Neighbours)
			{
				int nx = x + dx;
				int nz = z + dz;
				if (nx < 0 || nz < 0 || nx >= width || nz >= depth)
				{
					continue;
				}

				int next = nz * width + nx;
				if (!isLand[next])
				{
					continue;
				}

				long climb = Math.Abs(heights[next] - heights[index]);
				long altitude = Math.Max(0, heights[next] - seaLevel);
				long nextCost = current + stepCost + climb * SlopeCostPerMetre + altitude / AltitudeCostDivisor;
				if (nextCost < cost[next])
				{
					cost[next] = nextCost;
					owners[next] = owners[index];
					queue.Enqueue(next, nextCost * count + next);
				}
			}
		}

		return new Territory(width, depth, owners);
	}
}
