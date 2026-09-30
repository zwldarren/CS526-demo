using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>One live enemy. Ids are stable for the enemy's whole life: shots in flight track them.</summary>
    public struct EnemyState
    {
        public int Id;
        public EnemyKind Kind;
        public ShapeType Weakness;
        public float Hp;
        public float MaxHp;
        public Vec2 Position;
        public Vec2 PreviousPosition;
        public float AttackCooldown;

        /// <summary>True once it has stopped to hit something - the Core, or a machine in reach.</summary>
        public bool Attacking;
    }

    /// <summary>One enemy, flattened for the view layer.</summary>
    public struct EnemySnapshot
    {
        public int Id;
        public EnemyKind Kind;
        public ShapeType Weakness;
        public Vec2 PreviousPosition;
        public Vec2 Position;
        public float HealthFraction;
        public bool Attacking;
    }

    /// <summary>
    /// Every enemy on the map, plus the three things that happen to them: walking at the Core, being
    /// shot, and stopping to tear down whatever machine stands in the way.
    ///
    /// <b>Machines block and are attackable; belts are walked over.</b> An enemy walks the
    /// <see cref="PathField"/>'s flow toward the Core - around machines, not through them - but stops
    /// the moment an attackable machine is within <see cref="EnemyDef.AggroRange"/>, and charges one it
    /// notices within <see cref="EnemyDef.DetectionRange"/>: aggressive targeting, not just obstacle
    /// avoidance, so a building beside the path is a target rather than scenery. When the flow has no
    /// direction for a cell (the Core is sealed in), it walks straight at the Core and chews through
    /// whatever blocks the way; a standing enemy is always within its own reach of the blocker it is
    /// pressed against, so the siege resolves itself. Belts are walkable and unattackable,
    /// deliberately: a belt is a flat conveyor, and the 1-cost cell must not become the cheapest wall.
    ///
    /// The Core's rules are untouched: reaching the attack ring stops an enemy and drains
    /// <see cref="CoreState.Hp"/> at its own damage and interval.
    /// </summary>
    public sealed class EnemyField
    {
        /// <summary>
        /// Slack on the Core's reach ring. The ring position is computed by scaling a vector, so the
        /// radius it lands on is <c>reach * (1 +- 1e-7)</c>: without this, an enemy that arrives ends up
        /// standing a float hair outside the ring, fails the range test every tick, and hovers there
        /// forever without attacking. A thousandth of a tile is far below anything visible.
        /// </summary>
        private const float ReachEpsilon = 1e-3f;

        private EnemyState[] _enemies;
        private int _count;

        private readonly ContentDatabase _content;

        /// <summary>The stream enemy damage, kills and Core hits are reported on. Derived telemetry:
        /// nothing reads it back, so it cannot affect a run's outcome.</summary>
        private readonly SimEventBuffer _events;

        private readonly TileGrid _grid;
        private readonly MachineField _machines;
        private readonly PathField _paths;

        /// <summary>Id -> slot. Only ever used for lookup, never enumerated, so it cannot leak order
        /// into the simulation the way an enumerated dictionary would.</summary>
        private readonly Dictionary<int, int> _slotById = new Dictionary<int, int>();

        private int _nextId = 1;

        /// <summary>Every enemy in the field is alive: a killed one is removed the moment it dies,
        /// so this is the wave's remaining work, not a slot count.</summary>
        public int AliveCount => _count;

        public EnemyField(ContentDatabase content, SimEventBuffer events, TileGrid grid,
            MachineField machines, PathField paths, int capacity = 64)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _machines = machines ?? throw new ArgumentNullException(nameof(machines));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _enemies = new EnemyState[Math.Max(8, capacity)];
        }

        public void Clear()
        {
            Array.Clear(_enemies, 0, _count);
            _count = 0;
            _slotById.Clear();
            _nextId = 1;
        }

        /// <summary>Create one enemy. Returns its id, which is what shots home in on.</summary>
        public int Spawn(EnemyKind kind, Vec2 position)
        {
            if (_count == _enemies.Length) Array.Resize(ref _enemies, _enemies.Length * 2);

            EnemyDef spec = _content.Enemy(kind);
            int id = _nextId++;

            _enemies[_count] = new EnemyState
            {
                Id = id,
                Kind = kind,
                Weakness = spec.Weakness,
                Hp = spec.Hp,
                MaxHp = spec.Hp,
                Position = position,
                PreviousPosition = position,
                AttackCooldown = 0f,
            };

            _slotById[id] = _count;
            _count++;
            return id;
        }

        /// <summary>
        /// Advance every enemy, in this order each: hold at the Core's ring and hit it; stop and hit an
        /// attackable machine in reach; walk one step along the flow field toward the Core; or, when the
        /// flow has no direction, walk straight at the Core and hit whatever blocks the way.
        /// </summary>
        public void Step(float dt, ref CoreState core)
        {
            Vec2 centre = core.Center;
            float reach = Balance.CoreAttackRadius;
            float stopAt = reach + ReachEpsilon;

            for (int i = 0; i < _count; i++)
            {
                ref EnemyState e = ref _enemies[i];
                e.PreviousPosition = e.Position;

                EnemyDef spec = _content.Enemy(e.Kind);
                float distance = (centre - e.Position).Magnitude;

                // 1. The Core, first: an enemy inside the ring hits it even if a machine is also in
                // reach, so a defence built right on the Core cannot distract an enemy from losing the
                // run. Unchanged from the straight-line days, including the exact landing on the ring.
                if (distance <= stopAt)
                {
                    e.Attacking = true;
                    e.AttackCooldown -= dt;
                    if (e.AttackCooldown <= 0f)
                    {
                        core.Hp = MathF.Max(0f, core.Hp - spec.Damage);
                        e.AttackCooldown += spec.AttackInterval;
                        _events.CoreDamaged(spec.Damage);
                    }
                    continue;
                }

                // 2. Machines, nearest first: inside the attack reach it stops and hits the machine;
                // between the reach and the notice range it charges it - leaving its path and heading
                // for the building, which is what stops a machine two tiles off the lane from being
                // furniture. One scan covers both, because the nearest machine within either radius is
                // the one in reach whenever anything is: nothing can be closer than the nearest. The
                // same cooldown covers the Core and every machine, so the damage rate is the enemy's
                // rate whatever it is hitting.
                float notice = MathF.Max(spec.AggroRange, spec.DetectionRange);
                if (_machines.TryFindNearest(e.Position, notice, out Int2 target))
                {
                    float gap = (_grid.CellCenter(target) - e.Position).Magnitude;
                    if (gap <= spec.AggroRange)
                    {
                        e.Attacking = true;
                        e.AttackCooldown -= dt;
                        if (e.AttackCooldown <= 0f)
                        {
                            _machines.DamageAt(target, spec.Damage);
                            e.AttackCooldown += spec.AttackInterval;
                        }
                        continue;
                    }

                    e.Attacking = false;
                    e.Position = Advance(e.Position, _grid.CellCenter(target), spec.Speed * dt);
                    continue;
                }

                e.Attacking = false;
                float step = spec.Speed * dt;

                // 3. The flow field: steer at the centre of the next cell on the way to the Core, and
                // land exactly on it rather than stepping past it - overshooting would oscillate around
                // the centre the way it would around the Core's ring.
                Int2 cell = _grid.CellAt(e.Position);
                if (_paths.TryDirection(cell, out Dir dir))
                {
                    e.Position = Advance(e.Position, _grid.CellCenter(cell + dir.Offset()), step);
                    continue;
                }

                // 4. Sealed out: no way from here, so walk straight at the Core like an enemy always
                // used to - but never into a solid cell. A standing enemy is always within its own reach
                // of the blocker (1.6 >= sqrt 2), so step 2 chews it down next tick.
                e.Position = Advance(e.Position, centre, step);
            }
        }

        /// <summary>May a walker enter this cell? Ground, ore, belts and the Core are open; a machine
        /// is a wall whether or not it happens to have HP left.</summary>
        private bool Open(Int2 cell) => _grid.InBounds(cell) && !_grid.Get(cell).IsMachine();

        /// <summary>
        /// Walk one step from <paramref name="from"/> toward <paramref name="to"/>, landing exactly on it
        /// when the step reaches it - and never into a solid cell: a blocked step slides along one axis,
        /// then the other, and stands still if both are closed. That guard is what presses a charging
        /// enemy against the machine it is headed for instead of through it, and it is the same guard
        /// that keeps the siege honest.
        /// </summary>
        private Vec2 Advance(Vec2 from, Vec2 to, float step)
        {
            Vec2 delta = to - from;
            float distance = delta.Magnitude;
            Vec2 wish = distance <= step || distance <= 0f ? to : from + delta * (step / distance);

            if (Open(_grid.CellAt(wish))) return wish;

            var slideX = new Vec2(wish.X, from.Y);
            if (Open(_grid.CellAt(slideX))) return slideX;

            var slideY = new Vec2(from.X, wish.Y);
            if (Open(_grid.CellAt(slideY))) return slideY;

            return from;
        }

        /// <summary>
        /// Apply one shot's damage. A shot always carries the shape its turret eats, so it always
        /// arrives with the shape the enemy is weak to: a wrong-shape line jams before it ever fires,
        /// which is the whole reason the shape decides what a kill costs.
        /// </summary>
        public void ApplyDamage(int id, float baseDamage)
        {
            if (!_slotById.TryGetValue(id, out int slot)) return;

            _enemies[slot].Hp -= baseDamage;

            // Read what the report needs *before* RemoveAt, which reorders the array it would be read
            // from: the kind and the position are gone the moment the enemy is.
            bool killed = _enemies[slot].Hp <= 0f;
            EnemyKind kind = _enemies[slot].Kind;
            Vec2 position = _enemies[slot].Position;

            if (killed)
            {
                _events.EnemyKilled(id, kind, position);
                RemoveAt(slot);
            }
            else
            {
                _events.EnemyDamaged(id, kind, position, baseDamage);
            }
        }

        /// <summary>One enemy by slot, for a reader that wants a single slot. Returns a copy on
        /// purpose: nobody may hold a reference into an array that kills reorder.</summary>
        public EnemyState EnemyAt(int index) => _enemies[index];

        /// <summary>
        /// Nearest live enemy within <paramref name="range"/> of <paramref name="centre"/>, and where
        /// it is. Ties go to the lower slot, so which of two equidistant enemies a turret picks is a
        /// function of the map state and not of anything the caller did. False when nothing is in
        /// range, in which case the outputs are unusable.
        /// </summary>
        public bool TryFindNearest(Vec2 centre, float range, out int id, out Vec2 position)
        {
            float bestSq = range * range;
            id = 0;
            position = Vec2.Zero;

            for (int i = 0; i < _count; i++)
            {
                ref readonly EnemyState enemy = ref _enemies[i];
                if (enemy.Hp <= 0f) continue;

                Vec2 delta = enemy.Position - centre;
                float sq = delta.X * delta.X + delta.Y * delta.Y;
                if (sq > bestSq) continue;

                bestSq = sq;
                id = enemy.Id;
                position = enemy.Position;
            }

            return id != 0;
        }

        public bool TryGetPosition(int id, out Vec2 position)
        {
            if (_slotById.TryGetValue(id, out int slot))
            {
                position = _enemies[slot].Position;
                return true;
            }

            position = Vec2.Zero;
            return false;
        }

        /// <summary>Every enemy, in slot order, with the positions the view interpolates between.</summary>
        public void GetEnemies(List<EnemySnapshot> into)
        {
            into.Clear();
            for (int i = 0; i < _count; i++)
            {
                ref readonly EnemyState e = ref _enemies[i];
                into.Add(new EnemySnapshot
                {
                    Id = e.Id,
                    Kind = e.Kind,
                    Weakness = e.Weakness,
                    PreviousPosition = e.PreviousPosition,
                    Position = e.Position,
                    HealthFraction = e.MaxHp <= 0f ? 0f : e.Hp / e.MaxHp,
                    Attacking = e.Attacking,
                });
            }
        }

        private void RemoveAt(int slot)
        {
            int id = _enemies[slot].Id;
            _slotById.Remove(id);

            int last = _count - 1;
            if (slot != last)
            {
                _enemies[slot] = _enemies[last];
                _slotById[_enemies[slot].Id] = slot;
            }

            _enemies[last] = default;
            _count--;
        }
    }
}
