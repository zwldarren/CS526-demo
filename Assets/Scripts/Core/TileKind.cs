namespace Facet.Core
{
    /// <summary>
    /// What occupies a tile. Only Empty, Belt and Core have behaviour today;
    /// drill / cutter / turret get appended here as they land.
    /// </summary>
    public enum TileKind : byte
    {
        Empty = 0,
        Belt = 1,

        /// <summary>The defended object. Not buildable, not walkable, its HP is the fail state.</summary>
        Core = 2,
    }
}
