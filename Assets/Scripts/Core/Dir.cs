namespace Facet.Core
{
    /// <summary>Cardinal direction on the tile grid.</summary>
    public enum Dir
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    public static class DirExtensions
    {
        /// <summary>Tile step for this direction.</summary>
        public static Int2 Offset(this Dir d)
        {
            switch (d)
            {
                case Dir.North: return new Int2(0, 1);
                case Dir.East: return new Int2(1, 0);
                case Dir.South: return new Int2(0, -1);
                default: return new Int2(-1, 0);
            }
        }

        public static Vec2 ToVec(this Dir d)
        {
            Int2 o = d.Offset();
            return new Vec2(o.X, o.Y);
        }

        public static Dir Opposite(this Dir d) => (Dir)(((int)d + 2) & 3);

        /// <summary>90 degrees clockwise when viewed on screen (Y up).</summary>
        public static Dir RotateCw(this Dir d) => (Dir)(((int)d + 1) & 3);

        public static Dir RotateCcw(this Dir d) => (Dir)(((int)d + 3) & 3);

        /// <summary>True when the direction runs along the X axis (East/West).</summary>
        public static bool IsHorizontal(this Dir d) => d == Dir.East || d == Dir.West;
    }
}
