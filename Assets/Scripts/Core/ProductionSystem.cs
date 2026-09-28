using System;

namespace Facet.Core
{
    /// <summary>
    /// What drills and decomposers do every tick: the production half of the chain, and the only
    /// place shapes enter the world.
    ///
    /// The drill is deliberately matched to one belt (one circle per second). The decomposer doubles
    /// what it is fed - one circle in, two halves out per 1.5 s split, which is 1.33 halves/s, more
    /// than a single belt can carry away - so a decomposer at full tilt is the first building that asks
    /// for a second outlet: point belts away from two of its sides and it runs at its own rate, or feed
    /// a splitter to share the halves between two cannons. Its outlets are read off the belts around it,
    /// exactly like a splitter's, so which sides are outputs is a layout decision and not a facing.
    /// Both machines wait rather than jam when their output is blocked - waiting is the honest signal
    /// for "this stage is the bottleneck". The jam rule still holds at the decomposer's input: anything
    /// delivered that is not a circle blocks the feeding belt.
    /// </summary>
    public sealed class ProductionSystem
    {
        private readonly TileGrid _grid;
        private readonly ShapePatchField _patches;
        private readonly BeltField _belts;
        private readonly MachineField _machines;

        public ProductionSystem(TileGrid grid, ShapePatchField patches, BeltField belts, MachineField machines)
        {
            _grid = grid;
            _patches = patches;
            _belts = belts;
            _machines = machines;
        }

        /// <summary>Walk the map in linear index order, which is the simulation's only iteration order.</summary>
        public void Step(float dt)
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    var cell = new Int2(x, y);
                    ref MachineState machine = ref _machines.RefAt(cell);

                    switch (machine.Kind)
                    {
                        case TileKind.Drill: StepDrill(ref machine, cell, dt); break;
                        case TileKind.Decomposer: StepDecomposer(ref machine, cell, dt); break;
                    }
                }
            }
        }

        private void StepDrill(ref MachineState drill, Int2 cell, float dt)
        {
            if (!Tick(ref drill.Cooldown, dt)) return;

            ShapeType mined = _patches.ShapeAt(cell);
            if (!mined.IsShape()) return;

            // TrySpawnItem is also the "is that cell a belt with room" test: a drill facing anything
            // else, or onto a full cell, simply waits with its cooldown expired.
            if (_belts.TrySpawnItem(cell + drill.Direction.Offset(), mined))
                drill.Cooldown = Balance.DrillInterval;
        }

        /// <summary>
        /// One circle in, two halves out through whichever sides have a belt pointing away, 1.5 s per
        /// split. The split itself is unchanged; what the player changes is how many outlets are wired.
        ///
        /// The halves sit in an output buffer two splits deep (<see cref="Balance.DecomposerBuffer"/>)
        /// and leave one per tick while an outlet has room, so the split is never lost to a blocked
        /// output - the machine waits *after* the work, not in the middle of it. Intake starts
        /// whenever a split's worth of buffer is free, which is what lets a drained decomposer
        /// saturate its output belts despite splitting slower than they run.
        /// </summary>
        private void StepDecomposer(ref MachineState machine, Int2 cell, float dt)
        {
            // The twist holds at this input too: a half-circle (or anything not a circle) delivered
            // here jams the feeding belt, whether or not the machine is busy.
            if (MachineDelivery.TryFindWrongDelivery(_belts, cell, ShapeType.Circle, out Int2 wrong))
                _belts.TryJam(wrong);

            // Push buffered halves out through any wired outlet. One per tick is far above what belts
            // can carry, so the belts - not this line - set the real output rate; with two outlets the
            // halves of one split leave by different belts.
            if (machine.OutputCount > 0 &&
                MachineDelivery.TryPushOut(_belts, ref machine, cell, ShapeType.HalfCircle))
                machine.OutputCount--;

            if (machine.Carried == ShapeType.Circle)
            {
                machine.Cooldown -= dt;
                if (machine.Cooldown <= 0f)
                {
                    machine.Carried = ShapeType.None;
                    machine.OutputCount += Balance.HalvesPerSplit;
                }
                return;
            }

            // Intake only when one more split fits in the buffer, so a split never has nowhere to go.
            if (machine.OutputCount > Balance.DecomposerBuffer - Balance.HalvesPerSplit) return;

            if (MachineDelivery.TryFindDelivery(_belts, cell, ShapeType.Circle, requireShape: true,
                    out Int2 delivery, out _))
            {
                _belts.TryTakeItem(delivery, out _);
                machine.Carried = ShapeType.Circle;
                machine.Cooldown = Balance.DecomposeInterval;
            }
        }

        /// <summary>
        /// Count a cooldown down and report whether the machine may act this tick. The decrement comes
        /// first, so an interval of exactly one second is exactly 30 ticks at 30 Hz: testing the
        /// cooldown *before* decrementing it spends the tick it reaches zero doing nothing and mines at
        /// 31 ticks per item instead. The decomposer and the turret already decrement in this order.
        /// </summary>
        private static bool Tick(ref float cooldown, float dt)
        {
            cooldown = MathF.Max(0f, cooldown - dt);
            return cooldown <= 0f;
        }
    }
}
