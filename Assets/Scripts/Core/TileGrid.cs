using System;

namespace Facet.Core
{
    /// <summary>
    /// The tile grid: what sits on every tile, plus world&lt;-&gt;tile conversion.
    /// Map space is [0,Width] x [0,Height] world units, so tile (x,y) covers
    /// [x,x+1] x [y,y+1] and its centre is at (x+0.5, y+0.5).
    /// </summary>
    public sealed class TileGrid
    {
        public readonly int Width;
        public readonly int Height;

        private readonly TileKind[] _tiles;

        public TileGrid(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            _tiles = new TileKind[width * height];
        }

        public bool InBounds(Int2 c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

        public TileKind Get(Int2 c) => InBounds(c) ? _tiles[Index(c)] : TileKind.Empty;

        public void Set(Int2 c, TileKind kind)
        {
            if (!InBounds(c)) return;
            _tiles[Index(c)] = kind;
        }

        /// <summary>True when something already occupies the tile.</summary>
        public bool IsOccupied(Int2 c) => Get(c) != TileKind.Empty;

        /// <summary>True when a building may be placed here (in bounds and free).</summary>
        public bool IsBuildable(Int2 c) => InBounds(c) && !IsOccupied(c);

        /// <summary>Centre of a tile in world units.</summary>
        public Vec2 CellCenter(Int2 c) => new Vec2(c.X + 0.5f, c.Y + 0.5f);

        /// <summary>Tile containing a world position. Out-of-bounds results are not clamped.</summary>
        public Int2 CellAt(Vec2 world)
        {
            return new Int2((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));
        }

        private int Index(Int2 c) => c.Y * Width + c.X;
    }
}
