using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws the map's ground and its tile grid as one mesh of thin quads: the whole footprint filled
    /// with <see cref="Palette.Platform"/>, then the lines on top of it. That is what separates the
    /// buildable world from the void the camera sits in.
    ///
    /// The grid only ever changes when the zoom moves the screen-pixel size, which is exactly the
    /// rebuild reason the gate owns: the line width is a screen-space constant, so the mesh is
    /// rebuilt when the pixel size moves and not otherwise. Without it the lines are only as wide as
    /// the zoom they happened to be built at, and vanish into sub-pixel aliasing at the far end of
    /// the camera's range.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class GridRenderer : MeshView
    {
        /// <summary>Floor for the line's half-width, so a degenerate quad can never reach the mesh.</summary>
        private const float MinHalfWidth = 0.0005f;

        private TileGrid _grid;

        protected override Palette.Layer Layer => Colors.GridLayer;

        protected override void OnInitialized()
        {
            _grid = World.TileGrid;
        }

        /// <summary>The ground follows the map, and so do its lines.</summary>
        protected override void OnWorldRebound() => _grid = World.TileGrid;

        protected override void AppendFrame(in ViewFrame frame)
        {
            float half = Mathf.Max(Colors.GridLinePixels * frame.WorldPerPixel * 0.5f, MinHalfWidth);
            float width = _grid.Width;
            float height = _grid.Height;

            // The ground goes in first: one draw call, no depth writes, so within this mesh the
            // lines that follow simply blend over it.
            AppendQuad(new Vector2(0f, 0f), new Vector2(width, 0f),
                new Vector2(width, height), new Vector2(0f, height), Colors.Platform);

            for (int x = 0; x <= _grid.Width; x++)
            {
                AppendQuad(new Vector2(x - half, 0f), new Vector2(x + half, 0f),
                    new Vector2(x + half, height), new Vector2(x - half, height), Colors.GridLine);
            }

            for (int y = 0; y <= _grid.Height; y++)
            {
                AppendQuad(new Vector2(0f, y - half), new Vector2(width, y - half),
                    new Vector2(width, y + half), new Vector2(0f, y + half), Colors.GridLine);
            }
        }
    }
}
