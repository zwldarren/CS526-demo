using System;

namespace Facet.Core
{
    /// <summary>
    /// The twist, in code. Every tick, for every turret, in map order:
    ///
    ///   1. A delivery of the wrong shape at any of its inputs **jams that belt cell**. It does not
    ///      matter whether the turret wanted to shoot or was cooling down - the doc's rule is about the
    ///      delivery, and the jam is what silences everything downstream of that segment.
    ///   2. A delivery of the right shape arms the turret. The item stays on the belt, not in the turret:
    ///      a turret with nothing in range banks its ammo on the belt behind it and its line backs up.
    ///   3. If it is armed, has a target in range, and its cooldown has expired, it eats that item and
    ///      fires. So the fire rate is min(spec rate, what the line actually delivers): one belt is one
    ///      item per second, so a turret fed by one belt fires one shot per second.
    ///
    /// Nothing here can fire without a shape arriving, which is the entire mechanic: damage is a
    /// throughput property of the layout, not a purchase.
    /// </summary>
    public sealed class TurretSystem
    {
        private readonly TileGrid _grid;
        private readonly BeltField _belts;
        private readonly MachineField _machines;
        private readonly EnemyField _enemies;
        private readonly ProjectileField _projectiles;

        /// <summary>Shots that have actually been fired. Telemetry, not state: it is what tells a
        /// player (and a test) whether a line is delivering, as opposed to what it could deliver.</summary>
        public int ShotsFired { get; private set; }

        public TurretSystem(TileGrid grid, BeltField belts, MachineField machines,
            EnemyField enemies, ProjectileField projectiles)
        {
            _grid = grid;
            _belts = belts;
            _machines = machines;
            _enemies = enemies;
            _projectiles = projectiles;
        }

        public void Reset() => ShotsFired = 0;

        public void Step(float dt)
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    var cell = new Int2(x, y);
                    ref MachineState machine = ref _machines.RefAt(cell);
                    if (machine.Kind != TileKind.Turret) continue;

                    StepTurret(ref machine, cell, dt);
                }
            }
        }

        private void StepTurret(ref MachineState turret, Int2 cell, float dt)
        {
            TurretSpec spec = Balance.Turret(turret.Build);
            turret.Cooldown = MathF.Max(0f, turret.Cooldown - dt);

            Vec2 centre = _grid.CellCenter(cell);
            Aim(ref turret, centre, spec.Range);

            if (MachineDelivery.TryFindWrongDelivery(_belts, cell, spec.Ammo, out Int2 wrong))
            {
                _belts.TryJam(wrong);
                turret.Armed = false;
                return;
            }

            bool armed = MachineDelivery.TryFindDelivery(_belts, cell, spec.Ammo, requireShape: true,
                out Int2 delivery, out _);
            turret.Armed = armed;

            if (!armed || !turret.HasTarget || turret.Cooldown > 0f) return;

            _belts.TryTakeItem(delivery, out _);
            turret.Cooldown = spec.FireInterval;
            ShotsFired++;
            _projectiles.Spawn(centre + turret.Aim * 0.42f, turret.TargetId, turret.TargetPosition, spec);
        }

        /// <summary>Nearest enemy in range, and the barrel turned toward it. Which enemy that is comes
        /// from the field itself, so the turret does not walk its slots.</summary>
        private void Aim(ref MachineState turret, Vec2 centre, float range)
        {
            bool found = _enemies.TryFindNearest(centre, range, out int targetId, out Vec2 targetPosition);
            turret.HasTarget = found;
            turret.TargetId = found ? targetId : 0;
            if (!found) return;

            turret.TargetPosition = targetPosition;
            Vec2 direction = targetPosition - centre;
            float length = direction.Magnitude;
            turret.Aim = length > 1e-4f ? direction / length : turret.Direction.ToVec();
        }
    }
}
