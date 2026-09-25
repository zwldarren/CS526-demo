using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The ghost that shows where the next belt would land, and whether it would be allowed there.
    /// Green means buildable, red means something is already there - which is the feedback that
    /// makes dragging a run across an existing build predictable rather than surprising.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class CursorView : MonoBehaviour
    {
        private const float Z = -0.2f;
        private const int SortingOrder = 10;

        private const float FillAlpha = 0.16f;
        private const float EdgeAlpha = 0.85f;
        private const float EdgeThickness = 0.05f;

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _tris = new List<int>();

        private SimWorld _world;
        private SimulationDriver _driver;
        private Palette _palette;
        private Mesh _mesh;

        private Int2 _builtCell = new Int2(int.MinValue, int.MinValue);
        private bool _builtValid;
        private bool _builtVisible;

        public void Initialize(SimWorld world, SimulationDriver driver, Palette palette)
        {
            _world = world;
            _driver = driver;
            _palette = palette;

            _mesh = new Mesh { name = "FACET/Cursor" };

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ProcMesh.UnlitMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.sortingOrder = SortingOrder;

            transform.position = new Vector3(0f, 0f, Z);
        }

        private void LateUpdate()
        {
            if (_world == null || _driver == null) return;

            Int2 cell = _driver.CursorCell;
            bool visible = _world.TileGrid.InBounds(cell);
            bool valid = visible && _world.CanPlaceBelt(cell);

            if (visible == _builtVisible && cell == _builtCell && valid == _builtValid) return;

            _builtCell = cell;
            _builtValid = valid;
            _builtVisible = visible;
            Rebuild();
        }

        private void Rebuild()
        {
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();

            if (_builtVisible)
            {
                var x = (float)_builtCell.X;
                var y = (float)_builtCell.Y;
                Color edge = _builtValid ? _palette.CursorOk : _palette.CursorBlocked;
                Color fill = edge;
                fill.a = FillAlpha;
                edge.a = EdgeAlpha;

                ProcMesh.AppendQuad(_verts, _colors, _tris,
                    new Vector2(x, y), new Vector2(x + 1f, y), new Vector2(x + 1f, y + 1f), new Vector2(x, y + 1f), fill);

                float t = EdgeThickness;
                ProcMesh.AppendQuad(_verts, _colors, _tris,
                    new Vector2(x, y), new Vector2(x + 1f, y), new Vector2(x + 1f, y + t), new Vector2(x, y + t), edge);
                ProcMesh.AppendQuad(_verts, _colors, _tris,
                    new Vector2(x, y + 1f - t), new Vector2(x + 1f, y + 1f - t),
                    new Vector2(x + 1f, y + 1f), new Vector2(x, y + 1f), edge);
                ProcMesh.AppendQuad(_verts, _colors, _tris,
                    new Vector2(x, y), new Vector2(x + t, y), new Vector2(x + t, y + 1f), new Vector2(x, y + 1f), edge);
                ProcMesh.AppendQuad(_verts, _colors, _tris,
                    new Vector2(x + 1f - t, y), new Vector2(x + 1f, y),
                    new Vector2(x + 1f, y + 1f), new Vector2(x + 1f - t, y + 1f), edge);
            }

            ProcMesh.Finish(_verts, _colors, _tris, "FACET/Cursor", _mesh);
        }
    }
}
