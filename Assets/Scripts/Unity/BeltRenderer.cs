using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every belt cell as one mesh. The bed only changes when a belt is laid, re-pointed or
    /// jammed, so this rebuilds off the simulation's revision counter rather than every frame.
    ///
    /// That is also why belts carry no outline: an outline is baked at a fixed pixel width, so it
    /// would be wrong at every zoom but the one it was built at. The tile grid already separates
    /// the cells, and the chevron is what actually has to be read, so nothing is lost.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class BeltRenderer : MonoBehaviour
    {
        private const float Z = -0.05f;
        private const int SortingOrder = -5;

        /// <summary>Inset so the tile grid still reads between neighbouring belts.</summary>
        private const float Inset = 0.08f;

        private const float ChevronLength = 0.20f;
        private const float ChevronBack = 0.12f;
        private const float ChevronHalfWidth = 0.18f;

        private readonly List<BeltSnapshot> _belts = new List<BeltSnapshot>();
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _tris = new List<int>();

        private SimWorld _world;
        private Palette _palette;
        private Mesh _mesh;
        private int _builtRevision = -1;

        public void Initialize(SimWorld world, Palette palette)
        {
            _world = world;
            _palette = palette;

            _mesh = new Mesh { name = "FACET/Belts" };

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
            if (_world == null || _world.Belts.Revision == _builtRevision) return;

            _builtRevision = _world.Belts.Revision;
            Rebuild();
        }

        private void Rebuild()
        {
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();
            _world.Belts.GetBelts(_belts);

            for (int i = 0; i < _belts.Count; i++)
            {
                BeltSnapshot belt = _belts[i];
                float x = belt.Cell.X;
                float y = belt.Cell.Y;

                ProcMesh.AppendQuad(_verts, _colors, _tris,
                    new Vector2(x + Inset, y + Inset),
                    new Vector2(x + 1f - Inset, y + Inset),
                    new Vector2(x + 1f - Inset, y + 1f - Inset),
                    new Vector2(x + Inset, y + 1f - Inset),
                    belt.Jammed ? _palette.Jam : _palette.Belt);

                AppendChevron(belt);
            }

            ProcMesh.Finish(_verts, _colors, _tris, "FACET/Belts", _mesh);
        }

        private void AppendChevron(BeltSnapshot belt)
        {
            Vec2 forward2 = belt.Direction.ToVec();
            var forward = new Vector2(forward2.X, forward2.Y);
            var right = new Vector2(-forward.y, forward.x);
            var center = new Vector2(belt.Cell.X + 0.5f, belt.Cell.Y + 0.5f);

            // Order the three points so the triangle is wound CCW, which Polygon requires. With
            // tip last and right-hand first, (b - a) x (tip - a) stays positive for all four
            // cardinal directions, so no per-direction special case is needed.
            var a = center - forward * ChevronBack + right * ChevronHalfWidth;
            var b = center - forward * ChevronBack - right * ChevronHalfWidth;
            var tip = center + forward * ChevronLength;

            Color color = belt.Jammed ? _palette.Outline : _palette.BeltArrow;
            ProcMesh.AppendPolygon(_verts, _colors, _tris, new[] { a, b, tip }, Vector2.zero, color, color, 0f);
        }
    }
}
