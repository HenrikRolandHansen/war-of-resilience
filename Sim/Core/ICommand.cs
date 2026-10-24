namespace WarOfResilience.Sim.Core;

/// <summary>
/// The only way to change simulation state from outside. Players and the AI send the same commands,
/// which keeps the simulation deterministic and lets multiplayer send commands instead of state.
/// </summary>
public interface ICommand
{
	/// <summary>Id of the player (or AI) that issued the command.</summary>
	int PlayerId { get; }

	void Apply(SimContext context);
}
