using System;
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

        [Tooltip("Rig body fill.")]
        public Color RigFill = new Color(0.87f, 0.90f, 0.96f, 1f);

        [Tooltip("Outline used by every polygon.")]
        public Color Outline = new Color(0.05f, 0.06f, 0.09f, 1f);

        [Tooltip("Outline thickness in screen pixels (design doc: 2 px).")]
        public float OutlinePixels = 2f;
    }
}
