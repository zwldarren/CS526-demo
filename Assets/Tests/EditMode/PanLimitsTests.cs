using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The pan bound decides whether a tile at the map's edge can be looked at, and the HUD is what made
    /// the old bound wrong: the map had to cover the whole screen, so at any zoom the player would use,
    /// the bottom rows could not be panned clear of the build bar - rows nobody can look at, on the very
    /// edge a belt run has to be routed along. These tests pin the bound in screen pixels: how much of
    /// the screen each panel owns, how far past the map's edge the camera may then travel, and that the
    /// two together leave the background showing on every side.
    ///
    /// Measured on real screens, real maps and the rig's real zoom range, because the failure they guard
    /// is a ring of invisible tiles - something the player notices and nothing throws.
    /// </summary>
    public class PanLimitsTests
    {
        /// <summary>Half the visible height, i.e. the camera's orthographic size. 14 is the scene's.</summary>
        private const float SceneZoom = 14f;

        private static readonly Vector2[] Screens =
        {
            new Vector2(1280f, 720f),
            new Vector2(1920f, 1080f),
            new Vector2(2560f, 1440f),
            new Vector2(3840f, 2160f),
            new Vector2(800f, 600f),      // small and 4:3, to reach the bottom of the scale clamp
        };

        /// <summary>Map 1 and map 2, as (width, height).</summary>
        private static readonly Vector2[] Maps = { new Vector2(80f, 48f), new Vector2(64f, 40f) };

        /// <summary>The rig's zoom range, plus the scene's default in the middle.</summary>
        private static readonly float[] Zooms = { 4f, SceneZoom, 18f };

        private static readonly ScreenInsets None = new ScreenInsets(0f, 0f, 0f, 0f);

        /// <summary>A camera position far past anything the bound allows, so the clamp returns the limit
        /// itself - which is what these tests want to look at.</summary>
        private static readonly Vector3 FarCorner = new Vector3(-1e6f, -1e6f, 0f);

        [Test]
        public void WithNoPanelOnASide_TheMapStillCoversThatSide()
        {
            // The old rule, which is now just the no-panel, no-slack case of the new one.
            Assert.AreEqual(SceneZoom, PanLimits.ClampAxis(0f, SceneZoom, 48, None.Bottom, None.Top, 1080f), 1e-4f);
            Assert.AreEqual(48f - SceneZoom, PanLimits.ClampAxis(1e6f, SceneZoom, 48, None.Bottom, None.Top, 1080f), 1e-4f);
        }

        /// <summary>
        /// No slack: the bound stops the moment the map covers everything the HUD does not - the rule the
        /// slack is added to.
        /// </summary>
        [Test]
        public void WithoutSlack_TheMapStopsOnTheStripEachPanelOwns()
        {
            var screen = new Vector2(1920f, 1080f);
            ScreenInsets insets = HudLayout.Insets(HudLayout.Scale(screen.y));

            Vector3 centre = PanLimits.Clamp(FarCorner, SceneZoom, screen.x / screen.y, screen.x, screen.y,
                80, 48, insets, edgeSlack: 0f);
            Vector2 corner = ScreenPoint(0f, 0f, new Vector2(centre.x, centre.y), SceneZoom, screen);

            Assert.AreEqual(insets.Left, corner.x, 1e-2f, "the left column does not stop on the status card's strip");
            Assert.AreEqual(screen.y - insets.Bottom, corner.y, 1e-2f, "the bottom row does not stop on the build bar's strip");
        }

        /// <summary>
        /// The bound with its slack: panned as far as it allows, every side of the map has been carried
        /// past the panel that used to hide it, into the open screen - background visible behind the edge,
        /// on all four sides including the bottom one, which the build bar covers across its whole strip.
        /// </summary>
        [Test]
        public void PannedAsFarAsItGoes_EverySideOfTheMapShowsBackground()
        {
            foreach (Vector2 screen in Screens)
            foreach (Vector2 map in Maps)
            foreach (float zoom in Zooms)
            {
                string at = " at " + screen.x + "x" + screen.y + ", map " + map.x + "x" + map.y + ", zoom " + zoom;
                ScreenInsets insets = HudLayout.Insets(HudLayout.Scale(screen.y));
                float aspect = screen.x / screen.y;
                float slackX = PanLimits.DefaultEdgeSlack * screen.x;
                float slackY = PanLimits.DefaultEdgeSlack * screen.y;

                Vector3 low = PanLimits.Clamp(FarCorner, zoom, aspect, screen.x, screen.y,
                    (int)map.x, (int)map.y, insets);
                Vector2 lowCorner = ScreenPoint(0f, 0f, new Vector2(low.x, low.y), zoom, screen);

                Assert.AreEqual(insets.Left + slackX, lowCorner.x, 1e-2f, "the left column has the wrong slack" + at);
                Assert.AreEqual(screen.y - insets.Bottom - slackY, lowCorner.y, 1e-2f,
                    "the bottom row is not clear of the build bar" + at);
                Assert.Greater(lowCorner.x - insets.Left, 0f, "no background beside the left column" + at);
                Assert.Greater(screen.y - insets.Bottom - lowCorner.y, 0f, "no background under the bottom row" + at);

                Vector3 high = PanLimits.Clamp(new Vector3(1e6f, 1e6f, 0f), zoom, aspect, screen.x, screen.y,
                    (int)map.x, (int)map.y, insets);
                Vector2 highCorner = ScreenPoint(map.x, map.y, new Vector2(high.x, high.y), zoom, screen);

                Assert.AreEqual(screen.x - insets.Right - slackX, highCorner.x, 1e-2f, "the right column has the wrong slack" + at);
                Assert.AreEqual(insets.Top + slackY, highCorner.y, 1e-2f, "the top row is not clear of the top cards" + at);
                Assert.Greater(screen.x - insets.Right - highCorner.x, 0f, "no background beside the right column" + at);
                Assert.Greater(highCorner.y - insets.Top, 0f, "no background above the top row" + at);
            }
        }

        /// <summary>A map too small to fill the open screen is placed in the middle of it, rather than
        /// pushed up against a panel by a bound that assumes it is bigger than the screen. The slack is
        /// symmetric, so it does not move that middle.</summary>
        [Test]
        public void AMapSmallerThanTheOpenScreen_IsCentredInIt()
        {
            var screen = new Vector2(1920f, 1080f);
            ScreenInsets insets = HudLayout.Insets(HudLayout.Scale(screen.y));
            const float size = 4f;                     // world units, so the whole map is one screenful

            Vector3 centre = PanLimits.Clamp(FarCorner, SceneZoom, screen.x / screen.y, screen.x, screen.y,
                (int)size, (int)size, insets);
            var camera = new Vector2(size * 0.5f, centre.y);

            float openMiddle = (insets.Top + screen.y - insets.Bottom) * 0.5f;
            Assert.AreEqual(openMiddle, ScreenPoint(size * 0.5f, size * 0.5f, camera, SceneZoom, screen).y, 1e-2f,
                "the map is not centred in the screen the HUD leaves open");
            Assert.GreaterOrEqual(ScreenPoint(size * 0.5f, size, camera, SceneZoom, screen).y, insets.Top - 1e-2f,
                "the map's top row is under the top cards");
            Assert.LessOrEqual(ScreenPoint(size * 0.5f, 0f, camera, SceneZoom, screen).y, screen.y - insets.Bottom + 1e-2f,
                "the map's bottom row is under the build bar");
        }

        /// <summary>
        /// The other half of the contract: the strips the rig clears are the strips the panels actually
        /// occupy. The reserve and the panels are written down separately on purpose - the reserve has to
        /// follow the widest card on each side - so a card moved or added without the strip following it
        /// is a tile the camera will leave underneath it.
        /// </summary>
        [Test]
        public void EveryPanel_SitsInsideTheStripTheCameraClears()
        {
            foreach (Vector2 screen in Screens)
            {
                string at = " at " + screen.x + "x" + screen.y;
                float s = HudLayout.Scale(screen.y);
                ScreenInsets insets = HudLayout.Insets(s);

                Rect bar = HudLayout.Bar(screen.x, screen.y, s);
                Rect status = HudLayout.Status(s);
                Rect controls = HudLayout.Controls(screen.x, s);
                Rect info = HudLayout.Info(bar, s);

                Assert.LessOrEqual(status.xMax, insets.Left + 1e-2f, "the status card is wider than the left strip" + at);
                Assert.LessOrEqual(status.yMax, insets.Top + 1e-2f, "the status card is taller than the top strip" + at);
                Assert.GreaterOrEqual(controls.x, screen.x - insets.Right - 1e-2f, "the controls card reaches into the open screen" + at);
                Assert.LessOrEqual(controls.yMax, insets.Top + 1e-2f, "the controls card is taller than the top strip" + at);
                Assert.GreaterOrEqual(info.x, screen.x - insets.Right - 1e-2f, "the info card reaches into the open screen" + at);
                Assert.LessOrEqual(info.yMax, bar.y + 1e-2f, "the info card reaches into the build bar" + at);
                Assert.GreaterOrEqual(bar.y, screen.y - insets.Bottom - 1e-2f, "the build bar is taller than the bottom strip" + at);
                Assert.LessOrEqual(bar.yMax, screen.y + 1e-2f, "the build bar runs off the screen" + at);
            }
        }

        /// <summary>Where a world point lands on the screen, in pixels, for an orthographic camera
        /// centred on <paramref name="centre"/> on a screen <paramref name="screen"/> pixels across.</summary>
        private static Vector2 ScreenPoint(float worldX, float worldY, Vector2 centre, float zoom, Vector2 screen)
        {
            float halfWidth = zoom * screen.x / screen.y;
            float pixelsPerUnit = screen.y / (2f * zoom);

            return new Vector2(
                (worldX - centre.x + halfWidth) * pixelsPerUnit,
                (centre.y + zoom - worldY) * pixelsPerUnit);
        }
    }
}
