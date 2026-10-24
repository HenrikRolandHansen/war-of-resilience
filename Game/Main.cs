using Godot;
using System.Linq;
using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Game;

/// <summary>
/// Main scene. Drives the simulation at a fixed tick rate; rendering only reads its state.
/// </summary>
public partial class Main : Node3D
{
	// Fixed seed for now; later it comes from the match setup.
	private const ulong Seed = 1;

	private readonly Simulation _simulation = new(Seed);
	private double _accumulator;

	public override void _Ready()
	{
		var terrain = TerrainGenerator.Generate(Seed, new TerrainGenerator.Settings());
		_simulation.AddSystem(terrain);

		var world = WorldGenerator.Generate(Seed, terrain, new WorldGenerator.Settings());
		var grid = GridGenerator.Generate(Seed, terrain, world, new GridGenerator.Settings());

		// The grid runs first so cities earn this tick's income with this tick's power.
		_simulation.AddSystem(grid);
		_simulation.AddSystem(world);

		var terrainView = GetNode<TerrainView>("TerrainView");
		float cellSize = (float)terrain.CellSize.ToDouble();
		terrainView.Build(terrain, world.Territory.OwnerAtPoint,
			(x, z) => world.Cities.Max(c => CityBuilder.UrbanAmount(c, x * cellSize, z * cellSize)));

		float sizeX = terrainView.SizeX;
		float sizeZ = terrainView.SizeZ;
		var camera = GetNode<RtsCamera>("RtsCamera");
		camera.Configure(new Rect2(0, 0, sizeX, sizeZ), terrainView.HeightAt);

		GetNode<WorldView>("WorldView").Build(world, terrainView, camera);
		var gridView = new GridView { Name = "GridView" };
		AddChild(gridView);
		gridView.Build(grid, terrainView, camera);
		GetNode<Hud>("Hud").Setup(_simulation, world, grid);
		ApplyStartView(world, camera);

		GD.Print($"War of Resilience started. Simulation runs at {Simulation.TicksPerSecond} ticks/s.");
		foreach (var plant in grid.Plants)
		{
			GD.Print($"  {plant.Kind} plant of country {plant.CountryId}: {plant.CapacityMW} MW at {plant.X.ToDouble() / 1000:0},{plant.Z.ToDouble() / 1000:0} km");
		}

		GD.Print($"Map {sizeX / 1000:0} x {sizeZ / 1000:0} km, {world.Cities.Count} cities, {grid.Plants.Count()} power plants, "
			+ $"{grid.Lines.Count} power lines, checksum {_simulation.ComputeChecksum():X16}.");
	}

	/// <summary>
	/// Optional start view for testing, given after "--" on the command line:
	/// --look-at=CityName (or --look-at=x,z in km) and/or --distance=metres.
	/// </summary>
	private static void ApplyStartView(WorldState world, RtsCamera camera)
	{
		string? cityName = null;
		float? distance = null;
		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--look-at="))
			{
				cityName = arg["--look-at=".Length..].Replace('_', ' ');
			}
			else if (arg.StartsWith("--distance=") && float.TryParse(arg["--distance=".Length..], out var d))
			{
				distance = d;
			}
		}

		var target = new Vector2(camera.Position.X, camera.Position.Z);
		var city = world.Cities.FirstOrDefault(c => c.Name == cityName);
		var parts = cityName?.Split(',');
		if (city != null)
		{
			target = new Vector2((float)city.X.ToDouble(), (float)city.Z.ToDouble());
		}
		else if (parts is { Length: 2 } && float.TryParse(parts[0], out var xKm) && float.TryParse(parts[1], out var zKm))
		{
			target = new Vector2(xKm * 1000, zKm * 1000);
		}
		else if (distance == null)
		{
			return;
		}

		camera.JumpTo(target.X, target.Y, distance ?? camera.Distance);
	}

	public override void _Process(double delta)
	{
		const double tickLength = 1.0 / Simulation.TicksPerSecond;
		_accumulator += delta;
		while (_accumulator >= tickLength)
		{
			_simulation.Step();
			_accumulator -= tickLength;
		}
	}
}
