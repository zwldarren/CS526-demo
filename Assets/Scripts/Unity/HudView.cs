using System.Text;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Every readout the prototype needs, in four corners: the map and its wave, the stockpile and
    /// the Core's health, the defence's two diagnostic numbers (turrets armed, segments jammed), the
    /// build palette with costs and numbers, the controls, and a banner when the run ends.
    ///
    /// Two sentences in the design doc land here. "The intermission reads like a bill of materials":
    /// the preview names each enemy with the shape it is weak to, so "what do I need for this wave?"
    /// is answered as a shopping list. And "every building is paid for in circles banked at the
    /// Core": the stockpile is the economy's one number, so it sits under the title, and every build
    /// row carries its cost.
    ///
    /// Drawn with IMGUI on purpose: the whole game is built in code, with no prefabs and no scene
    /// authoring, and a hand-authored uGUI canvas would be the one asset that has to be edited in the
    /// Editor to be changed. The colours still come from the Palette, and the numbers printed for a
    /// building come from <see cref="Balance"/>, so tuning a stat tunes what the HUD claims.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class HudView : MonoBehaviour
    {
        private const float Margin = 12f;

        /// <summary>Rebuild the styles once the screen has scaled this far from the built size.</summary>
        private const float StyleScaleTolerance = 0.05f;

        private SimWorld _world;
        private Palette _palette;

        private Texture2D _pixel;
        private GUIStyle _title;
        private GUIStyle _body;
        private GUIStyle _small;
        private GUIStyle _banner;
        private GUIStyle _button;

        private float _stylesScale = -1f;

        /// <summary>The mouse is over a HUD panel right now, so a click belongs to the UI and not to the
        /// world underneath. Read by the driver and handed to <see cref="BuildInput.Sample"/>.</summary>
        public bool PointerOverHud { get; private set; }

        /// <summary>Is there a wave to start early, and is the world in a state to start it? The button
        /// is drawn only when this is true, so it never promises something the director would ignore.</summary>
        private bool CanStartWaveNow =>
            _world.Status == GameStatus.Playing && !_world.Paused &&
            _world.Waves.CurrentWave == 0 && _world.Waves.NextWave > 0;

        /// <summary>The HUD reads only simulation state and the screen: no mesh, no frame.</summary>
        public void Initialize(SimWorld world, Palette palette)
        {
            _world = world;
            _palette = palette;

            // One white pixel, tinted per draw: a texture per panel colour would be four textures whose
            // only difference is a multiply.
            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "FACET/HudPixel", hideFlags = HideFlags.DontSave };
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        private void OnGUI()
        {
            if (_world == null || _palette == null) return;

            EnsureStyles();

            float s = _stylesScale;
            var status = new Rect(Margin * s, Margin * s, 430f * s, 210f * s);
            var build = new Rect(Margin * s, Screen.height - Margin * s - 250f * s, 500f * s, 250f * s);

            // The HUD owns its panels: the pointer being over one is reported to the input source, so a
            // click on the button cannot also build under it. OnGUI runs after the driver's Update, so
            // this describes the panel under the pointer as of the previous frame - a frame of lag on a
            // pointer that has to travel to the button anyway. The banner is left out on purpose: it has
            // nothing to click, and while it is up (paused, or the run is over) the world is not ticking.
            PointerOverHud = PointerOver(status) || PointerOver(build);

            DrawStatus(status, s);
            DrawBuildPanel(build, s);
            DrawBanner(s);
        }

        // ------------------------------------------------------------------ status

        private void DrawStatus(Rect panel, float s)
        {
            Panel(panel);

            Rect line = new Rect(panel.x + 12f * s, panel.y + 10f * s, panel.width - 24f * s, 22f * s);
            Label(line, TitleLine(), _palette.HudAccent, _title);

            line.y += 26f * s;
            DrawHealthBar(new Rect(line.x, line.y, line.width, 16f * s), s);
            line.y += 22f * s;

            // The economy's one number, with the reminder of how it grows: circles banked by
            // belting them home, not mined into the pocket.
            Label(line, "stockpile " + _world.Economy.Circles + " " + Glyph(ShapeType.Circle) +
                    "   (belt circles into the core to bank them)", _palette.HudAccent, _body);
            line.y += 20f * s;

            _world.CountTurrets(out int total, out int armed);
            bool starved = armed < total;
            Label(line, "turrets armed " + armed + " / " + total + (starved ? "   (a starved turret's icon is grey)" : ""),
                starved ? _palette.HudWarn : _palette.HudGood, _body);
            line.y += 20f * s;

            int jams = _world.JamCount;
            Label(line, jams == 0
                    ? "no jammed segments"
                    : jams + " jammed segment" + (jams == 1 ? "" : "s") + "   [RMB clears one]",
                jams == 0 ? _palette.HudText : _palette.HudWarn, _body);
            line.y += 20f * s;

            Label(new Rect(line.x, line.y, line.width, 40f * s), WaveLines(), _palette.HudText, _small);
            line.y += 40f * s;

            if (CanStartWaveNow) DrawStartWaveButton(new Rect(line.x, line.y, 250f * s, 26f * s));
        }

        /// <summary>The one thing in the HUD the player presses: start the next wave now instead of
        /// waiting out its countdown - and the only way the first wave, which has no countdown, ever
        /// arrives. It reports the request to the world rather than calling the director, so a click
        /// lands on a tick boundary like every other input.</summary>
        private void DrawStartWaveButton(Rect rect)
        {
            Color previousBackground = GUI.backgroundColor;
            Color previousContent = GUI.contentColor;
            GUI.backgroundColor = _palette.HudAccent;
            GUI.contentColor = _palette.HudPanel;

            if (GUI.Button(rect, "▶  start wave " + _world.Waves.NextWave + " now", _button))
                _world.RequestNextWave();

            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;
        }

        private static bool PointerOver(Rect rect) => rect.Contains(Event.current.mousePosition);

        private void DrawHealthBar(Rect bar, float s)
        {
            Fill(bar, _palette.HudPanel);
            float fraction = _world.Core.HealthFraction;
            Color colour = fraction > 0.4f ? _palette.HudGood : _palette.HudWarn;
            Fill(new Rect(bar.x, bar.y, bar.width * fraction, bar.height), colour);

            Label(new Rect(bar.x + 6f * s, bar.y - 1f * s, bar.width, bar.height),
                "core " + Mathf.CeilToInt(_world.Core.Hp) + " / " + Mathf.CeilToInt(_world.Core.MaxHp),
                _palette.Outline, _small);
        }

        private string TitleLine()
        {
            int waveCount = _world.Map.Waves.Length;
            string map = _world.Map.Name;

            switch (_world.Status)
            {
                case GameStatus.Won: return "FACET  ·  " + map + "  ·  map cleared";
                case GameStatus.Lost: return "FACET  ·  " + map + "  ·  the core is gone";
            }

            if (_world.Waves.CurrentWave == 0)
            {
                // The first wave has no countdown to print - no clock starts it, the player does - so
                // its slot says "ready" instead of "in 0s" and the button below is the invitation. The
                // wave line underneath names the key, and the panel is only so wide.
                if (_world.Waves.FirstWaveHeld)
                    return "FACET  ·  " + map + "  ·  wave 1 of " + waveCount + "  ·  ready";

                return "FACET  ·  " + map + "  ·  wave " + _world.Waves.NextWave + " of " + waveCount +
                       " in " + Mathf.CeilToInt(_world.Waves.IntermissionRemaining) + "s";
            }

            return "FACET  ·  " + map + "  ·  wave " + _world.Waves.CurrentWave + " of " + waveCount;
        }

        /// <summary>The wave line and, during an intermission, the composition the player is building for.</summary>
        private string WaveLines()
        {
            if (_world.Status != GameStatus.Playing) return "press R to restart";

            WaveDirector waves = _world.Waves;
            if (waves.NextWave == 0) return "no waves left";

            if (waves.CurrentWave == 0)
            {
                WaveDefinition next = waves.NextWaveDefinition;
                string incoming = "incoming: " + Composition(next);

                // The first wave arrives when the player calls it, so the line names the call.
                return waves.FirstWaveHeld ? incoming + "   ·   press N to start it" : incoming;
            }

            WaveDefinition current = _world.Map.Waves[waves.CurrentWave - 1];
            int left = _world.Enemies.AliveCount + waves.PendingInWave;
            return "left in this wave: " + left + " of " + current.Total + " (" + Composition(current) + ")";
        }

        /// <summary>"16 Spike (◠-weak)" - the doc's bill of materials, over whatever enemies exist.</summary>
        private static string Composition(WaveDefinition wave)
        {
            var text = new StringBuilder();
            for (int i = 0; i < Balance.EnemyKinds.Length; i++)
            {
                var kind = Balance.EnemyKinds[i];
                int count = wave.CountOf(kind);
                if (count == 0) continue;

                EnemySpec spec = Balance.Enemy(kind);
                if (text.Length > 0) text.Append(" · ");
                text.Append(count).Append(' ').Append(spec.Name).Append(" (").Append(Glyph(spec.Weakness)).Append("-weak)");
            }

            return text.ToString();
        }

        // ------------------------------------------------------------------ build palette

        private void DrawBuildPanel(Rect panel, float s)
        {
            Panel(panel);

            Rect line = new Rect(panel.x + 12f * s, panel.y + 8f * s, panel.width - 24f * s, 20f * s);
            Label(line, "build — 1-6, Q/E to turn it, LMB to place (drags for belts) · everything costs "
                + Glyph(ShapeType.Circle), _palette.HudAccent, _small);
            line.y += 22f * s;

            for (int i = 0; i < BuildCatalog.Count; i++)
            {
                BuildKind kind = BuildCatalog.All[i];
                bool selected = kind == _world.SelectedKind;
                bool affordable = _world.Economy.CanAfford(kind);

                Label(line, (selected ? "▶ " : "   ") + (i + 1) + "   " + Name(kind),
                    selected ? _palette.HudAccent : _palette.HudText, _body);

                Color detailColour;
                if (!affordable) detailColour = _palette.CursorNoFunds;
                else if (selected) detailColour = _palette.HudText;
                else detailColour = _palette.MachineIdle;

                Label(new Rect(line.x + 118f * s, line.y, line.width - 118f * s, line.height),
                    Balance.Cost(kind) + " " + Glyph(ShapeType.Circle) + " · " + Detail(kind),
                    detailColour, _small);

                line.y += 24f * s;
            }

            Label(new Rect(line.x, line.y + 2f * s, line.width, panel.height - (line.y - panel.y) - 10f * s),
                "WASD / arrows pan · wheel zooms · RMB deletes for a full refund (a jammed segment is cleared first)\n" +
                "belt = 1 shape/s · the cannon fires only half-circles that reach it · R restarts · Space pauses · N starts the next wave",
                _palette.HudText, _small);
        }

        private static string Name(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.Belt: return "Belt";
                case BuildKind.Drill: return "Drill";
                case BuildKind.Decomposer: return "Decomposer";
                case BuildKind.Pipe: return "Pipe";
                case BuildKind.Splitter: return "Splitter";
                default: return Balance.Turret(kind).Name;
            }
        }

        /// <summary>What the building does, with its numbers - the same table the simulation runs on.</summary>
        private static string Detail(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.Belt:
                    return "carries one shape per tile · drag to lay a run";
                case BuildKind.Drill:
                    return "on a shape patch · mines " + (1f / Balance.DrillInterval).ToString("0.##") +
                           " " + Glyph(ShapeType.Circle) + "/s onto the belt it faces";
                case BuildKind.Decomposer:
                    return "splits " + Glyph(ShapeType.Circle) + " into " + Glyph(ShapeType.HalfCircle) +
                           Glyph(ShapeType.HalfCircle) + " · " + Balance.DecomposeInterval.ToString("0.##") + " s per split";
                case BuildKind.Pipe:
                    return "jumps one tile - the crossing piece · never jams";
                case BuildKind.Splitter:
                    return "ports read off the belts around it · in/out by their direction";
            }

            TurretSpec spec = Balance.Turret(kind);
            var text = new StringBuilder();
            text.Append("eats ").Append(Glyph(spec.Ammo)).Append(" · ")
                .Append(spec.ShotsPerSecond.ToString("0.##")).Append(" shots/s · ")
                .Append(spec.Damage.ToString("0.##")).Append(" dmg · range ").Append(spec.Range.ToString("0.##"));
            return text.ToString();
        }

        private static string Glyph(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.Circle: return "○";
                case ShapeType.HalfCircle: return "◠";
                default: return "·";
            }
        }

        // ------------------------------------------------------------------ banner

        /// <summary>The one thing that has to be unmissable: the run is over, or the game is paused.
        /// An ended run also reports itself - shots fired, circles banked - so "do better" has numbers.</summary>
        private void DrawBanner(float s)
        {
            string text;
            Color colour;

            switch (_world.Status)
            {
                case GameStatus.Lost:
                    text = "CORE DESTROYED\n" + StatsLine() + "\nR to restart";
                    colour = _palette.HudWarn;
                    break;
                case GameStatus.Won:
                    text = _world.Map.Name.ToUpperInvariant() + " CLEARED\n" + StatsLine() + "\nR to restart";
                    colour = _palette.HudGood;
                    break;
                default:
                    if (!_world.Paused) return;
                    text = "PAUSED\nSpace to resume · R to restart";
                    colour = _palette.HudAccent;
                    break;
            }

            float width = 460f * s;
            float height = 150f * s;
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.42f, width, height);

            Panel(panel);
            Label(new Rect(panel.x, panel.y + 22f * s, panel.width, panel.height - 30f * s), text, colour, _banner);
        }

        /// <summary>The run in one line: how much shooting the defence did, and how much economy
        /// the player built - the two axes "a better run" moves on.</summary>
        private string StatsLine()
        {
            return _world.ShotsFired + " shots fired · " + _world.Economy.TotalBanked + " " +
                Glyph(ShapeType.Circle) + " banked";
        }

        // ------------------------------------------------------------------ drawing helpers

        private void EnsureStyles()
        {
            float s = Mathf.Clamp(Screen.height / 720f, 0.8f, 2.2f);
            if (_title != null && Mathf.Abs(s - _stylesScale) < StyleScaleTolerance) return;

            _stylesScale = s;

            _title = MakeStyle(15, FontStyle.Bold, TextAnchor.UpperLeft);
            _body = MakeStyle(13, FontStyle.Normal, TextAnchor.UpperLeft);
            _small = MakeStyle(12, FontStyle.Normal, TextAnchor.UpperLeft);
            _banner = MakeStyle(22, FontStyle.Bold, TextAnchor.MiddleCenter);

            // From GUI.skin.button and not GUI.skin.label, so the button keeps the skin's own hover and
            // pressed states; only its colour and size are ours.
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = false,
                normal = { textColor = Color.white },
            };
        }

        /// <summary>Text colour is applied per label through GUI.contentColor, so every style's own
        /// colour is white and no per-frame style clones are needed.</summary>
        private static GUIStyle MakeStyle(int size, FontStyle font, TextAnchor anchor)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = font,
                alignment = anchor,
                wordWrap = true,
                richText = false,
                normal = { textColor = Color.white },
                padding = new RectOffset(0, 0, 0, 0),
            };
        }

        /// <summary>Panel backdrop, scaled with the font size so the HUD stays proportional.</summary>
        private void Panel(Rect rect)
        {
            Fill(rect, _palette.HudPanel);
        }

        private void Fill(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, _pixel);
            GUI.color = previous;
        }

        private void Label(Rect rect, string text, Color colour, GUIStyle style)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = colour;
            GUI.Label(rect, text, style);
            GUI.contentColor = previous;
        }
    }
}
