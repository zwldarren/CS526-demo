using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every enemy plus the map's wave entry points as one mesh, rebuilt each frame because
    /// enemies are always moving. Positions are interpolated between the last two simulation ticks
    /// with the frame's Alpha, so a wave walks smoothly at any frame rate instead of stepping.
    ///
    /// Two of the design doc's art rules land here. "Colour says which side it is on, polygon count
    /// says how damaged it is": the body is a 3..6-sided polygon that loses a side for each chunk of
    /// health, and the four possible bodies are cached once instead of regenerated per enemy per
    /// frame. And every enemy wears its own weakness as an icon, in the exact colour that ammo rides
    /// the belts in, so the counter-pick is one glance rather than a trip through the codex.
    /// </summary>
    [DefaultExecutionOrder(112)]
    public sealed class EnemyRenderer : MeshView
    {
        private readonly List<EnemySnapshot> _enemies = new List<EnemySnapshot>();

        /// <summary>Bodies indexed by side count minus three: [0]=3 sides (dead-ish) .. [3]=6 sides (full health).</summary>
        private readonly Vector2[][] _bodies = new Vector2[4][];

        private ShapeOutlines _weaknessIcons;

        /// <summary>Reused per frame: which spawn points the NEXT wave walks in from. Sized to the
        /// map at initialization, since the map is what owns its entry points.</summary>
        private bool[] _nextWaveSpawns;

        protected override Palette.Layer Layer => Colors.EnemyLayer;

        protected override void OnInitialized()
        {
            // First vertex at 90 degrees puts the "missing" side of a triangle at the bottom, so a
            // damaged enemy reads as worn down from below - and a 6-sided body reads as a hexagon
            // pointing up, distinct from a 4-sided one at any rotation.
            for (int sides = 3; sides <= 6; sides++)
                _bodies[sides - 3] = ProcMesh.RegularPolygon(sides, Colors.Enemies.BodyRadius, 90f);

            // Same silhouettes as the items, so a weakness icon and the ammo that counters it are
            // recognisably the same shape.
            _weaknessIcons = ShapeOutlines.AtRadius(Colors.Enemies.WeaknessRadius, Colors.CircleSides);

            _nextWaveSpawns = new bool[World.Map.SpawnPoints.Length];
        }

        /// <summary>Enemies are always moving, so this view rebuilds every frame.</summary>
        protected override void Observe(in ViewFrame frame) => MarkDirty();

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Enemies.GetEnemies(_enemies);
            float outlineWidth = OutlineWidth;

            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemySnapshot enemy = _enemies[i];

                int sides = Mathf.Clamp(3 + Mathf.RoundToInt(3f * enemy.HealthFraction), 3, 6);
                Vec2 p = Vec2.Lerp(enemy.PreviousPosition, enemy.Position, frame.Alpha);
                var at = new Vector2(p.X, p.Y);

                // Red outline only while it is actually hurting the Core - damage in progress,
                // not damage taken.
                Color outline = enemy.Attacking ? Colors.Jam : Colors.Outline;
                AppendPolygon(_bodies[sides - 3], at,
                    Colors.EnemyColor(enemy.Kind), outline, outlineWidth);

                // No outline on the weakness icon: an outlined shape inside an outlined body is
                // noise, and the fill colour already matches the ammo it is weak to.
                Vector2[] weak = _weaknessIcons[enemy.Weakness];
                if (weak != null)
                    AppendPolygon(weak, at, Colors.ShapeColor(enemy.Weakness), Colors.Outline, 0f);
            }

            AppendSpawnMarkers();
        }

        /// <summary>
        /// The four wave entry points, each as a hollow frame built from one alpha value this frame.
        /// While a wave runs the frames sit at a steady faint alpha; during an intermission they
        /// pulse with the countdown so the map itself says "something is coming". The points the
        /// NEXT wave uses additionally get a solid inner quad, which is the wave preview drawn
        /// where the player is already looking.
        /// </summary>
        private void AppendSpawnMarkers()
        {
            WaveDirector waves = World.Waves;

            Palette.EnemyLook look = Colors.Enemies;
            float frameAlpha;
            if (waves.IntermissionTotal > 0f)
            {
                // IntermissionTotal > 0 is the single source of truth for "counting down";
                // CurrentWave alone is 0 both before the first wave and right after one clears, and
                // the first wave has no countdown at all - it waits for the player (WaveDirector), so
                // its frames sit steady instead of pulsing at a clock that is not running.
                float phase = 1f - waves.IntermissionRemaining / waves.IntermissionTotal;
                frameAlpha = look.SpawnPulseCenter + look.SpawnPulseAmplitude * Mathf.Sin(phase * Mathf.PI * 2f * look.SpawnPulseCycles);
            }
            else
            {
                frameAlpha = look.SpawnWaveRunningAlpha;
            }

            for (int i = 0; i < _nextWaveSpawns.Length; i++) _nextWaveSpawns[i] = false;
            WaveDefinition next = waves.NextWaveDefinition;
            if (next != null)
                for (int g = 0; g < next.Groups.Length; g++)
                    _nextWaveSpawns[next.Groups[g].SpawnPoint % _nextWaveSpawns.Length] = true;

            Color frame = Colors.SpawnMarker;
            frame.a = frameAlpha;

            Color preview = Colors.SpawnMarker;
            preview.a = look.SpawnPreviewAlpha;

            for (int i = 0; i < World.Map.SpawnPoints.Length; i++)
            {
                Int2 cell = World.Map.SpawnPoints[i];
                float minX = cell.X + look.SpawnFrameInset;
                float minY = cell.Y + look.SpawnFrameInset;
                float maxX = cell.X + 1f - look.SpawnFrameInset;
                float maxY = cell.Y + 1f - look.SpawnFrameInset;
                float thickness = look.SpawnFrameThickness;

                AppendRect(minX, minY, maxX, minY + thickness, frame);              // bottom
                AppendRect(minX, maxY - thickness, maxX, maxY, frame);              // top
                AppendRect(minX, minY + thickness, minX + thickness, maxY - thickness, frame); // left
                AppendRect(maxX - thickness, minY + thickness, maxX, maxY - thickness, frame); // right

                if (_nextWaveSpawns[i])
                    AppendRect(cell.X + look.SpawnInnerMarkerInset, cell.Y + look.SpawnInnerMarkerInset,
                        cell.X + 1f - look.SpawnInnerMarkerInset, cell.Y + 1f - look.SpawnInnerMarkerInset, preview);
            }
        }
    }
}
