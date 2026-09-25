using System;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Every colour FACET draws, in one place. The design doc's art rule is
    /// "colour says which side it is on, polygon count says how damaged it is" -
    /// so this is the single source of truth for the first half of that rule.
    /// </summary>
    [Serializable]
    public sealed class Palette
    {
        [Tooltip("Camera clear colour.")]
        public Color Background = new Color(0.07f, 0.08f, 0.11f, 1f);

        [Tooltip("Tile grid lines, drawn faintly under everything.")]
        public Color GridLine = new Color(1f, 1f, 1f, 0.06f);

        [Tooltip("Outline used by every polygon.")]
        public Color Outline = new Color(0.05f, 0.06f, 0.09f, 1f);

        [Tooltip("Outline thickness in screen pixels (design doc: 2 px).")]
        public float OutlinePixels = 2f;

        [Tooltip("Belt bed.")]
        public Color Belt = new Color(0.20f, 0.22f, 0.28f, 1f);

        [Tooltip("Belt direction chevron. Reads as 'this way'.")]
        public Color BeltArrow = new Color(0.45f, 0.50f, 0.60f, 1f);

        [Tooltip("A jammed belt segment - the twist's failure state, so it has to shout.")]
        public Color Jam = new Color(0.87f, 0.26f, 0.26f, 1f);

        [Tooltip("Triangle ammo - Tri-AA's diet.")]
        public Color TriangleShape = new Color(1f, 0.78f, 0.28f, 1f);

        [Tooltip("Square ammo - the Block Cannon's diet.")]
        public Color SquareShape = new Color(0.36f, 0.76f, 1f, 1f);

        [Tooltip("Circle ammo - the Ring Mortar's diet, and the splash answer to clumped Spikes.")]
        public Color CircleShape = new Color(0.72f, 0.55f, 1f, 1f);

        [Tooltip("The defended Core.")]
        public Color Core = new Color(0.95f, 0.96f, 1f, 1f);

        [Tooltip("What the Core tints toward as it takes damage.")]
        public Color CoreDamage = new Color(0.87f, 0.26f, 0.26f, 1f);

        [Tooltip("Ghost preview where the cursor may build.")]
        public Color CursorOk = new Color(0.35f, 0.85f, 0.45f, 1f);

        [Tooltip("Ghost preview where it may not.")]
        public Color CursorBlocked = new Color(0.87f, 0.32f, 0.32f, 1f);

        /// <summary>Colour for one of the three ammo shapes. None falls back to the outline.</summary>
        public Color ShapeColor(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.Triangle: return TriangleShape;
                case ShapeType.Square: return SquareShape;
                case ShapeType.Circle: return CircleShape;
                default: return Outline;
            }
        }
    }
}
