using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every item riding a belt as one mesh, rebuilt each frame because items are always
    /// moving. Positions are interpolated between the last two simulation ticks with the driver's
    /// Alpha, so transport is smooth at any frame rate instead of stepping at 30 Hz.
    ///
    /// Unlike the belts, items do carry an outline, and the shape is the one thing on screen the
    /// player has to identify at a glance. Because the mesh is rebuilt every frame anyway, the
    /// outline width is recomputed from the current zoom each time and stays a true 2 px.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class ItemRenderer : MonoBehaviour
    {
        private const float Z = -0.08f;
        private const int SortingOrder = 0;

        /// <summary>Item radius in tiles. Small enough that a packed belt still reads as separate cells.</summary>
        private const float RadiusTiles = 0.22f;

        private const int CircleSides = 16;

        private readonly List<ItemSnapshot> _items = new List<ItemSnapshot>();
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _tris = new List<int>();

        private SimWorld _world;
        private SimulationDriver _driver;
        private Palette _palette;
        private Mesh _mesh;

        private Vector2[] _triangle;
        private Vector2[] _square;
        private Vector2[] _circle;

        public void Initialize(SimWorld world, SimulationDriver driver, Palette palette)
        {
            _world = world;
            _driver = driver;
            _palette = palette;

            // Distinct silhouettes at a glance: point-up triangle, axis-aligned square, round circle.
            _triangle = ProcMesh.RegularPolygon(3, RadiusTiles, 90f);
            _square = ProcMesh.RegularPolygon(4, RadiusTiles, 45f);
            _circle = ProcMesh.RegularPolygon(CircleSides, RadiusTiles, 0f);

            _mesh = new Mesh { name = "FACET/Items" };

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
            if (_world == null) return;

            _verts.Clear();
            _colors.Clear();
            _tris.Clear();
            _world.Belts.GetItems(_items);

            float alpha = _driver != null ? _driver.Alpha : 1f;
            float outlineWidth = _palette.OutlinePixels * (_driver != null ? _driver.WorldPerPixel : 0.02f);

            for (int i = 0; i < _items.Count; i++)
            {
                ItemSnapshot item = _items[i];
                Vector2[] points = PointsFor(item.Shape);
                if (points == null) continue;

                Vec2 p = Vec2.Lerp(item.PreviousPosition, item.Position, alpha);
                ProcMesh.AppendPolygon(_verts, _colors, _tris, points,
                    new Vector2(p.X, p.Y), _palette.ShapeColor(item.Shape), _palette.Outline, outlineWidth);
            }

            ProcMesh.Finish(_verts, _colors, _tris, "FACET/Items", _mesh);
        }

        private Vector2[] PointsFor(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.Triangle: return _triangle;
                case ShapeType.Square: return _square;
                case ShapeType.Circle: return _circle;
                default: return null;
            }
        }
    }
}
