namespace Facet.Core
{
    /// <summary>
    /// The global rules that are not tied to one piece of content: how close an enemy must be to hit
    /// the Core, how forgiving a projectile's hit test is, how long a stray shot lives.
    ///
    /// Everything else that used to live in this file - build costs, turret and enemy stats, the
    /// machine table, the decomposer recipe - now lives in <see cref="ContentDatabase"/>, the single
    /// source of truth the simulation, the view and the Editor asset all read. The methods below are a
    /// facade over the shipped table (<see cref="ContentDatabase.Default"/>) for the call sites that
    /// predate it; new code should take a <see cref="ContentDatabase"/> and read it directly, which is
    /// what lets a test or a replay run against a different, still-reproducible table.
    /// </summary>
    public static class Balance
    {
        /// <summary>How close to the Core's centre an enemy has to be before it stops and hits it.
        /// The Core's own half-extent: enemies stop at its edge, not inside it.</summary>
        public static readonly float CoreAttackRadius = CoreState.Size * 0.5f;

        /// <summary>A projectile bursts this close to its target. Generous, because a miss is
        /// indistinguishable from a bug to the player.</summary>
        public static readonly float ProjectileHitRadius = 0.35f;

        /// <summary>A shot that never lands is destroyed after this long, so a killed target cannot
        /// leave a projectile flying forever.</summary>
        public static readonly float ProjectileMaxLifetime = 6f;

        private static ContentDatabase Content => ContentDatabase.Default;

        private static RecipeDef DecomposeRecipe => Content.Recipe(ContentDatabase.DecomposeRecipeId);

        /// <summary>Every enemy kind, for UIs that print the roster instead of hardcoding a count.</summary>
        public static EnemyKind[] EnemyKinds => Content.EnemyKinds;

        /// <summary>Seconds per mined item, from the drill definition. Matched to one belt, so a drill
        /// exactly saturates a line: one item every 30 ticks at 30 Hz, never 31.</summary>
        public static float DrillInterval => Content.Machine(BuildKind.Drill).Interval;

        /// <summary>Seconds an item spends inside a pipe crossing one tile.</summary>
        public static float PipeTransit => Content.Machine(BuildKind.Pipe).Interval;

        /// <summary>Seconds to split one circle into two half-circles, from the decomposer recipe.</summary>
        public static float DecomposeInterval => DecomposeRecipe.Interval;

        /// <summary>Half-circles one split yields.</summary>
        public static int HalvesPerSplit => DecomposeRecipe.OutputCount;

        /// <summary>Half-circles a decomposer can hold waiting for its output belt.</summary>
        public static int DecomposerBuffer => DecomposeRecipe.Buffer;

        /// <summary>
        /// What each building costs, in circles banked at the Core. Removal refunds in full, so the
        /// cost gates how fast you can expand, not whether you dare to experiment.
        /// </summary>
        public static int Cost(BuildKind kind) => Content.Machine(kind).Cost;

        /// <summary>One turret's numbers. Throws for a kind that is not a turret - there is no
        /// "default is the turret" convention left to get wrong.</summary>
        public static TurretDef Turret(BuildKind kind) => Content.Turret(kind);

        public static EnemyDef Enemy(EnemyKind kind) => Content.Enemy(kind);
    }
}
