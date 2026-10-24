namespace WarOfResilience.Sim.Core;

/// <summary>
/// One part of the simulation (economy, power grid, units, ...). Updated once per tick,
/// in the order the systems were added.
/// </summary>
public interface ISimSystem
{
	void Update(SimContext context);

	/// <summary>Adds all state that matters for determinism to the checksum.</summary>
	void AddToChecksum(ref Checksum checksum);
}
