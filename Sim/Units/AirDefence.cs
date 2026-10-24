using WarOfResilience.Sim.Core;

namespace WarOfResilience.Sim.Units;

/// <summary>
/// Surface-to-air missiles. Every living unit with <see cref="UnitType.AirDefence"/> fires at the
/// enemy aircraft in range, preferring ones no missile is chasing yet, then the closest. Missiles
/// fly at the target's current position and roll for a hit when they reach it. Launchers carry a
/// small magazine that refills slowly, so a swarm can still get through. Runs after
/// <see cref="UnitManager"/>.
/// </summary>
public sealed class AirDefence : ISimSystem
{
	/// <summary>Interceptions are kept this many ticks so the view can show them.</summary>
	private const int RecentTicks = 20;

	/// <summary>Missiles leave the launcher this far above it.</summary>
	private static readonly Fixed LaunchHeight = Fixed.FromInt(6);

	private readonly UnitManager _units;
	private readonly SortedDictionary<int, Battery> _batteries = new();
	private readonly List<Missile> _missiles = new();
	private readonly List<Interception> _recent = new();
	private int _nextMissileId;

	public AirDefence(UnitManager units)
	{
		_units = units;
	}

	public IReadOnlyList<Missile> Missiles => _missiles;

	/// <summary>Missiles that reached their target in the last couple of seconds, hit or miss.</summary>
	public IReadOnlyList<Interception> Recent => _recent;

	/// <summary>Missiles fired since the start.</summary>
	public int Launched => _nextMissileId;

	/// <summary>Missiles ready to fire, or 0 for units without air defence.</summary>
	public int AmmoOf(Unit unit) => unit.Type.AirDefence is { } stats
		? _batteries.TryGetValue(unit.Id, out var battery) ? battery.Ammo : stats.Magazine
		: 0;

	public void Update(SimContext context)
	{
		_recent.RemoveAll(e => e.Tick <= context.Tick - RecentTicks);
		FlyMissiles(context);

		foreach (var unit in _units.Units)
		{
			if (unit.Type.AirDefence is not { } stats || !unit.IsAlive)
			{
				continue;
			}

			if (!_batteries.TryGetValue(unit.Id, out var battery))
			{
				battery = new Battery { Ammo = stats.Magazine };
				_batteries.Add(unit.Id, battery);
			}

			if (battery.Ammo < stats.Magazine && ++battery.RearmTimer >= stats.RearmTicks)
			{
				battery.Ammo++;
				battery.RearmTimer = 0;
			}

			if (battery.Cooldown > 0)
			{
				battery.Cooldown--;
				continue;
			}

			if (battery.Ammo == 0 || ChooseTarget(unit, stats) is not { } target)
			{
				continue;
			}

			_missiles.Add(new Missile(_nextMissileId++, unit.OwnerId, unit.Id, target.Id, unit.X, unit.Y + LaunchHeight, unit.Z));
			battery.Ammo--;
			battery.Cooldown = stats.ReloadTicks;
		}
	}

	private Unit? ChooseTarget(Unit launcher, AirDefenceStats stats)
	{
		var range = Fixed.FromInt(stats.RangeMetres);
		var rangeSquared = range * range;
		Unit? best = null;
		int bestChasers = int.MaxValue;
		Fixed bestDistance = Fixed.Zero;
		foreach (var unit in _units.Units)
		{
			if (unit.OwnerId == launcher.OwnerId || !unit.IsAlive || unit.Type.Domain != UnitDomain.Air)
			{
				continue;
			}

			var dx = unit.X - launcher.X;
			var dz = unit.Z - launcher.Z;
			var distance = dx * dx + dz * dz;
			if (distance > rangeSquared)
			{
				continue;
			}

			int chasers = _missiles.Count(m => m.TargetId == unit.Id);
			if (chasers < bestChasers || (chasers == bestChasers && distance < bestDistance))
			{
				best = unit;
				bestChasers = chasers;
				bestDistance = distance;
			}
		}

		return best;
	}

	private void FlyMissiles(SimContext context)
	{
		for (int i = 0; i < _missiles.Count; i++)
		{
			var missile = _missiles[i];
			var target = _units.Units[missile.TargetId];
			var stats = _units.Units[missile.LauncherId].Type.AirDefence!;
			if (!target.IsAlive)
			{
				// Something else got it first; the missile self-destructs.
				_recent.Add(new Interception(missile.Id, missile.X, missile.Y, missile.Z, false, context.Tick));
				_missiles.RemoveAt(i--);
				continue;
			}

			var step = _units.Catalog.StepPerTick(stats.MissileSpeedKmh);
			var dx = target.X - missile.X;
			var dy = target.Y - missile.Y;
			var dz = target.Z - missile.Z;
			var distance = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
			if (distance > step)
			{
				missile.X += dx / distance * step;
				missile.Y += dy / distance * step;
				missile.Z += dz / distance * step;
				continue;
			}

			bool hit = context.Random.Chance(stats.HitChancePercent);
			if (hit)
			{
				UnitManager.Damage(target, stats.MissileDamage);
			}

			_recent.Add(new Interception(missile.Id, target.X, target.Y, target.Z, hit, context.Tick));
			_missiles.RemoveAt(i--);
		}
	}

	public void AddToChecksum(ref Checksum checksum)
	{
		foreach (var (id, battery) in _batteries)
		{
			checksum.Add(id);
			checksum.Add(battery.Ammo);
			checksum.Add(battery.Cooldown);
			checksum.Add(battery.RearmTimer);
		}

		foreach (var missile in _missiles)
		{
			checksum.Add(missile.Id);
			checksum.Add(missile.TargetId);
			checksum.Add(missile.X);
			checksum.Add(missile.Y);
			checksum.Add(missile.Z);
		}
	}

	private sealed class Battery
	{
		public int Ammo;
		public int Cooldown;
		public int RearmTimer;
	}
}

/// <summary>A surface-to-air missile in flight.</summary>
public sealed class Missile
{
	internal Missile(int id, int ownerId, int launcherId, int targetId, Fixed x, Fixed y, Fixed z)
	{
		Id = id;
		OwnerId = ownerId;
		LauncherId = launcherId;
		TargetId = targetId;
		X = x;
		Y = y;
		Z = z;
	}

	public int Id { get; }

	public int OwnerId { get; }

	public int LauncherId { get; }

	public int TargetId { get; }

	public Fixed X { get; internal set; }

	public Fixed Y { get; internal set; }

	public Fixed Z { get; internal set; }
}

/// <summary>Where a missile ended: on its target (hit or miss) or self-destructed.</summary>
public readonly record struct Interception(int MissileId, Fixed X, Fixed Y, Fixed Z, bool Hit, long Tick);
