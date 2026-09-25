using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The defended object. Drawn as a hexagon that tints toward the damage colour as its HP falls -
    /// one shape carrying both "this is the thing you lose on" and "how close you are to losing",
    /// which is the second half of the design doc's art rule.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CoreView : MonoBehaviour
    {
        private const float Z = -0.06f;
        private const int SortingOrder = 0;
        private const float RadiusTiles = 0.46f;

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _tris = new List<int>();

        private SimWorld _world;
        private SimulationDriver _driver;
        private Palette _palette;
        private Mesh _mesh;
        private Vector2[] _hexagon;

        private float _builtFraction = -1f;
        private Int2 _builtCell = new Int2(int.MinValue, int.MinValue);

        public void Initialize(SimWorld world, SimulationDriver driver, Palette palette)
        {
            _world = world;
            _driver = driver;
            _palette = palette;
            _hexagon = ProcMesh.RegularPolygon(6, RadiusTiles, 0f);

            _mesh = new Mesh { name = "FACET/Core" };

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

            Rebuild();
        }

        private void LateUpdate()
        {
            if (_world == null) return;

            CoreState core = _world.Core;
            if (core.Cell == _builtCell && Mathf.Abs(core.HealthFraction - _builtFraction) < 0.001f) return;

            Rebuild();
        }

        private void Rebuild()
        {
            CoreState core = _world.Core;
            _builtCell = core.Cell;
            _builtFraction = core.HealthFraction;

            _verts.Clear();
            _colors.Clear();
            _tris.Clear();

            Vec2 center = _world.TileGrid.CellCenter(core.Cell);
            Color fill = Color.Lerp(_palette.CoreDamage, _palette.Core, core.HealthFraction);
            float outlineWidth = _palette.OutlinePixels * (_driver != null ? _driver.WorldPerPixel : 0.02f);

            ProcMesh.AppendPolygon(_verts, _colors, _tris, _hexagon,
                new Vector2(center.X, center.Y), fill, _palette.Outline, outlineWidth);

            ProcMesh.Finish(_verts, _colors, _tris, "FACET/Core", _mesh);
        }
    }
}
