namespace Facet.Core
{
    /// <summary>
    /// The whole logic layer. Deterministic, engine-free, advanced exactly once per fixed tick
    /// by the host. Nothing here knows about Unity, time.DeltaTime, or frame rates.
    /// </summary>
    public sealed class SimWorld
    {
        public readonly TileGrid TileGrid;
        public readonly SimConfig Config;

        /// <summary>Ticks elapsed since the run started. The clock everything else reads.</summary>
        public int TickCount { get; private set; }

        /// <summary>Paused worlds still accept input but do not advance.</summary>
        public bool Paused;

        public RigState Rig;

        public SimWorld(TileGrid grid, SimConfig config)
        {
            TileGrid = grid;
            Config = config;
            SpawnRigAt(grid.CellCenter(new Int2(grid.Width / 2, grid.Height / 2)));
        }

        public float ElapsedSeconds => TickCount * SimConfig.TickDt;

        public void SpawnRigAt(Vec2 worldPosition)
        {
            Rig.Position = worldPosition;
            Rig.PreviousPosition = worldPosition;
            Rig.Facing = Vec2.Zero;
        }

        /// <summary>Advance one fixed step. Call at exactly SimConfig.TickRate Hz.</summary>
        public void Tick(InputCommand cmd)
        {
            if (Paused) return;

            Rig.PreviousPosition = Rig.Position;
            MoveRig(cmd.Move);

            // Belts, processors, turrets and enemies are ticked here as they land.

            TickCount++;
        }

        private void MoveRig(Vec2 input)
        {
            Vec2 dir = input.Normalized;
            if (dir.SqrMagnitude <= 0f) return;

            Rig.Facing = dir;
            Vec2 next = Rig.Position + dir * (Config.RigSpeed * SimConfig.TickDt);

            // Per-axis clamping also gives free wall-sliding: blocked on one axis, still moves on the other.
            Rig.Position = TileGrid.ClampToBounds(next, Config.RigRadius);
        }
    }
}
