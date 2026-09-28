namespace Facet.Core
{
    /// <summary>
    /// Everything the player can put on the map, in hotkey order (1..6): the production chain the
    /// design names (drill, decomposer, cannon) and the transport pieces (belt, pipe, splitter).
    /// The ordinal is the control that selects it and the field the HUD reads, so the order is
    /// part of the interface.
    /// </summary>
    public enum BuildKind : byte
    {
        Belt = 0,
        Drill = 1,
        Decomposer = 2,
        Pipe = 3,
        Splitter = 4,
        Cannon = 5,
    }

    public static class BuildCatalog
    {
        public const int Count = 6;

        public static readonly BuildKind[] All =
        {
            BuildKind.Belt,
            BuildKind.Drill,
            BuildKind.Decomposer,
            BuildKind.Pipe,
            BuildKind.Splitter,
            BuildKind.Cannon,
        };

        /// <summary>What a placement of this kind puts on the tile.</summary>
        public static TileKind TileFor(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.Belt: return TileKind.Belt;
                case BuildKind.Drill: return TileKind.Drill;
                case BuildKind.Decomposer: return TileKind.Decomposer;
                case BuildKind.Pipe: return TileKind.Pipe;
                case BuildKind.Splitter: return TileKind.Splitter;
                default: return TileKind.Turret;
            }
        }

        public static bool IsTurret(BuildKind kind) => kind == BuildKind.Cannon;
    }
}
