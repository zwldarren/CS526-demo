using System.Collections.Generic;
using Facet.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Facet.Game
{
    /// <summary>
    /// The module every view of the simulation extends. It owns the mesh lifecycle - the geometry
    /// buffers, the mesh and its renderer, the screen-space constants, and the rebuild gate - so a
    /// view contributes only the geometry for the current frame.
    ///
    /// The four rebuild reasons in the view layer are all expressed through two hooks. A view calls
    /// <see cref="MarkDirty"/> from <see cref="Observe"/> when its watched value changed - a belt
    /// revision, the Core's health fraction, the cursor's cell, or simply "every frame" - while the
    /// gate owns the one reason that is identical across views: a zoom that moves the pixel size past
    /// a tolerance. The first rebuild happens on the first frame with a real zoom, so a view never
    /// has to synthesize a frame of its own.
    ///
    /// One <see cref="MeshView"/> is one GameObject with one mesh, which is the shape the PlayMode
    /// tests pin: the default (procedural) output is a single mesh whose vertex count they can read.
    /// A content that opts into a Sprite override adds a small pooled SpriteRenderer behind that
    /// mesh, so the default look still touches no GameObject beyond the view's own mesh.
    /// </summary>
    public abstract class MeshView : MonoBehaviour
    {
        /// <summary>Rebuild once the pixel size has moved this far from what the mesh was built at.</summary>
        private const float ZoomTolerance = 0.05f;

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        /// <summary>Scratch for <see cref="AppendChevron"/>, so a view drawing many chevrons in one
        /// frame does not allocate a point array per chevron.</summary>
        private readonly Vector2[] _chevron = new Vector2[3];

        private Mesh _mesh;
        private FrameClock _frames;
        private Palette _palette;
        private bool _dirty;
        private float _builtWorldPerPixel = -1f;

        /// <summary>Sprites this view draws, created lazily and reused across frames.</summary>
        private readonly List<SpriteRenderer> _sprites = new List<SpriteRenderer>();
        private Transform _spriteRoot;
        private int _spritesUsed;

        /// <summary>Sprites sit this far behind the view's mesh, so the mesh's functional overlays
        /// (a turret's barrel, a machine's port stubs) draw on top of an overridden body. Small
        /// enough never to be seen as a parallax offset at any zoom.</summary>
        private const float SpriteDepthOffset = 0.001f;

        /// <summary>The simulation this view draws. The view keeps its own gather and reads
        /// whatever it needs from here; the mesh lifecycle does not depend on it.</summary>
        protected SimWorld World { get; private set; }

        /// <summary>The one asset the whole look lives in: colours, screen-space thicknesses and
        /// every view's layer.</summary>
        protected Palette Colors => _palette;

        /// <summary>Where this view draws: its z-plane and its sorting order within it.</summary>
        protected abstract Palette.Layer Layer { get; }

        /// <summary>Size of one screen pixel in world units at the current resolution and zoom.</summary>
        protected float WorldPerPixel => _frames != null ? _frames.Frame.WorldPerPixel : 0f;

        /// <summary>The palette's screen-space outline constant at the current zoom, in world units.</summary>
        protected float OutlineWidth => _palette != null ? _palette.OutlinePixels * WorldPerPixel : 0f;

        /// <summary>
        /// Build the view: one mesh on this GameObject, on the layer the view declares, with the
        /// shared view material. Nothing is drawn until the first frame rebuilds.
        /// </summary>
        public void Initialize(SimWorld world, FrameClock frames, Palette palette)
        {
            World = world;
            _frames = frames;
            _palette = palette;

            _mesh = new Mesh { name = "FACET/" + gameObject.name };
            AttachRenderer(Layer, palette.ResolveViewMaterial());

            OnInitialized();
            _dirty = true;
        }

        /// <summary>
        /// Attach the MeshFilter + MeshRenderer pair this view draws through, configured the one way
        /// that is correct for flat unlit geometry, and put the object on the layer's z-plane and
        /// sorting order. Tile coordinates are world coordinates in FACET, so a view on the wrong
        /// z-plane drifts away from what the cursor clicks on.
        /// </summary>
        private void AttachRenderer(Palette.Layer layer, Material material)
        {
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = layer.SortingOrder;

            gameObject.transform.position = new Vector3(0f, 0f, layer.Z);
        }

        /// <summary>One-time setup a view needs beyond the mesh. The world and palette are ready.
        /// Called once per view; a map change goes through <see cref="Rebind"/> instead.</summary>
        protected virtual void OnInitialized() { }

        /// <summary>
        /// Point this view at a different simulation - a different map - without rebuilding any of the
        /// mesh machinery: the mesh, the material and the pooled sprites stay, and only what they are
        /// filled from changes. The next frame rebuilds from the new world.
        ///
        /// A view that cached anything sized or positioned by the old map drops it in
        /// <see cref="OnWorldRebound"/>: the grid's own tile grid, the wave-entry markers, the belt
        /// revision it last drew. Those are exactly the caches that would otherwise keep drawing the
        /// previous map, which is why the hook exists rather than <see cref="Initialize"/> being called
        /// a second time.
        /// </summary>
        public void Rebind(SimWorld world)
        {
            World = world ?? throw new System.ArgumentNullException(nameof(world));
            OnWorldRebound();
            _dirty = true;
        }

        /// <summary>Drop everything cached from the previous world. Called by <see cref="Rebind"/>,
        /// so a view with no such cache needs no override.</summary>
        protected virtual void OnWorldRebound() { }

        /// <summary>Called once per frame before the gate. A view compares its own watched value and
        /// calls <see cref="MarkDirty"/> when it changed. The zoom reason is the gate's, not the view's.</summary>
        protected virtual void Observe(in ViewFrame frame) { }

        /// <summary>Add this frame's geometry. The buffers are empty when this is called.</summary>
        protected abstract void AppendFrame(in ViewFrame frame);

        /// <summary>Ask for a rebuild of the mesh. Every-frame views call this from <see cref="Observe"/>.</summary>
        protected void MarkDirty() => _dirty = true;

        /// <summary>World-space centre of a tile. Tile coordinates are world coordinates, so a cell
        /// spans [x, x+1) and its centre is half a tile in from its origin.</summary>
        protected static Vector2 CellCentre(Int2 cell) => new Vector2(cell.X + 0.5f, cell.Y + 0.5f);

        /// <summary>One CCW quad into the shared buffers.</summary>
        protected void AppendQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
            => ProcMesh.AppendQuad(_vertices, _colors, _triangles, a, b, c, d, color);

        /// <summary>One convex CCW polygon, with an optional constant-width outline, into the shared buffers.</summary>
        protected void AppendPolygon(IReadOnlyList<Vector2> points, Vector2 offset,
            Color fill, Color outline, float outlineWidth)
            => ProcMesh.AppendPolygon(_vertices, _colors, _triangles, points, offset, fill, outline, outlineWidth);

        /// <summary>One axis-aligned CCW quad from its min/max corners into the shared buffers.</summary>
        protected void AppendRect(float minX, float minY, float maxX, float maxY, Color color)
            => AppendQuad(new Vector2(minX, minY), new Vector2(maxX, minY),
                new Vector2(maxX, maxY), new Vector2(minX, maxY), color);

        /// <summary>One lane between two points, as a CCW quad of width 2w.</summary>
        protected void AppendLane(Vector2 from, Vector2 to, float halfWidth, Color color)
        {
            Vector2 along = to - from;
            var right = new Vector2(-along.y, along.x).normalized * halfWidth;

            AppendQuad(from - right, from + right, to + right, to - right, color);
        }

        /// <summary>One filled chevron (a triangle) pointing along <paramref name="direction"/>, at
        /// <paramref name="back"/> behind and <paramref name="length"/> ahead of the centre. With the
        /// right-hand point first and the tip last, the winding stays counter-clockwise for all four
        /// directions, so no caller needs a per-direction special case.</summary>
        protected void AppendChevron(Vector2 centre, Dir direction, float back, float halfWidth,
            float length, Color color)
        {
            Vec2 forward2 = direction.ToVec();
            var forward = new Vector2(forward2.X, forward2.Y);
            var right = new Vector2(-forward.y, forward.x);

            _chevron[0] = centre - forward * back + right * halfWidth;
            _chevron[1] = centre - forward * back - right * halfWidth;
            _chevron[2] = centre + forward * length;

            AppendPolygon(_chevron, Vector2.zero, color, color, 0f);
        }

        /// <summary>
        /// The rebuild gate. Ask the view whether it wants a rebuild, and add the one reason every
        /// view shares: the zoom moved the pixel size past the tolerance, so a screen-space constant
        /// stays a true number of pixels. An invalid frame is skipped rather than drawn at zero zoom.
        /// </summary>
        protected virtual void LateUpdate()
        {
            if (_frames == null || _palette == null) return;

            ViewFrame frame = _frames.Frame;
            if (frame.WorldPerPixel <= 0f) return;

            Observe(in frame);
            if (!_dirty && !ZoomMoved(frame.WorldPerPixel)) return;

            _dirty = false;
            _builtWorldPerPixel = frame.WorldPerPixel;

            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();
            AppendFrame(in frame);
            ProcMesh.Finish(_vertices, _colors, _triangles, _mesh.name, _mesh);
        }

        /// <summary>Outline width for a resolved style, in world units, at the current zoom.</summary>
        protected float OutlineWidthFor(VisualStyle style)
            => style != null ? style.OutlinePixels * WorldPerPixel : OutlineWidth;

        /// <summary>Reset the sprite pool cursor before drawing this frame's overrides.</summary>
        protected void BeginSprites() => _spritesUsed = 0;

        /// <summary>
        /// Draw one sprite centred on a world position, scaled so its width and height are
        /// 2 * <paramref name="halfExtent"/> tiles (<paramref name="halfExtent"/> 0 keeps the
        /// sprite's native size), then offset and rotated. A centred sprite pivot is assumed - the
        /// Unity default. Sprites are pooled and live just behind the view's mesh.
        /// </summary>
        protected void DrawSprite(Sprite sprite, Vector2 centre, float halfExtent,
            float rotation, Vector2 offset, Color tint)
        {
            if (sprite == null) return;

            SpriteRenderer renderer = RentSprite();
            renderer.sprite = sprite;
            renderer.color = tint;

            Vector2 native = sprite.bounds.size;
            float scaleX = halfExtent > 0f && native.x > 0f ? (halfExtent * 2f) / native.x : 1f;
            float scaleY = halfExtent > 0f && native.y > 0f ? (halfExtent * 2f) / native.y : 1f;

            renderer.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            renderer.transform.localPosition = new Vector3(centre.x + offset.x, centre.y + offset.y, SpriteDepthOffset);
            renderer.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            renderer.gameObject.SetActive(true);
            _spritesUsed++;
        }

        /// <summary>Draw a style's authored Sprite at a world position with a tint: the short form of
        /// the full call, used wherever a view replaces a body with an image.</summary>
        protected void DrawSprite(VisualStyle style, Vector2 centre, Color tint)
            => DrawSprite(style.Sprite, centre, style.Size, style.Rotation, style.Offset, tint);

        /// <summary>Hide any pooled sprite this frame did not use, so a removed entity stops drawing.
        /// Call after the frame's sprites were drawn.</summary>
        protected void EndSprites()
        {
            for (int i = _spritesUsed; i < _sprites.Count; i++)
            {
                if (_sprites[i].gameObject.activeSelf) _sprites[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Draw one shape icon from a view's icon set: the authored Sprite when the style names one,
        /// otherwise the procedural silhouette with its own outline. A <paramref name="tint"/>
        /// recolours either form - how a silenced turret's ammo goes grey - and null keeps the style's
        /// fill. The one place every view that draws a shape as an icon goes through, so a re-skinned
        /// shape looks the same on a belt, in the ground, on a machine and on an enemy.
        /// </summary>
        protected void AppendIcon(ShapeIconSet icons, ShapeType shape, Vector2 centre, Color? tint)
        {
            if (shape == ShapeType.None) return;

            VisualStyle style = icons.Style(shape);
            if (style == null) return;

            if (style.Source == VisualSource.Sprite)
            {
                DrawSprite(style.Sprite, centre, style.Size, style.Rotation, style.Offset, tint ?? Color.white);
                return;
            }

            Vector2[] points = icons.Points(shape);
            if (points == null) return;

            AppendPolygon(points, centre + style.Offset, tint ?? style.Fill, style.Outline, OutlineWidthFor(style));
        }

        private SpriteRenderer RentSprite()
        {
            while (_spritesUsed >= _sprites.Count)
            {
                if (_spriteRoot == null)
                {
                    var root = new GameObject("Sprites");
                    root.transform.SetParent(transform, false);
                    _spriteRoot = root.transform;
                }

                var go = new GameObject("Sprite");
                go.transform.SetParent(_spriteRoot, false);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = Layer.SortingOrder;
                go.SetActive(false);
                _sprites.Add(renderer);
            }

            return _sprites[_spritesUsed];
        }

        private bool ZoomMoved(float worldPerPixel)
            => _builtWorldPerPixel <= 0f
               || Mathf.Abs(worldPerPixel / _builtWorldPerPixel - 1f) >= ZoomTolerance;
    }
}
