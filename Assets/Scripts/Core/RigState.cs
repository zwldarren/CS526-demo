namespace Facet.Core
{
    /// <summary>
    /// The player's hands in the world. Sim-owned state; the view only reads it.
    /// PreviousPosition is kept so the renderer can interpolate between ticks.
    /// </summary>
    public struct RigState
    {
        /// <summary>Position after the most recent tick.</summary>
        public Vec2 Position;

        /// <summary>Position before the most recent tick.</summary>
        public Vec2 PreviousPosition;

        /// <summary>Last non-zero movement direction. Zero until the Rig first moves.</summary>
        public Vec2 Facing;
    }
}
