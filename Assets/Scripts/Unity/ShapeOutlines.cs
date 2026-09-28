using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The outline polygons one view keeps cached, indexed by shape so callers stop switching on
    /// shape themselves.
    ///
    /// Every view draws the same shapes at a size it picked itself, so each one still builds its
    /// own geometry - only the shape-to-slot mapping is shared. A new shape is then a slot added
    /// here, next to <see cref="Palette.ShapeColor"/>, not a new switch in every view that draws icons.
    /// </summary>
    internal readonly struct ShapeOutlines
    {
        private readonly Vector2[] _circle;
        private readonly Vector2[] _halfCircle;

        public ShapeOutlines(Vector2[] circle, Vector2[] halfCircle)
        {
            _circle = circle;
            _halfCircle = halfCircle;
        }

        /// <summary>
        /// The silhouettes at one radius: a round circle and its dome-up half. The half-disc's
        /// orientation is fixed here so a weakness icon, the decomposer's output and the ammo riding
        /// a belt all read as the same shape across every view.
        /// </summary>
        public static ShapeOutlines AtRadius(float radius, int circleSides)
        {
            return new ShapeOutlines(
                ProcMesh.RegularPolygon(circleSides, radius, 0f),
                ProcMesh.HalfDisc(circleSides / 2, radius));
        }

        /// <summary>Outline for <paramref name="shape"/>, or null when there is nothing to draw.</summary>
        public Vector2[] this[ShapeType shape]
        {
            get
            {
                switch (shape)
                {
                    case ShapeType.Circle: return _circle;
                    case ShapeType.HalfCircle: return _halfCircle;
                    default: return null;
                }
            }
        }
    }
}
