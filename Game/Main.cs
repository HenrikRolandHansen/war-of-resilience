using Godot;
using WarOfResilience.Sim.Core;
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
		_simulation.AddSystem(world);

		var terrainView = GetNode<TerrainView>("TerrainView");
		terrainView.Build(terrain);

		float sizeX = terrainView.SizeX;
		float sizeZ = terrainView.SizeZ;
		var camera = GetNode<RtsCamera>("RtsCamera");
		camera.Configure(new Rect2(0, 0, sizeX, sizeZ), terrainView.HeightAt);

		GetNode<WorldView>("WorldView").Build(world, terrainView, camera);
		GetNode<Hud>("Hud").Setup(_simulation, world);

		GD.Print($"War of Resilience started. Simulation runs at {Simulation.TicksPerSecond} ticks/s.");
		GD.Print($"Map {sizeX / 1000:0} x {sizeZ / 1000:0} km, {world.Cities.Count} cities, checksum {_simulation.ComputeChecksum():X16}.");
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
