namespace Facet.Core
{
    /// <summary>
    /// Everything an input source may ask the simulation to do in one tick.
    /// Note there is no movement field: the player has no avatar in the world,
    /// the camera pans freely and only the cursor interacts with the simulation.
    /// </summary>
    public readonly struct InputCommand
    {
        /// <summary>Left mouse button held - starts / continues dragging a belt run.</summary>
        public readonly bool BuildHeld;

        /// <summary>Right mouse button held - removes whatever is under the cursor.</summary>
        public readonly bool RemoveHeld;

        /// <summary>Tile under the mouse cursor. (-1,-1) when there is no cursor.</summary>
        public readonly Int2 CursorCell;

        public InputCommand(bool buildHeld, bool removeHeld, Int2 cursorCell)
        {
            BuildHeld = buildHeld;
            RemoveHeld = removeHeld;
            CursorCell = cursorCell;
        }

        public static readonly InputCommand None = new InputCommand(false, false, new Int2(-1, -1));
    }
}
