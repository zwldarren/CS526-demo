using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws the shape patches in the ground, one mesh for all of them: a bed quad plus the shape's
    /// own silhouette at each patch tile. This is the player's map of where each resource lives
    /// before any drill is placed, so the icon uses the same geometry and colour as the belt items it
    /// will become - recognition should not have to be learned twice.
    ///
    /// The patches are terrain, stamped once from the map's data and never moving during a run,
    /// and a restart re-stamps the identical layout - so this view has no rebuild reason of its own.
    /// The only thing that changes it is the one reason the gate owns: the icon carries an outline, a
    /// screen-space constant, so a zoom rebuilds it and re-stamps the bed at no extra cost.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ShapePatchRenderer : MeshView
    {
        private readonly List<ShapePatchSnapshot> _patches = new List<ShapePatchSnapshot>();
        private ShapeOutlines _outlines;

        protected override Palette.Layer Layer => Colors.ShapePatchLayer;

        protected override void OnInitialized()
        {
            _outlines = ShapeOutlines.AtRadius(Colors.Patches.IconRadius, Colors.CircleSides);
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Patches.GetPatches(_patches);
            float outlineWidth = OutlineWidth;

            for (int i = 0; i < _patches.Count; i++)
            {
                ShapePatchSnapshot patch = _patches[i];
                Vector2[] points = _outlines[patch.Shape];
                if (points == null) continue;

                // Tile coordinates ARE world coordinates: (x,y) spans [x,x+1]x[y,y+1].
                float x = patch.Cell.X;
                float y = patch.Cell.Y;
                float bedInset = Colors.Patches.BedInset;
                AppendQuad(
                    new Vector2(x + bedInset, y + bedInset),
                    new Vector2(x + 1f - bedInset, y + bedInset),
                    new Vector2(x + 1f - bedInset, y + 1f - bedInset),
                    new Vector2(x + bedInset, y + 1f - bedInset),
                    Colors.ShapePatchBed);

                AppendPolygon(points, new Vector2(x + 0.5f, y + 0.5f),
                    Colors.ShapeColor(patch.Shape), Colors.Outline, outlineWidth);
            }
        }
    }
}
