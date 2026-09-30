using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Where the HUD's panels sit, as pure geometry in screen pixels: the three cards and the bar the
    /// view draws, and the strips of the screen they own.
    ///
    /// A table rather than fields on <see cref="HudView"/> because two views need the same answer: the
    /// HUD draws the panels, and the camera rig must not leave a tile hidden behind one. A second copy
    /// of "the bar is 126 units tall" would drift from the first.
    ///
    /// Panel sizes are unscaled and multiplied by <see cref="Scale"/> - the same scale the HUD's fonts
    /// use - so the layout stays the same fraction of the screen at every resolution instead of the
    /// text growing inside a fixed box.
    /// </summary>
    public static class HudLayout
    {
        /// <summary>Gap between a panel and the screen edge.</summary>
        public const float Margin = 12f;

        // Unscaled panel sizes. The status card is the largest by a distance, which is why the left
        // and top strips are measured from it (see Insets).
        public const float StatusWidth = 470f;
        public const float StatusHeight = 252f;
        public const float ControlsWidth = 210f;
        public const float ControlsHeight = 100f;
        public const float InfoWidth = 392f;
        public const float InfoHeight = 134f;
        public const float BarHeight = 126f;

        /// <summary>The gap the info card floats above the bar with.</summary>
        public const float InfoGap = 8f;

        /// <summary>How much bigger than a 720p screen everything is drawn.</summary>
        public static float Scale(float screenHeight) => Mathf.Clamp(screenHeight / 720f, 0.8f, 2.2f);

        public static Rect Status(float s)
            => new Rect(Margin * s, Margin * s, StatusWidth * s, StatusHeight * s);

        public static Rect Controls(float screenWidth, float s)
            => new Rect(screenWidth - (Margin + ControlsWidth) * s, Margin * s,
                ControlsWidth * s, ControlsHeight * s);

        public static Rect Bar(float screenWidth, float screenHeight, float s)
            => new Rect(Margin * s, screenHeight - (Margin + BarHeight) * s,
                screenWidth - 2f * Margin * s, BarHeight * s);

        public static Rect Info(Rect bar, float s)
            => new Rect(bar.xMax - InfoWidth * s, bar.y - (InfoGap + InfoHeight) * s,
                InfoWidth * s, InfoHeight * s);

        /// <summary>
        /// How far the panels reach into the screen from each edge, in pixels: the strip the camera rig
        /// keeps the map clear of, so that every tile can be panned out from under the HUD.
        ///
        /// Each strip is the widest panel on its side, so two of the four are not the panel they are
        /// named after: the right strip is the wider of the two right-hand cards (the info card), and
        /// the top strip the taller of the two top cards (the status card). The bottom strip is the bar
        /// alone - the info card floats above it, where the right strip has already cleared it.
        /// </summary>
        public static ScreenInsets Insets(float s)
            => new ScreenInsets(
                (Margin + StatusWidth) * s,
                (Margin + Mathf.Max(ControlsWidth, InfoWidth)) * s,
                (Margin + Mathf.Max(StatusHeight, ControlsHeight)) * s,
                (Margin + BarHeight) * s);
    }
}
