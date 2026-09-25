namespace Facet.Core
{
    /// <summary>Everything an input source may ask the simulation to do in one tick.</summary>
    public readonly struct InputCommand
    {
        /// <summary>Desired move direction, magnitude 0..1. Not normalised by the caller.</summary>
        public readonly Vec2 Move;

        /// <summary>Left mouse button held - start / continue dragging a belt.</summary>
        public readonly bool BuildHeld;

        /// <summary>Right mouse button held - remove / rotate.</summary>
        public readonly bool RemoveHeld;

        /// <summary>Tile under the mouse cursor.</summary>
        public readonly Int2 CursorCell;

        public InputCommand(Vec2 move, bool buildHeld, bool removeHeld, Int2 cursorCell)
        {
            Move = move;
            BuildHeld = buildHeld;
            RemoveHeld = removeHeld;
            CursorCell = cursorCell;
        }

        public static readonly InputCommand None = new InputCommand(Vec2.Zero, false, false, Int2.Zero);
    }
}
