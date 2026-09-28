using System;

namespace Facet.Core
{
    /// <summary>
    /// The two transport machines, stepped once per tick after production.
    ///
    /// **Pipe** is the crossing piece: it eats anything delivered to it, carries the item for
    /// <see cref="Balance.PipeTransit"/> seconds, and drops it on the belt two cells ahead in its
    /// facing - jumping exactly one tile, which may be another belt (a crossing) or bare ground (a
    /// gap). It is transport, not a machine with a diet: every shape is accepted and nothing jams.
    /// A blocked landing holds the item at the far end instead of losing it.
    ///
    /// **Splitter** is the balancer: one hub whose four ports are read off the belts around it,
    /// every tick - a belt pointing into the hub is an input, a belt pointing away from it is an
    /// output, so 1-in-3-out, 3-in-1-out and 2-in-2-out are the same building on different streets,
    /// and re-pointing one belt re-wires the hub without touching it. The hub holds one item at a
    /// time and pushes it round-robin across the outputs (skipping any whose belt is full or
    /// jammed), which makes balanced distribution the default and backpressure - not loss - the
    /// answer to a blocked output.
    /// </summary>
    public sealed class TransitSystem
    {
        /// <summary>Port scan order, fixed so ties are the map's decision, not iteration order's.
        /// Borrowed from <see cref="MachineDelivery"/> so a splitter and a decomposer can never disagree
        /// about which way round their ports go.</summary>
        private static readonly Dir[] Ports = MachineDelivery.Ports;

        private readonly TileGrid _grid;
        private readonly BeltField _belts;
        private readonly MachineField _machines;

        public TransitSystem(TileGrid grid, BeltField belts, MachineField machines)
        {
            _grid = grid;
            _belts = belts;
            _machines = machines;
        }

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
                        case TileKind.Pipe: StepPipe(ref machine, cell, dt); break;
                        case TileKind.Splitter: StepSplitter(ref machine, cell); break;
                    }
                }
            }
        }

        private void StepPipe(ref MachineState pipe, Int2 cell, float dt)
        {
            if (pipe.Carried == ShapeType.None)
            {
                // Empty pipe: swallow whatever is being delivered, from any side.
                if (MachineDelivery.TryFindDelivery(_belts, cell, ShapeType.None, requireShape: false,
                        out Int2 feed, out ShapeType shape))
                {
                    _belts.TryTakeItem(feed, out _);
                    pipe.Carried = shape;
                    pipe.Cooldown = Balance.PipeTransit;
                }
                return;
            }

            if (pipe.Cooldown > 0f)
            {
                pipe.Cooldown = MathF.Max(0f, pipe.Cooldown - dt);
                if (pipe.Cooldown > 0f) return;   // still crossing
            }

            // Land on the belt two cells ahead. No belt or no room: the item waits at the far end.
            if (_belts.TrySpawnItem(cell + pipe.Direction.Offset() * 2, pipe.Carried))
                pipe.Carried = ShapeType.None;
        }

        private void StepSplitter(ref MachineState splitter, Int2 cell)
        {
            // Push first, so a hub that empties can take again in the same tick. Round-robin across
            // the outlets - the shared rule every machine with ports uses, so a splitter and a
            // decomposer distribute the same way.
            if (splitter.Carried != ShapeType.None &&
                MachineDelivery.TryPushOut(_belts, ref splitter, cell, splitter.Carried))
                splitter.Carried = ShapeType.None;

            if (splitter.Carried != ShapeType.None) return;

            for (int i = 0; i < Ports.Length; i++)
            {
                Int2 source = cell + Ports[i].Offset();
                if (!_belts.TryGet(source, out BeltState belt)) continue;
                if (belt.Direction != Ports[i].Opposite()) continue;   // must point into the hub
                if (belt.Jammed || belt.Item == ShapeType.None || belt.Progress < 1f) continue;

                _belts.TryTakeItem(source, out ShapeType shape);
                splitter.Carried = shape;
                return;
            }
        }
    }
}
