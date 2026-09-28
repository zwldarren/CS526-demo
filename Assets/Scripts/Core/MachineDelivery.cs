namespace Facet.Core
{
    /// <summary>
    /// The one rule that connects belts to machines, in one place: **a machine eats from every belt
    /// cell that delivers into it, and from nothing else.**
    ///
    /// A belt delivers into a machine when it points at it - the belt's direction is the step from its
    /// own cell to the machine's - and when its item has reached the exit edge of its cell (progress 1),
    /// which is the same boundary a belt-to-belt hand-off happens on. Anything else feeding a machine
    /// is not a delivery: a belt pointing the other way is a dead end and simply queues, which is a
    /// visible mistake instead of a silent one.
    ///
    /// Reading from any of the four sides is what makes "expand supply" a layout answer: two belts
    /// converging on one cannon are how it stays at its 1 shot/s ceiling while lines cross and
    /// share, and a jam anywhere upstream of those belts silences it without touching the turret itself.
    /// </summary>
    internal static class MachineDelivery
    {
        /// <summary>
        /// Fixed scan order, so which of several ready items a machine takes is decided by the map and
        /// not by iteration order. North, East, South, West.
        ///
        /// The same order doubles as the round-robin order a machine pushes along, which is why it is
        /// public: every machine with ports - the splitter and the decomposer - must agree on it, or
        /// two of them would disagree about which way round their own outlets go.
        /// </summary>
        public static readonly Dir[] Ports = { Dir.North, Dir.East, Dir.South, Dir.West };

        /// <summary>
        /// First cell in scan order delivering a ready item into <paramref name="cell"/>.
        /// With <paramref name="requireShape"/> the item must be exactly <paramref name="wanted"/>.
        /// </summary>
        public static bool TryFindDelivery(BeltField belts, Int2 cell, ShapeType wanted, bool requireShape,
            out Int2 deliveryCell, out ShapeType shape)
        {
            deliveryCell = default;
            shape = ShapeType.None;

            for (int i = 0; i < Ports.Length; i++)
            {
                if (!TryReadyAt(belts, cell, Ports[i], out Int2 neighbour, out BeltState state)) continue;
                if (requireShape && state.Item != wanted) continue;

                deliveryCell = neighbour;
                shape = state.Item;
                return true;
            }

            return false;
        }

        /// <summary>
        /// First cell delivering an item of the *wrong* shape: the delivery that jams. Reported
        /// separately from a good delivery because a wrong shape always jams, even when the turret is
        /// busy cooling down - the doc's rule is about the delivery, not about the shot.
        /// </summary>
        public static bool TryFindWrongDelivery(BeltField belts, Int2 cell, ShapeType ammo, out Int2 deliveryCell)
        {
            deliveryCell = default;

            for (int i = 0; i < Ports.Length; i++)
            {
                if (!TryReadyAt(belts, cell, Ports[i], out Int2 neighbour, out BeltState state)) continue;
                if (state.Item == ammo) continue;

                deliveryCell = neighbour;
                return true;
            }

            return false;
        }

        /// <summary>
        /// An outlet: a belt on this side of the machine pointing *away* from it. Whether that belt
        /// has room is <see cref="BeltField.TrySpawnItem"/>'s question, asked again per push.
        ///
        /// Ports are read off the belts rather than stored on the machine, which is what makes a
        /// decomposer's two outlets the same kind of thing as a splitter's four: re-pointing one belt
        /// re-wires the machine without touching it, and a machine with no belt pointing away simply
        /// waits instead of pushing into a dead end.
        /// </summary>
        public static bool IsOutput(BeltField belts, Int2 cell, Dir side)
            => belts.TryGet(cell + side.Offset(), out BeltState belt) && belt.Direction == side;

        /// <summary>
        /// Push <paramref name="shape"/> out through the first outlet with room, starting at the
        /// machine's rotation cursor and wrapping, then advance that cursor past the outlet used.
        /// Shared by the decomposer and the splitter so both distribute round-robin: the two halves of
        /// one split should leave by different belts when two are available, not queue behind one.
        /// </summary>
        public static bool TryPushOut(BeltField belts, ref MachineState machine, Int2 cell, ShapeType shape)
        {
            for (int k = 0; k < Ports.Length; k++)
            {
                int port = (machine.Rotation + k) & 3;   // Ports.Length is 4
                Dir side = Ports[port];
                if (!IsOutput(belts, cell, side)) continue;
                if (!belts.TrySpawnItem(cell + side.Offset(), shape)) continue;

                machine.Rotation = (port + 1) & 3;   // the next item starts past this outlet
                return true;
            }

            return false;
        }

        /// <summary>Is there a belt on the <paramref name="side"/> of <paramref name="cell"/>, pointing
        /// into it, with an item parked on its exit edge?</summary>
        private static bool TryReadyAt(BeltField belts, Int2 cell, Dir side, out Int2 neighbour, out BeltState state)
        {
            neighbour = cell + side.Offset();
            if (!belts.TryGet(neighbour, out state)) return false;
            if (state.Direction != side.Opposite()) return false;   // it must point at the machine
            return state.Item != ShapeType.None && state.Progress >= 1f;
        }
    }
}
