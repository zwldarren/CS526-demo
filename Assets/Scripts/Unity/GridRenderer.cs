using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws the tile grid as one flat mesh. Built once in <see cref="Initialize"/> -
    /// the grid never changes shape, so there is nothing to rebuild.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class GridRenderer : MonoBehaviour
    {
        private const float Z = 0f;
        private const int SortingOrder = -10;

        private MeshFilter _filter;
        private MeshRenderer _renderer;

        public void Initialize(TileGrid grid, Palette palette, float worldPerPixel)
        {
            Mesh mesh = ProcMesh.LineGrid(grid.Width, grid.Height, Mathf.Max(worldPerPixel, 0.0005f), palette.GridLine);

            _filter = gameObject.AddComponent<MeshFilter>();
            _filter.sharedMesh = mesh;

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = ProcMesh.UnlitMaterial();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.sortingOrder = SortingOrder;

            transform.position = new Vector3(0f, 0f, Z);
        }
    }
}
