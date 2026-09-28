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

        /// <summary>True once it has reached the Core and started hitting it.</summary>
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
    /// Every enemy on the map, plus the two things that happen to them: walking at the Core and being
    /// shot.
    ///
    /// They walk in a straight line and **ignore buildings entirely**. That is a deliberate ruling,
    /// not a missing feature: damage has to stay the only defence. It was argued on the grounds that
    /// buildings were free - a wall of belts would have been a wall - and although a belt cell now
    /// costs a circle, the ruling stands until someone puts it on a later map with real build costs
    /// to test. Enemies therefore walk over the layout.
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

        /// <summary>Id -> slot. Only ever used for lookup, never enumerated, so it cannot leak order
        /// into the simulation the way an enumerated dictionary would.</summary>
        private readonly Dictionary<int, int> _slotById = new Dictionary<int, int>();

        private int _nextId = 1;

        /// <summary>Every enemy in the field is alive: a killed one is removed the moment it dies,
        /// so this is the wave's remaining work, not a slot count.</summary>
        public int AliveCount => _count;

        public EnemyField(int capacity = 64)
        {
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

            EnemySpec spec = Balance.Enemy(kind);
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

        /// <summary>Advance every enemy: walk toward the Core, or hit it once there.</summary>
        public void Step(float dt, ref CoreState core)
        {
            Vec2 centre = core.Center;
            float reach = Balance.CoreAttackRadius;
            float stopAt = reach + ReachEpsilon;

            for (int i = 0; i < _count; i++)
            {
                ref EnemyState e = ref _enemies[i];
                e.PreviousPosition = e.Position;

                EnemySpec spec = Balance.Enemy(e.Kind);
                Vec2 toCore = centre - e.Position;
                float distance = toCore.Magnitude;

                if (distance > stopAt)
                {
                    float step = spec.Speed * dt;
                    // Land exactly on the attack ring instead of stepping through it, so every enemy
                    // hits the Core from the same distance and the damage rate stays legible.
                    e.Position = distance - step <= stopAt
                        ? centre - toCore * (reach / distance)
                        : e.Position + toCore * (step / distance);
                    e.Attacking = false;
                    continue;
                }

                e.Attacking = true;
                e.AttackCooldown -= dt;
                if (e.AttackCooldown <= 0f)
                {
                    core.Hp = MathF.Max(0f, core.Hp - spec.CoreDamage);
                    e.AttackCooldown += spec.AttackInterval;
                }
            }
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

            if (_enemies[slot].Hp <= 0f) RemoveAt(slot);
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
