namespace Facet.Core
{
    /// <summary>The one enemy map 1 ships with.</summary>
    public enum EnemyKind : byte
    {
        /// <summary>Fast, light, weak to ◠. Two cannon shots bring one down, so the question is
        /// only how fast the line behind the cannon can feed it.</summary>
        Spike = 0,
    }

    /// <summary>One turret's numbers. Names come from the design doc; a turret's diet is what makes it fire.</summary>
    public sealed class TurretSpec
    {
        public readonly BuildKind Build;
        public readonly string Name;
        public readonly ShapeType Ammo;
        public readonly float FireInterval;
        public readonly float Damage;
        public readonly float Range;
        public readonly float ProjectileSpeed;

        public TurretSpec(BuildKind build, string name, ShapeType ammo, float fireInterval,
            float damage, float range, float projectileSpeed)
        {
            Build = build;
            Name = name;
            Ammo = ammo;
            FireInterval = fireInterval;
            Damage = damage;
            Range = range;
            ProjectileSpeed = projectileSpeed;
        }

        /// <summary>Shots per second at an unlimited supply. Whether it ever reaches this is the
        /// belt's business: one belt delivers one item per second, so this is a ceiling, not a rate.</summary>
        public float ShotsPerSecond => FireInterval <= 0f ? 0f : 1f / FireInterval;
    }

    /// <summary>One enemy's numbers, including what it is weak to.</summary>
    public sealed class EnemySpec
    {
        public readonly EnemyKind Kind;
        public readonly string Name;
        public readonly float Hp;
        public readonly float Speed;
        public readonly float CoreDamage;
        public readonly float AttackInterval;
        public readonly ShapeType Weakness;

        public EnemySpec(EnemyKind kind, string name, float hp, float speed, float coreDamage,
            float attackInterval, ShapeType weakness)
        {
            Kind = kind;
            Name = name;
            Hp = hp;
            Speed = speed;
            CoreDamage = coreDamage;
            AttackInterval = attackInterval;
            Weakness = weakness;
        }
    }

    /// <summary>
    /// Every gameplay number in the prototype, in one table, next to the reasoning that produced it.
    ///
    /// The economy to reason with: a belt cell holds one item and moves at one tile per second
    /// (<see cref="SimConfig.BeltSpeed"/>), so a belt delivers exactly 1 shape/s. A drill mines at
    /// that same second per item, and a decomposer takes 1.5 s per circle but pushes two halves,
    /// which makes its output 1.33 halves/s - more than one belt can carry away, so a decomposer
    /// at full tilt asks for a second outlet. That mismatch is the bottleneck puzzle the design poses.
    ///
    /// The cannon's fire rate is a ceiling, not a rate: 1 shot/s needs one belt delivering, and
    /// "upgrading means expanding supply" is enforced by there being nothing here to buy - only
    /// circles to route.
    /// </summary>
    public static class Balance
    {
        /// <summary>Seconds per mined item. Matched to one belt, so a drill exactly saturates a line:
        /// one item every 30 ticks at 30 Hz, never 31.</summary>
        public const float DrillInterval = 1f;

        /// <summary>
        /// Seconds to split one circle into two half-circles. A drill feeds circles at 1/s but the
        /// split consumes them at 0.67/s, so circles back up behind the decomposer - it, not the
        /// drill, is the stage that starves the line behind it, and a second decomposer is the fix.
        /// Each split yields two halves, so one belt pointing away caps the machine at 1 half/s; wire
        /// a second outlet and it runs at its own 1.33 halves/s. Two cannons at full rate therefore
        /// ask for two decomposers - or one with both outlets wired, split and shared, at 0.67 shots/s
        /// each.
        /// </summary>
        public const float DecomposeInterval = 1.5f;

        /// <summary>Half-circles one split yields: the two halves of the circle that went in, and the
        /// reason a decomposer's output outruns the belt carrying it away.</summary>
        public const int HalvesPerSplit = 2;

        /// <summary>Half-circles a decomposer can hold waiting for its output belt: two splits'
        /// worth (<see cref="HalvesPerSplit"/> each), so intake never has to wait for the belt to
        /// finish draining one split.</summary>
        public const int DecomposerBuffer = 4;

        /// <summary>
        /// Seconds an item spends inside a pipe crossing one tile. Well under a belt's one second
        /// per item, so a pipe never throttles the line it carries.
        /// </summary>
        public const float PipeTransit = 0.25f;

        /// <summary>How close to the Core's centre an enemy has to be before it stops and hits it.
        /// The Core's own half-extent: enemies stop at its edge, not inside it.</summary>
        public const float CoreAttackRadius = CoreState.Size * 0.5f;

        /// <summary>A projectile bursts this close to its target. Generous, because a miss is
        /// indistinguishable from a bug to the player.</summary>
        public const float ProjectileHitRadius = 0.35f;

        /// <summary>A shot that never lands is destroyed after this long, so a killed target cannot
        /// leave a projectile flying forever.</summary>
        public const float ProjectileMaxLifetime = 6f;

        private static readonly TurretSpec[] TurretSpecs =
        {
            // The one turret: mid-rate, mid-range, eats half-circles. Two shots kill a Spike, so a
            // single fed cannon holds a trickle; a packed wave wants two lines or a split feed.
            new TurretSpec(BuildKind.Cannon, "Cannon", ShapeType.HalfCircle,
                fireInterval: 1f, damage: 3f, range: 7f, projectileSpeed: 14f),
        };

        private static readonly EnemySpec[] EnemySpecs =
        {
            new EnemySpec(EnemyKind.Spike, "Spike",
                hp: 6f, speed: 1.5f, coreDamage: 6f, attackInterval: 1f,
                weakness: ShapeType.HalfCircle),
        };

        /// <summary>Every enemy kind, for UIs that print the roster instead of hardcoding a count.</summary>
        public static readonly EnemyKind[] EnemyKinds = { EnemyKind.Spike };

        /// <summary>
        /// What each building costs, in circles banked at the Core. A belt is cheap per cell but a
        /// long run is a real purchase, which is what makes the map's distances matter; a cannon is
        /// the big spend, so where it stands is a decision. Removal refunds in full: the cost gates
        /// how fast you can expand, not whether you dare to experiment.
        /// </summary>
        public static int Cost(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.Belt: return 1;
                case BuildKind.Drill: return 10;
                case BuildKind.Decomposer: return 15;
                case BuildKind.Pipe: return 6;
                case BuildKind.Splitter: return 10;
                case BuildKind.Cannon: return 20;
                default: throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "not buildable");
            }
        }

        public static TurretSpec Turret(BuildKind kind)
        {
            int index = (int)kind - (int)BuildKind.Cannon;
            if (index < 0 || index >= TurretSpecs.Length)
                throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "not a turret");

            return TurretSpecs[index];
        }

        public static EnemySpec Enemy(EnemyKind kind) => EnemySpecs[(int)kind];
    }
}
