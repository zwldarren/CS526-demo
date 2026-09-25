using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws the Rig as a procedurally-built hexagon and drives its transform from the
    /// simulation, interpolating between the last two ticks so movement is smooth at any
    /// frame rate instead of stepping at 30 Hz.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class RigView : MonoBehaviour
    {
        /// <summary>Rig body radius in tiles (matches SimConfig.RigRadius).</summary>
        private const float RadiusTiles = 0.42f;

        private const float Z = -0.1f;
        private const int SortingOrder = 0;

        private SimWorld _world;
        private SimulationDriver _driver;
        private float _angleDeg;
        private float _angleVelocity;

        public void Initialize(SimWorld world, SimulationDriver driver, Palette palette, float worldPerPixel)
        {
            _world = world;
            _driver = driver;

            float outlineWidth = palette.OutlinePixels * worldPerPixel;
            // Pointy-right hexagon: the first vertex sits on +X, so rotating by the facing
            // angle points a corner down the direction of travel.
            Vector2[] hexagon = ProcMesh.RegularPolygon(6, RadiusTiles, 0f);
            Mesh mesh = ProcMesh.Polygon(hexagon, palette.RigFill, palette.Outline, outlineWidth, "FACET/Rig");

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ProcMesh.UnlitMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.sortingOrder = SortingOrder;

            _angleDeg = 0f;
            Apply(1f);
        }

        private void LateUpdate()
        {
            Apply(_driver != null ? _driver.Alpha : 1f);
        }

        private void Apply(float alpha)
        {
            RigState rig = _world.Rig;

            Vec2 p = Vec2.Lerp(rig.PreviousPosition, rig.Position, alpha);
            transform.position = new Vector3(p.X, p.Y, Z);

            if (rig.Facing.SqrMagnitude > 1e-6f)
            {
                float target = Mathf.Atan2(rig.Facing.Y, rig.Facing.X) * Mathf.Rad2Deg;
                // Exponential smoothing, frame-rate independent.
                _angleDeg = Mathf.SmoothDampAngle(_angleDeg, target, ref _angleVelocity, 0.06f, Mathf.Infinity, Time.deltaTime);
            }

            transform.rotation = Quaternion.Euler(0f, 0f, _angleDeg);
        }
    }
}
