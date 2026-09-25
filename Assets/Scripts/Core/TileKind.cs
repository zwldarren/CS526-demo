namespace Facet.Core
{
    /// <summary>
    /// What occupies a tile. Only Empty and Belt have behaviour today;
    /// drill / cutter / assembler / turret / city get appended here as they land.
    /// </summary>
    public enum TileKind : byte
    {
        Empty = 0,
        Belt = 1,
    }
}
