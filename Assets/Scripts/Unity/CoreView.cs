using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The defended object: one flat block covering its whole <see cref="CoreState.Size"/> x
    /// <see cref="CoreState.Size"/> footprint, with a dark emblem in the middle that tints toward the
    /// damage colour as HP falls - one shape carrying both "this is the thing you lose on" and "how
    /// close you are to losing", which is the second half of the design doc's art rule.
    ///
    /// The Core is the one view whose rebuild reason is a watched value rather than a revision or the
    /// clock: it redraws when its cell or its health fraction changes, and not otherwise. The
    /// geometry, which is static, is built once in <see cref="OnInitialized"/>.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CoreView : MeshView
    {
        private Vector2[] _block;
        private Vector2[] _emblem;

        private Int2 _seenCell = new Int2(int.MinValue, int.MinValue);
        private float _seenFraction = -1f;

        protected override Palette.Layer Layer => Colors.CoreLayer;

        protected override void OnInitialized()
        {
            // Counter-clockwise, which ProcMesh requires of every polygon.
            float size = CoreState.Size;
            float inset = Colors.CoreBlock.Inset;
            _block = new[]
            {
                new Vector2(inset, inset),
                new Vector2(size - inset, inset),
                new Vector2(size - inset, size - inset),
                new Vector2(inset, size - inset),
            };

            float emblemTiles = Colors.CoreBlock.EmblemTiles;
            float min = size * 0.5f - emblemTiles * 0.5f;
            float max = size * 0.5f + emblemTiles * 0.5f;
            _emblem = new[]
            {
                new Vector2(min, min),
                new Vector2(max, min),
                new Vector2(max, max),
                new Vector2(min, max),
            };
        }

        protected override void Observe(in ViewFrame frame)
        {
            CoreState core = World.Core;
            if (core.Cell == _seenCell && Mathf.Abs(core.HealthFraction - _seenFraction) < 0.001f) return;

            _seenCell = core.Cell;
            _seenFraction = core.HealthFraction;
            MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            CoreState core = World.Core;
            var origin = new Vector2(core.Cell.X, core.Cell.Y);
            Color fill = Color.Lerp(Colors.CoreDamage, Colors.Core, core.HealthFraction);
            Color emblem = Color.Lerp(Colors.CoreDamage, Colors.Outline, core.HealthFraction);

            AppendPolygon(_block, origin, fill, Colors.Outline, OutlineWidth);

            // No outline of its own: it sits inside the block, and a second outline there would read
            // as a seam in the building.
            AppendPolygon(_emblem, origin, emblem, emblem, 0f);
        }
    }
}
