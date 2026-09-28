using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>One shape patch, flattened for the view layer.</summary>
    public struct ShapePatchSnapshot
    {
        public Int2 Cell;
        public ShapeType Shape;
    }

    /// <summary>
    /// The shape patches in the ground: terrain, so it is stamped once by <see cref="Maps"/> and
    /// never moves during a run. A tile is a patch independently of <see cref="TileGrid"/>'s kind
    /// (which a drill overwrites while it stands there), so this is what tells the drill what it is
    /// mining and what to restore when the drill is removed.
    /// </summary>
    public sealed class ShapePatchField
    {
        private readonly TileGrid _grid;
        private readonly ShapeType[] _shape;

        public ShapePatchField(TileGrid grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _shape = new ShapeType[grid.Width * grid.Height];
        }

        public bool Has(Int2 cell) => _grid.InBounds(cell) && _shape[_grid.Index(cell)].IsShape();

        public ShapeType ShapeAt(Int2 cell) => _grid.InBounds(cell) ? _shape[_grid.Index(cell)] : ShapeType.None;

        /// <summary>Stamp a patch tile: records the shape and marks the grid kind.</summary>
        public void Set(Int2 cell, ShapeType shape)
        {
            if (!_grid.InBounds(cell)) return;
            _shape[_grid.Index(cell)] = shape;
            _grid.Set(cell, TileKind.ShapePatch);
        }

        public void Clear()
        {
            Array.Clear(_shape, 0, _shape.Length);
        }

        /// <summary>
        /// Put the terrain back after a building is removed: a patch if the drill was standing on one,
        /// bare ground otherwise.
        /// </summary>
        public void RestoreTerrain(Int2 cell)
        {
            if (!_grid.InBounds(cell)) return;
            _grid.Set(cell, Has(cell) ? TileKind.ShapePatch : TileKind.Empty);
        }

        /// <summary>Every patch, in stable linear-index order.</summary>
        public void GetPatches(List<ShapePatchSnapshot> into)
        {
            into.Clear();
            for (int i = 0; i < _shape.Length; i++)
            {
                if (!_shape[i].IsShape()) continue;
                into.Add(new ShapePatchSnapshot { Cell = _grid.CellOf(i), Shape = _shape[i] });
            }
        }
    }
}
