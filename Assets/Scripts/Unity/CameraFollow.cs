using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Keeps the Rig on screen without ever showing the void outside the map.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private float smoothTime = 0.12f;

        private Camera _camera;
        private Transform _target;
        private TileGrid _grid;
        private Vector3 _velocity;
        private bool _snapped;

        public void Initialize(Transform target, TileGrid grid)
        {
            _camera = GetComponent<Camera>();
            _target = target;
            _grid = grid;
            _snapped = false;
        }

        private void LateUpdate()
        {
            if (_camera == null || _target == null || _grid == null) return;

            if (!_snapped)
            {
                transform.position = Desired(_target.position);
                _snapped = true;
                return;
            }

            transform.position = Vector3.SmoothDamp(transform.position, Desired(_target.position), ref _velocity, smoothTime);
        }

        private Vector3 Desired(Vector3 targetPosition)
        {
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;

            return new Vector3(
                ClampAxis(targetPosition.x, halfWidth, _grid.Width),
                ClampAxis(targetPosition.y, halfHeight, _grid.Height),
                transform.position.z);
        }

        /// <summary>Keep the view inside [0,size]; if the map is smaller than the view, centre it.</summary>
        private static float ClampAxis(float value, float halfExtent, int size)
        {
            if (size <= halfExtent * 2f) return size * 0.5f;
            return Mathf.Clamp(value, halfExtent, size - halfExtent);
        }
    }
}
