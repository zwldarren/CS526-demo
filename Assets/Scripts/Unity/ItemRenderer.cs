using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every item riding a belt as one mesh, rebuilt each frame because items are always
    /// moving. Positions are interpolated between the last two simulation ticks with the frame's
    /// Alpha, so transport is smooth at any frame rate instead of stepping at 30 Hz.
    ///
    /// Unlike the belts, items do carry an outline, and the shape is the one thing on screen the
    /// player has to identify at a glance. The view rebuilds every frame anyway, so the outline
    /// width is recomputed from the current zoom each time and stays a true 2 px.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class ItemRenderer : MeshView
    {
        private readonly List<ItemSnapshot> _items = new List<ItemSnapshot>();
        private ShapeOutlines _outlines;

        protected override Palette.Layer Layer => Colors.ItemLayer;

        protected override void OnInitialized()
        {
            _outlines = ShapeOutlines.AtRadius(Colors.Items.Radius, Colors.CircleSides);
        }

        /// <summary>Items are always moving, so this view rebuilds every frame.</summary>
        protected override void Observe(in ViewFrame frame) => MarkDirty();

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Belts.GetItems(_items);
            float outlineWidth = OutlineWidth;

            for (int i = 0; i < _items.Count; i++)
            {
                ItemSnapshot item = _items[i];
                Vector2[] points = _outlines[item.Shape];
                if (points == null) continue;

                Vec2 p = Vec2.Lerp(item.PreviousPosition, item.Position, frame.Alpha);
                AppendPolygon(points, new Vector2(p.X, p.Y),
                    Colors.ShapeColor(item.Shape), Colors.Outline, outlineWidth);
            }
        }
    }
}
