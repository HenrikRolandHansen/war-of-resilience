using Godot;
using WarOfResilience.Sim.Core;

namespace WarOfResilience.Game;

/// <summary>
/// Main scene. Drives the simulation at a fixed tick rate; rendering only reads its state.
/// </summary>
public partial class Main : Node3D
{
	// Fixed seed for now; later it comes from the match setup.
	private readonly Simulation _simulation = new(seed: 1);
	private double _accumulator;

	public override void _Ready()
	{
		GD.Print($"War of Resilience started. Simulation runs at {Simulation.TicksPerSecond} ticks/s.");
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
