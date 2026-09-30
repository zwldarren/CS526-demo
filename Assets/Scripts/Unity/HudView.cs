using System;
using System.Text;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The player's whole operating surface, laid out in four fixed regions: a status card top-left
    /// (the map and its wave, the Core's health, the stockpile, the defence's two diagnostics and the
    /// last second's news), a small controls card top-right, a clickable **build bar** along the
    /// bottom, and an info card just above it that describes whichever building is hovered or selected.
    ///
    /// The build bar is the point of this view. Every building is a real button carrying its hotkey,
    /// its name, its role and its cost; a tile is tinted when the stockpile cannot cover it, outlined
    /// when it is selected, and highlighted under the pointer. Clicking one selects it exactly the way
    /// a number key does (through the input latch, so the "the press decides what is built" rule is
    /// untouched), which is what lets the game be played without memorising the keyboard - on WebGL,
    /// or on a trackpad, or by someone who has never seen it.
    ///
    /// Two sentences in the design doc land here. "The intermission reads like a bill of materials":
    /// the preview names each enemy with the shape it is weak to, so "what do I need for this wave?"
    /// is answered as a shopping list. And "every building is paid for in circles banked at the
    /// Core": the stockpile is the economy's one number, and every tile carries its cost.
    ///
    /// Drawn with IMGUI on purpose: the whole game is built in code, with no prefabs and no scene
    /// authoring, and a hand-authored uGUI canvas would be the one asset that has to be edited in the
    /// Editor to be changed. It is immediate mode, but it is not un-styled: panels, borders, accent
    /// stripes and hover states all come from the <see cref="Palette"/>, and every number printed for a
    /// building comes from the run's <see cref="ContentDatabase"/>, so tuning a stat tunes what the HUD
    /// claims.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class HudView : MonoBehaviour
    {
        private const float Margin = 12f;

        /// <summary>Rebuild the styles once the screen has scaled this far from the built size.</summary>
        private const float StyleScaleTolerance = 0.05f;

        // Panel sizes, unscaled: multiplied by the same screen scale the fonts use, so the layout
        // stays proportional instead of the text growing inside a fixed box.
        private const float StatusWidth = 470f;
        private const float StatusHeight = 252f;
        private const float TutorialHeight = 112f;
        private const float ControlsWidth = 210f;
        private const float ControlsHeight = 100f;
        private const float InfoWidth = 392f;
        private const float InfoHeight = 134f;
        private const float BarHeight = 126f;

        /// <summary>The window the "last second" line reports over: one second, in ticks, so the
        /// numbers a player steers by are a rate rather than a running total.</summary>
        private static readonly int RecentTicks = (int)SimConfig.TickRate;

        private SimWorld _world;
        private Palette _palette;
        private CampaignState _campaign;

        /// <summary>The build bar's two ways back into the input latch: select a kind, and turn the
        /// placement ghost. Handed in by the driver rather than reached for, so the HUD cannot touch
        /// anything the input source does not offer, and a scene with no driver still draws.</summary>
        private Action<BuildKind> _selectKind;
        private Action<int> _rotate;

        private Texture2D _pixel;
        private GUIStyle _title;
        private GUIStyle _labelCenter;
        private GUIStyle _small;
        private GUIStyle _smallRight;
        private GUIStyle _tiny;
        private GUIStyle _tinyRight;
        private GUIStyle _tinyCenter;
        private GUIStyle _wrap;
        private GUIStyle _button;
        private GUIStyle _banner;

        private float _stylesScale = -1f;

        /// <summary>Which build tile the pointer is over this frame, or -1. Read by the info card,
        /// which describes the hovered building in preference to the selected one.</summary>
        private int _hoveredTile = -1;

        /// <summary>The mouse is over a HUD panel right now, so a click belongs to the UI and not to the
        /// world underneath. Read by the driver and handed to <see cref="BuildInput.Sample"/>.</summary>
        public bool PointerOverHud { get; private set; }

        /// <summary>Is there a wave to start early, and is the world in a state to start it? The button
        /// is drawn only when this is true, so it never promises something the director would ignore.</summary>
        private bool CanStartWaveNow =>
            _world.Status == GameStatus.Playing && !_world.Paused &&
            _world.Waves.CurrentWave == 0 && _world.Waves.NextWave > 0;

        /// <summary>
        /// Point the HUD at a world, its palette, and where the player is in the campaign. Called
        /// again on every map change, which is why the pixel texture is created once and kept: the
        /// styles and the one white pixel outlive every world the HUD has drawn.
        ///
        /// <paramref name="campaign"/> is optional so a scene (or a test) with one map can leave it
        /// out and get exactly the single-map HUD. The selection and rotation callbacks are optional
        /// for the same reason: without them the bar still draws, it just cannot be clicked.
        /// </summary>
        public void Initialize(SimWorld world, Palette palette, CampaignState campaign = null,
            Action<BuildKind> selectKind = null, Action<int> rotate = null)
        {
            _world = world;
            _palette = palette;
            _campaign = campaign;
            if (selectKind != null) _selectKind = selectKind;
            if (rotate != null) _rotate = rotate;

            if (_pixel != null) return;

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

            var status = new Rect(Margin * s, Margin * s, StatusWidth * s, StatusHeight * s);

            bool showTutorial = _campaign != null && _campaign.MapIndex == 0;

var tutorial = new Rect(
    status.x,
    status.yMax + 8f * s,
    StatusWidth * s,
    TutorialHeight * s);

            var controls = new Rect(Screen.width - Margin * s - ControlsWidth * s, Margin * s,
                ControlsWidth * s, ControlsHeight * s);
            var bar = new Rect(Margin * s, Screen.height - Margin * s - BarHeight * s,
                Screen.width - 2f * Margin * s, BarHeight * s);
            var info = new Rect(bar.xMax - InfoWidth * s, bar.y - 8f * s - InfoHeight * s,
                InfoWidth * s, InfoHeight * s);

            // The HUD owns its panels: the pointer being over one is reported to the input source, so a
            // click on a button cannot also build under it. OnGUI runs after the driver's Update, so
            // this describes the panel under the pointer as of the previous frame - a frame of lag on a
            // pointer that has to travel to the button anyway. The banner is left out on purpose: it has
            // nothing to click, and while it is up (paused, or the run is over) the world is not ticking.
            PointerOverHud =
    PointerOver(status) ||
    PointerOver(controls) ||
    PointerOver(bar) ||
    PointerOver(info) ||
    (showTutorial && PointerOver(tutorial));


            DrawStatus(status, s);

            if (showTutorial)
{
    DrawTutorial(tutorial, s);
}

            DrawControls(controls, s);

            // The bar runs first so it can record which tile is under the pointer; the info card reads
            // that decision and describes the hovered building in preference to the selected one.
            DrawBuildBar(bar, s);
            DrawInfo(info, s);

            DrawBanner(s);
        }

        // ------------------------------------------------------------------ status


private void DrawTutorial(Rect panel, float s)
{
    Panel(panel);
    Fill(new Rect(panel.x, panel.y, 3f * s, panel.height), _palette.HudAccent);

    Rect line = new Rect(
        panel.x + 14f * s,
        panel.y + 10f * s,
        panel.width - 28f * s,
        22f * s);

    Label(line, "HOW TO PLAY", _palette.HudAccent, _title);

    line.y += 28f * s;
    line.height = 70f * s;

    Label(
        line,
        "1. Mine circle ore and carry it back to the Core using conveyor belts.\n" +
        "2. Ore can only be spent after it reaches the Core.\n" +
        "3. The wrong shape will jam a line. Right-click the jammed segment to clear it.",
        _palette.HudText,
        _wrap);
}
        private void DrawStatus(Rect panel, float s)
        {
            Panel(panel);
            Fill(new Rect(panel.x, panel.y, 3f * s, panel.height), _palette.HudAccent);

            Rect line = new Rect(panel.x + 14f * s, panel.y + 10f * s, panel.width - 28f * s, 22f * s);
            Label(line, TitleLine(), _palette.HudAccent, _title);

            line.y += 28f * s;
            DrawHealthBar(new Rect(line.x, line.y, line.width, 16f * s), s);
            line.y += 24f * s;

            // The economy's one number, with the reminder of how it grows: circles banked by
            // belting them home, not mined into the pocket.
            Label(line, "stockpile " + _world.Economy.Circles + " " + Glyph(ShapeType.Circle) +
                    "   ·   belt circles into the core to bank them", _palette.HudAccent, _small);
            line.y += 20f * s;

            _world.CountTurrets(out int total, out int armed);
            bool starved = armed < total;
            Label(line, "turrets armed " + armed + " / " + total + (starved ? "   (a starved turret's icon is grey)" : ""),
                starved ? _palette.HudWarn : _palette.HudGood, _small);
            line.y += 20f * s;

            int jams = _world.JamCount;
            Label(line, jams == 0
                    ? "no jammed segments"
                    : jams + " jammed segment" + (jams == 1 ? "" : "s") + "   [RMB clears one]",
                jams == 0 ? _palette.HudText : _palette.HudWarn, _small);
            line.y += 20f * s;

            Label(line, RecentLine(), _palette.HudText, _tiny);
            line.y += 19f * s;

            Label(new Rect(line.x, line.y, line.width, 40f * s), WaveLines(), _palette.HudText, _tiny);
            line.y += 41f * s;

            if (CanStartWaveNow)
            {
                var button = new Rect(line.x, line.y, 250f * s, 26f * s);
                if (Button(button, "▶  start wave " + _world.Waves.NextWave + " now",
                        _palette.HudAccent, _palette.HudPanel, s))
                    _world.RequestNextWave();
            }
        }

        /// <summary>
        /// What the simulation reported in the last second, read straight off its event stream: the
        /// economy's heartbeat (circles banked), the defence's work (shots, kills) and the one thing
        /// the player has to act on (a jam, and where it is).
        ///
        /// A window rather than a log, deliberately: a scrolling list of one-second-old news is
        /// something to read instead of play, while these four numbers are what a player actually
        /// steers by - and they come from the same stream a sound or a flash would, so the HUD cannot
        /// disagree with the rest of the feedback about what just happened.
        /// </summary>
        private string RecentLine()
        {
            SimEventBuffer events = _world.Events;
            int since = _world.TickCount - RecentTicks;

            int banked = 0, shots = 0, kills = 0, jams = 0;
            var lastJam = new Int2(0, 0);

            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Tick < since) continue;

                switch (e.Kind)
                {
                    case SimEventKind.Banked: banked++; break;
                    case SimEventKind.ShotFired: shots++; break;
                    case SimEventKind.EnemyKilled: kills++; break;
                    case SimEventKind.Jammed: jams++; lastJam = e.Cell; break;
                }
            }

            var text = new StringBuilder();

            void Add(string part)
            {
                if (text.Length > 0) text.Append("  ·  ");
                text.Append(part);
            }

            if (banked > 0) Add(banked + " " + Glyph(ShapeType.Circle) + " banked");
            if (shots > 0) Add(shots + (shots == 1 ? " shot" : " shots"));
            if (kills > 0) Add(kills + (kills == 1 ? " kill" : " kills"));
            if (jams > 0) Add(jams + " jammed at (" + lastJam.X + "," + lastJam.Y + ")");

            return "last 1s:  " + (text.Length == 0 ? "quiet" : text.ToString());
        }

        private static bool PointerOver(Rect rect) => rect.Contains(Event.current.mousePosition);

        private void DrawHealthBar(Rect bar, float s)
        {
            Fill(bar, _palette.HudPanel);
            float fraction = _world.Core.HealthFraction;
            Color colour = fraction > 0.4f ? _palette.HudGood : _palette.HudWarn;
            Fill(new Rect(bar.x, bar.y, bar.width * fraction, bar.height), colour);
            Border(bar, Blend(_palette.HudText, _palette.HudPanel, 0.5f), 1f * s);

            Label(new Rect(bar.x + 6f * s, bar.y - 1f * s, bar.width, bar.height),
                "core " + Mathf.CeilToInt(_world.Core.Hp) + " / " + Mathf.CeilToInt(_world.Core.MaxHp),
                _palette.Outline, _tiny);
        }

        private string TitleLine()
        {
            int waveCount = _world.Map.Waves.Length;
            string map = _world.Map.Name;
            string at = _campaign != null && _campaign.MapCount > 1
                ? "map " + (_campaign.MapIndex + 1) + "/" + _campaign.MapCount + "  ·  "
                : string.Empty;

            switch (_world.Status)
            {
                case GameStatus.Won:
                    return "FACET  ·  " + at + map +
                           (_campaign != null && _campaign.HasNext ? "  ·  map cleared" : "  ·  campaign cleared");
                case GameStatus.Lost: return "FACET  ·  " + at + map + "  ·  the core is gone";
            }

            if (_world.Waves.CurrentWave == 0)
            {
                // The first wave has no countdown to print - no clock starts it, the player does - so
                // its slot says "ready" instead of "in 0s" and the button below is the invitation. The
                // wave line underneath names the key, and the panel is only so wide.
                if (_world.Waves.FirstWaveHeld)
                    return "FACET  ·  " + at + map + "  ·  wave 1 of " + waveCount + "  ·  ready";

                return "FACET  ·  " + at + map + "  ·  wave " + _world.Waves.NextWave + " of " + waveCount +
                       " in " + Mathf.CeilToInt(_world.Waves.IntermissionRemaining) + "s";
            }

            return "FACET  ·  " + at + map + "  ·  wave " + _world.Waves.CurrentWave + " of " + waveCount;
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
        private string Composition(WaveDefinition wave)
        {
            ContentDatabase content = _world.Content;
            var text = new StringBuilder();
            for (int i = 0; i < content.EnemyKinds.Length; i++)
            {
                EnemyKind kind = content.EnemyKinds[i];
                int count = wave.CountOf(kind);
                if (count == 0) continue;

                EnemyDef spec = content.Enemy(kind);
                if (text.Length > 0) text.Append(" · ");
                text.Append(count).Append(' ').Append(spec.Name).Append(" (").Append(Glyph(spec.Weakness)).Append("-weak)");
            }

            return text.ToString();
        }

        // ------------------------------------------------------------------ controls card

        /// <summary>
        /// The three buttons that are about the run rather than about a building: pause, restart, and
        /// (when there is one) the start-wave button living in the status card. Kept out of the build
        /// bar so a misplaced click can never restart the run.
        /// </summary>
        private void DrawControls(Rect panel, float s)
        {
            Panel(panel);

            Rect line = new Rect(panel.x + 12f * s, panel.y + 9f * s, panel.width - 24f * s, 14f * s);
            Label(line, "CONTROLS", _palette.HudAccent, _tiny);
            line.y += 19f * s;

            if (Button(new Rect(line.x, line.y, line.width, 26f * s), _world.Paused ? "▶  resume" : "II  pause",
                    ButtonFill, _palette.HudText, s))
                _world.Paused = !_world.Paused;
            line.y += 29f * s;

            if (Button(new Rect(line.x, line.y, line.width, 26f * s), "R  restart  run",
                    ButtonFill, _palette.HudText, s))
                _world.RequestRestart();
        }

        // ------------------------------------------------------------------ build bar

        private void DrawBuildBar(Rect panel, float s)
        {
            Panel(panel);
            Fill(new Rect(panel.x, panel.y, panel.width, 3f * s), _palette.HudAccent);
            _hoveredTile = -1;

            Rect header = new Rect(panel.x + 12f * s, panel.y + 9f * s, panel.width - 24f * s, 15f * s);
            Label(header, "BUILD", _palette.HudAccent, _tiny);
            Label(header,
                "click a tile or press 1–9   ·   Q / E turn the ghost   ·   LMB place (drag to lay belts)   ·   RMB delete or clear a jam",
                _palette.HudText, _tinyRight);

            BuildKind[] kinds = _world.Content.BuildKinds;
            int count = kinds.Length;
            float pad = 10f * s;
            float gap = 6f * s;
            float tileWidth = (panel.width - 2f * pad - (count - 1) * gap) / count;
            float tileY = panel.y + 29f * s;
            float tileHeight = panel.height - 39f * s;

            for (int i = 0; i < count; i++)
            {
                var tile = new Rect(panel.x + pad + i * (tileWidth + gap), tileY, tileWidth, tileHeight);
                bool hovered = PointerOver(tile);
                if (hovered) _hoveredTile = i;

                DrawBuildTile(tile, kinds[i], i, hovered, s);
            }
        }

        /// <summary>
        /// One building as a button: its hotkey in the corner, its cost in the other, its name in the
        /// middle and the job it does underneath, over a stripe tinted by that job. Selected is an
        /// accent outline and a tinted fill; unaffordable greys the name and turns the cost amber; the
        /// pointer lightens the whole tile. A click selects the kind through the input latch - it never
        /// places anything, so a stray click on the palette can only ever change what is selected.
        /// </summary>
        private void DrawBuildTile(Rect rect, BuildKind kind, int index, bool hovered, float s)
        {
            MachineDef def = _world.Content.Machine(kind);
            bool selected = kind == _world.SelectedKind;
            bool affordable = _world.Economy.CanAfford(kind);
            Color category = CategoryColor(kind);

            Color fill = _palette.HudPanel;
            if (selected) fill = Blend(_palette.HudPanel, category, 0.18f);
            else if (hovered) fill = Blend(_palette.HudPanel, _palette.HudText, 0.10f);
            Fill(rect, fill);

            Fill(new Rect(rect.x, rect.y, rect.width, 3f * s),
                selected ? category : Blend(category, _palette.HudPanel, 0.4f));

            if (selected) Border(rect, _palette.HudAccent, 2f * s);
            else if (hovered) Border(rect, Blend(_palette.HudText, _palette.HudPanel, 0.35f), 1f * s);

            Label(new Rect(rect.x + 8f * s, rect.y + 9f * s, 26f * s, 15f * s), (index + 1).ToString(),
                selected ? _palette.HudAccent : _palette.MachineIdle, _small);

            Label(new Rect(rect.x, rect.y + 9f * s, rect.width - 8f * s, 15f * s),
                def.Cost + " " + Glyph(ShapeType.Circle),
                affordable ? (selected ? _palette.HudText : _palette.MachineIdle) : _palette.CursorNoFunds,
                _smallRight);

            Label(new Rect(rect.x + 4f * s, rect.y + 27f * s, rect.width - 8f * s, 20f * s), def.Name,
                affordable ? _palette.HudText : _palette.MachineIdle, _labelCenter);

            Label(new Rect(rect.x + 4f * s, rect.y + rect.height - 20f * s, rect.width - 8f * s, 14f * s),
                RoleOf(kind), Blend(category, _palette.HudText, 0.15f), _tinyCenter);

            if (hovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                _selectKind?.Invoke(kind);
            }
        }

        // ------------------------------------------------------------------ info card

        /// <summary>
        /// What the hovered (or, when nothing is hovered, the selected) building actually does: its
        /// cost and role in the header, a sentence of what it is for, the numbers behind it, and the
        /// facing its ghost will be built with - plus the two rotate buttons, which are the one way to
        /// turn a building without a keyboard.
        /// </summary>
        private void DrawInfo(Rect panel, float s)
        {
            BuildKind[] kinds = _world.Content.BuildKinds;
            BuildKind kind = _hoveredTile >= 0 && _hoveredTile < kinds.Length ? kinds[_hoveredTile] : _world.SelectedKind;
            MachineDef def = _world.Content.Machine(kind);
            Color category = CategoryColor(kind);

            Panel(panel);
            Fill(new Rect(panel.x, panel.y, 3f * s, panel.height), category);

            Rect line = new Rect(panel.x + 14f * s, panel.y + 9f * s, panel.width - 28f * s, 18f * s);
            Label(line, def.Name, _palette.HudText, _title);
            Label(line, def.Cost + " " + Glyph(ShapeType.Circle) + "   " + RoleOf(kind),
                _palette.MachineIdle, _smallRight);
            line.y += 24f * s;

            Label(new Rect(line.x, line.y, line.width, 32f * s), Blurb(kind), _palette.HudText, _wrap);
            line.y += 34f * s;

            string stats = Stats(kind);
            if (!string.IsNullOrEmpty(stats))
            {
                Label(new Rect(line.x, line.y, line.width, 15f * s), stats, category, _small);
                line.y += 18f * s;
            }

            Label(new Rect(line.x, line.y, 130f * s, 22f * s),
                "facing " + DirName(_world.PlacementDirection), _palette.MachineIdle, _small);

            float buttonWidth = 54f * s;
            var left = new Rect(panel.xMax - 14f * s - buttonWidth * 2f - 6f * s, line.y, buttonWidth, 22f * s);
            var right = new Rect(panel.xMax - 14f * s - buttonWidth, line.y, buttonWidth, 22f * s);

            if (Button(left, "◀ turn", ButtonFill, _palette.HudText, s)) _rotate?.Invoke(-1);
            if (Button(right, "turn ▶", ButtonFill, _palette.HudText, s)) _rotate?.Invoke(+1);
        }

        private Color CategoryColor(BuildKind kind)
        {
            switch (_world.Content.Machine(kind).Behavior)
            {
                case BehaviorKind.Turret: return _palette.HudWarn;
                case BehaviorKind.Drill:
                case BehaviorKind.Converter: return _palette.CircleShape;
                case BehaviorKind.Splitter:
                case BehaviorKind.Sorter: return _palette.HudAccent;
                default: return _palette.HalfCircleShape;   // belts and pipes: transport
            }
        }

        private string RoleOf(BuildKind kind)
        {
            switch (_world.Content.Machine(kind).Behavior)
            {
                case BehaviorKind.Turret: return "DEFENCE";
                case BehaviorKind.Drill: return "MINING";
                case BehaviorKind.Converter: return "PROCESSING";
                case BehaviorKind.Splitter:
                case BehaviorKind.Sorter: return "ROUTING";
                default: return "TRANSPORT";
            }
        }

        /// <summary>The building in one sentence: the definition's own description where it has one,
        /// and a behaviour-shaped sentence where it does not - so a new turret or converter gets a
        /// sensible line without a HUD edit.</summary>
        private string Blurb(BuildKind kind)
        {
            MachineDef def = _world.Content.Machine(kind);

            switch (def.Behavior)
            {
                case BehaviorKind.Turret:
                {
                    TurretDef spec = _world.Content.Turret(kind);
                    return "eats " + Glyph(spec.Ammo) + " from any side · fires at whatever is in range";
                }

                case BehaviorKind.Converter:
                {
                    RecipeDef recipe = _world.Content.Recipe(def.RecipeId);
                    return "splits " + Glyph(recipe.Input) + " into " + Repeat(Glyph(recipe.Output), recipe.OutputCount) +
                           " · outlets read off the belts pointing away";
                }

                case BehaviorKind.Sorter:
                    return def.Description + " (" + Glyph(def.Filter) + ")";

                default:
                    return def.Description;
            }
        }

        /// <summary>The numbers behind the sentence, read from the same definitions the tick runs on.</summary>
        private string Stats(BuildKind kind)
        {
            MachineDef def = _world.Content.Machine(kind);

            switch (def.Behavior)
            {
                case BehaviorKind.Drill:
                    return def.Interval > 0f
                        ? (1f / def.Interval).ToString("0.##") + " " + Glyph(ShapeType.Circle) + "/s mined"
                        : string.Empty;

                case BehaviorKind.Converter:
                {
                    RecipeDef recipe = _world.Content.Recipe(def.RecipeId);
                    return recipe.Interval.ToString("0.##") + " s per split · holds " + recipe.Buffer + " when blocked";
                }

                case BehaviorKind.Pipe:
                    return def.Interval > 0f
                        ? "crosses 2 tiles · " + def.Interval.ToString("0.##") + " s per item"
                        : string.Empty;

                case BehaviorKind.Turret:
                {
                    TurretDef spec = _world.Content.Turret(kind);
                    return spec.ShotsPerSecond.ToString("0.##") + " shots/s · " + spec.Damage.ToString("0.##") +
                           " dmg · range " + spec.Range.ToString("0.##") + " tiles";
                }

                case BehaviorKind.Sorter:
                    return "one shape leaves by the side it faces · the rest take the other outlets";

                case BehaviorKind.Splitter:
                    return "deals items round-robin · skips blocked outlets";

                default:
                    return _world.Config.BeltSpeed.ToString("0.##") + " shapes/s per segment";
            }
        }

        private static string DirName(Dir direction)
        {
            switch (direction)
            {
                case Dir.North: return "north";
                case Dir.South: return "south";
                case Dir.West: return "west";
                default: return "east";
            }
        }

        private static string Repeat(string text, int count)
        {
            var repeated = new StringBuilder();
            for (int i = 0; i < count; i++) repeated.Append(text);
            return repeated.ToString();
        }

        private string Glyph(ShapeType shape) => _world.Content.Shape(shape).Glyph;

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
                    if (_campaign != null && _campaign.HasNext)
                    {
                        MapDefinition next = Maps.All[_campaign.MapIndex + 1];
                        text = _world.Map.Name.ToUpperInvariant() + " CLEARED\n" + StatsLine() +
                               "\npress N for the next map: " + next.Name;
                    }
                    else
                    {
                        text = _world.Map.Name.ToUpperInvariant() + " CLEARED\n" + StatsLine() +
                               (_campaign != null && _campaign.MapCount > 1 ? "\ncampaign complete · R to play it again" : "\nR to restart");
                    }

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

        private Color ButtonFill => Blend(_palette.HudPanel, _palette.HudText, 0.09f);

        private void EnsureStyles()
        {
            float s = Mathf.Clamp(Screen.height / 720f, 0.8f, 2.2f);
            if (_title != null && Mathf.Abs(s - _stylesScale) < StyleScaleTolerance) return;

            _stylesScale = s;

            _title = MakeStyle(15, FontStyle.Bold, TextAnchor.UpperLeft, wrap: false);
            _labelCenter = MakeStyle(13, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
            _small = MakeStyle(12, FontStyle.Normal, TextAnchor.UpperLeft, wrap: false);
            _smallRight = MakeStyle(12, FontStyle.Normal, TextAnchor.UpperRight, wrap: false);
            _tiny = MakeStyle(11, FontStyle.Normal, TextAnchor.UpperLeft, wrap: false);
            _tinyRight = MakeStyle(11, FontStyle.Normal, TextAnchor.UpperRight, wrap: false);
            _tinyCenter = MakeStyle(10, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
            _wrap = MakeStyle(11, FontStyle.Normal, TextAnchor.UpperLeft, wrap: true);
            _button = MakeStyle(13, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
            _banner = MakeStyle(22, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: true);
        }

        /// <summary>Text colour is applied per label through GUI.contentColor, so every style's own
        /// colour is white and no per-frame style clones are needed.</summary>
        private static GUIStyle MakeStyle(int size, FontStyle font, TextAnchor anchor, bool wrap)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = font,
                alignment = anchor,
                wordWrap = wrap,
                richText = false,
                normal = { textColor = Color.white },
                padding = new RectOffset(0, 0, 0, 0),
            };
        }

        /// <summary>Panel backdrop, with the thin edge that keeps a dark card from bleeding into the map.</summary>
        private void Panel(Rect rect)
        {
            Fill(rect, _palette.HudPanel);
            Border(rect, new Color(_palette.HudText.r, _palette.HudText.g, _palette.HudText.b, 0.12f), 1f);
        }

        private void Fill(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, _pixel);
            GUI.color = previous;
        }

        /// <summary>An inset border drawn as four filled edges, all scaled together.</summary>
        private void Border(Rect rect, Color colour, float thickness)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), colour);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), colour);
            Fill(new Rect(rect.x, rect.y + thickness, thickness, rect.height - 2f * thickness), colour);
            Fill(new Rect(rect.xMax - thickness, rect.y + thickness, thickness, rect.height - 2f * thickness), colour);
        }

        private void Label(Rect rect, string text, Color colour, GUIStyle style)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = colour;
            GUI.Label(rect, text, style);
            GUI.contentColor = previous;
        }

        /// <summary>
        /// A HUD button drawn from the palette rather than the skin, so it matches the cards. It fires
        /// on the press, which is what makes the build bar feel immediate; the world never sees the
        /// click because <see cref="PointerOverHud"/> already claimed it for the UI.
        /// </summary>
        private bool Button(Rect rect, string text, Color fill, Color textColour, float s)
        {
            bool hovered = PointerOver(rect);
            Fill(rect, hovered ? Blend(fill, _palette.HudText, 0.16f) : fill);
            Border(rect, hovered ? _palette.HudAccent : Blend(_palette.HudText, _palette.HudPanel, 0.35f), 1f * s);
            Label(rect, text, textColour, _button);

            if (hovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }

            return false;
        }

        private static Color Blend(Color from, Color to, float t) => Color.Lerp(from, to, t);
    }
}
