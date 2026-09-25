using System.Collections.Generic;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Builds flat, vertex-coloured meshes at runtime. No textures, no gradients: every FACET visual
    /// is a flat fill plus a constant-width dark outline. All polygons must be convex and wound
    /// counter-clockwise.
    ///
    /// The Append* + <see cref="Finish"/> trio exists because a few hundred belts or items have to
    /// land in ONE mesh; building a mesh per object would mean a GameObject per belt.
    /// </summary>
    public static class ProcMesh
    {
        private static Material _unlit;

        /// <summary>Shared unlit material that respects per-vertex colour.</summary>
        public static Material UnlitMaterial()
        {
            if (_unlit != null) return _unlit;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            _unlit = new Material(shader) { name = "FACET/UnlitVertexColor", hideFlags = HideFlags.DontSave };
            return _unlit;
        }

        /// <summary>Vertices of a regular polygon, counter-clockwise, first vertex at <paramref name="startAngleDeg"/>.</summary>
        public static Vector2[] RegularPolygon(int sides, float radius, float startAngleDeg = 0f)
        {
            if (sides < 3) sides = 3;
            var pts = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (startAngleDeg + 360f * i / sides) * Mathf.Deg2Rad;
                pts[i] = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
            }
            return pts;
        }

        /// <summary>
        /// Drop one polygon into shared vertex/colour/index buffers, optionally displaced by
        /// <paramref name="offset"/>. This is what lets a renderer pack every belt or every item
        /// into a single mesh instead of one GameObject per object.
        /// The outline is built as one quad per edge rather than by scaling the polygon outward,
        /// so its width stays constant no matter how many sides the shape has.
        /// </summary>
        public static void AppendPolygon(List<Vector3> verts, List<Color> colors, List<int> tris,
            IReadOnlyList<Vector2> points, Vector2 offset, Color fill, Color outline, float outlineWidth)
        {
            int n = points.Count;
            if (n < 3) return;

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < n; i++) centroid += points[i];
            centroid = centroid / n + offset;

            // Fill: triangle fan from the centroid (valid because polygons are convex).
            for (int i = 0; i < n; i++)
            {
                int i0 = verts.Count;
                verts.Add(centroid);
                verts.Add(points[i] + offset);
                verts.Add(points[(i + 1) % n] + offset);
                colors.Add(fill);
                colors.Add(fill);
                colors.Add(fill);
                tris.Add(i0);
                tris.Add(i0 + 1);
                tris.Add(i0 + 2);
            }

            if (outlineWidth > 0f && outline.a > 0f)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[(i + 1) % n];
                    Vector2 d = b - a;
                    if (d.sqrMagnitude <= 1e-12f) continue;
                    // Outward normal of a CCW polygon edge.
                    var nrm = new Vector2(d.y, -d.x).normalized * outlineWidth;

                    int i0 = verts.Count;
                    verts.Add(a + offset);
                    verts.Add(b + offset);
                    verts.Add(b + nrm + offset);
                    verts.Add(a + nrm + offset);
                    colors.Add(outline);
                    colors.Add(outline);
                    colors.Add(outline);
                    colors.Add(outline);
                    tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                    tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
                }
            }
        }

        /// <summary>
        /// A single mesh holding every line of a W x H tile grid, drawn as thin quads.
        /// Built once - the grid never changes shape.
        /// </summary>
        public static Mesh LineGrid(int width, int height, float lineWidth, Color color, string name = "FACET/Grid")
        {
            var verts = new List<Vector3>(((width + 1) + (height + 1)) * 4);
            var colors = new List<Color>(verts.Capacity);
            var tris = new List<int>(verts.Capacity * 6);

            float h = lineWidth * 0.5f;
            if (h < 0.0005f) h = 0.0005f;

            for (int x = 0; x <= width; x++)
            {
                float cx = x;
                AppendQuad(verts, colors, tris,
                    new Vector2(cx - h, 0f), new Vector2(cx + h, 0f),
                    new Vector2(cx + h, height), new Vector2(cx - h, height), color);
            }

            for (int y = 0; y <= height; y++)
            {
                float cy = y;
                AppendQuad(verts, colors, tris,
                    new Vector2(0f, cy - h), new Vector2(width, cy - h),
                    new Vector2(width, cy + h), new Vector2(0f, cy + h), color);
            }

            return Finish(verts, colors, tris, name);
        }

        /// <summary>Quad pushed into shared buffers. Four corners, CCW order.</summary>
        public static void AppendQuad(List<Vector3> verts, List<Color> colors, List<int> tris,
            Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int i0 = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            verts.Add(d);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
            tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
        }

        /// <summary>Turn shared buffers into a mesh. Reuses <paramref name="into"/> when given, so a
        /// renderer that rebuilds every frame does not allocate a new mesh each time.</summary>
        public static Mesh Finish(List<Vector3> verts, List<Color> colors, List<int> tris, string name, Mesh into = null)
        {
            Mesh mesh = into != null ? into : new Mesh();
            mesh.name = name;
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
