using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The ghost that shows where the next placement would land, whether it would be allowed there, and
    /// what it would be: the belt's chevron, a machine's body and facing, and a turret's range ring.
    /// Green means buildable, amber means the stockpile cannot cover it, red means something is
    /// already there (or, for a drill, that there is no shape patch under it) - which is the feedback
    /// that makes dragging a run across an existing build predictable rather than surprising.
    ///
    /// The range ring is the one thing on screen that makes turret placement a decision instead of a
    /// guess: range is the turret's whole job, and a player who cannot see it is placing blind.
    ///
    /// Its rebuild reasons are its own - the cursor cell, whether the placement is valid and
    /// affordable, and the selected building and facing - plus the gate's zoom reason, because the
    /// border and the ring are screen-space constants. The stockpile moving also dirties it: a cell
    /// the player cannot afford turns green the tick a delivery banks.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class CursorView : MeshView
    {
        private Int2 _seenCell = new Int2(int.MinValue, int.MinValue);
        private bool _seenValid;
        private bool _seenAffordable;
        private bool _seenVisible;
        private BuildKind _seenKind;
        private Dir _seenDirection;
        private int _seenCircles = -1;

        protected override Palette.Layer Layer => Colors.CursorLayer;

        protected override void Observe(in ViewFrame frame)
        {
            Int2 cell = frame.CursorCell;
            BuildKind kind = World.SelectedKind;
            Dir direction = World.PlacementDirection;
            int circles = World.Economy.Circles;

            bool visible = World.TileGrid.InBounds(cell);
            bool valid = visible && World.CanPlace(kind, cell);
            bool affordable = World.Economy.CanAfford(kind);

            if (visible == _seenVisible && cell == _seenCell && valid == _seenValid &&
                affordable == _seenAffordable && circles == _seenCircles &&
                kind == _seenKind && direction == _seenDirection) return;

            _seenCell = cell;
            _seenValid = valid;
            _seenAffordable = affordable;
            _seenVisible = visible;
            _seenKind = kind;
            _seenDirection = direction;
            _seenCircles = circles;
            MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            if (!_seenVisible) return;

            Palette.CursorLook look = Colors.Cursor;
            var x = (float)_seenCell.X;
            var y = (float)_seenCell.Y;
            Color edge;
            if (!_seenValid) edge = Colors.CursorBlocked;
            else if (_seenAffordable) edge = Colors.CursorOk;
            else edge = Colors.CursorNoFunds;
            Color fill = edge;
            fill.a = look.FillAlpha;
            edge.a = look.EdgeAlpha;

            AppendQuad(
                new Vector2(x, y), new Vector2(x + 1f, y),
                new Vector2(x + 1f, y + 1f), new Vector2(x, y + 1f), fill);

            float t = Mathf.Min(Colors.CursorEdgePixels * frame.WorldPerPixel, look.MaxEdgeThickness);
            AppendBorder(new Vector2(x, y), 1f, t, 0f, edge);

            if (_seenKind == BuildKind.Belt)
            {
                AppendChevron(new Vector2(x + 0.5f, y + 0.5f), _seenDirection,
                    look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, Colors.BeltArrow);
            }
            else
            {
                // A machine is a single tile with a facing, so the ghost is its footprint plus the
                // arrow it will be built facing - except the splitter and the decomposer, which have
                // no facing: their ports are read off the belts around them, so an arrow would
                // promise what Q/E cannot turn.
                Color body = edge;
                body.a = look.BodyAlpha;
                float bodyInset = look.BodyInset;
                AppendQuad(
                    new Vector2(x + bodyInset, y + bodyInset), new Vector2(x + 1f - bodyInset, y + bodyInset),
                    new Vector2(x + 1f - bodyInset, y + 1f - bodyInset), new Vector2(x + bodyInset, y + 1f - bodyInset), body);

                if (_seenKind != BuildKind.Splitter && _seenKind != BuildKind.Decomposer)
                    AppendChevron(new Vector2(x + 0.5f, y + 0.5f), _seenDirection,
                        look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, edge);
            }

            if (BuildCatalog.IsTurret(_seenKind)) AppendRangeRing(new Vector2(x + 0.5f, y + 0.5f));
        }

        /// <summary>An inset border around a cell, as four quads.</summary>
        private void AppendBorder(Vector2 origin, float size, float thickness, float inset, Color color)
        {
            float a = inset;
            float b = size - inset;
            float inner = b - thickness;

            AppendQuad(
                new Vector2(origin.x + a, origin.y + a), new Vector2(origin.x + b, origin.y + a),
                new Vector2(origin.x + b, origin.y + a + thickness), new Vector2(origin.x + a, origin.y + a + thickness), color);
            AppendQuad(
                new Vector2(origin.x + a, origin.y + inner), new Vector2(origin.x + b, origin.y + inner),
                new Vector2(origin.x + b, origin.y + b), new Vector2(origin.x + a, origin.y + b), color);
            AppendQuad(
                new Vector2(origin.x + a, origin.y + a), new Vector2(origin.x + a + thickness, origin.y + a),
                new Vector2(origin.x + a + thickness, origin.y + b), new Vector2(origin.x + a, origin.y + b), color);
            AppendQuad(
                new Vector2(origin.x + inner, origin.y + a), new Vector2(origin.x + b, origin.y + a),
                new Vector2(origin.x + b, origin.y + b), new Vector2(origin.x + inner, origin.y + b), color);
        }

        /// <summary>
        /// The ring every turret is placed for. It is a thin annulus built from quads rather than a
        /// scaled outline, so its width stays a constant number of screen pixels at any zoom.
        /// </summary>
        private void AppendRangeRing(Vector2 centre)
        {
            float radius = Balance.Turret(_seenKind).Range;
            float half = Mathf.Max(Colors.GridLinePixels * 2f * WorldPerPixel, 0.012f) * 0.5f;
            Color color = _seenValid ? Colors.RangeRing : Colors.CursorBlocked;

            int segments = Colors.Cursor.RingSegments;
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.PI * 2f * i / segments;
                float a1 = Mathf.PI * 2f * (i + 1) / segments;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                AppendQuad(
                    centre + d0 * (radius - half), centre + d0 * (radius + half),
                    centre + d1 * (radius + half), centre + d1 * (radius - half), color);
            }
        }
    }
}
