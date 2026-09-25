using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Facet.Game
{
    /// <summary>
    /// The free camera the design doc's controls call for: WASD or the arrow keys pan, the wheel
    /// zooms, and the view is never allowed to show the void outside the map.
    ///
    /// This reads input itself rather than going through the simulation. Panning is a view concern -
    /// it is not part of the game state, nothing replays it, and routing it through the tick would
    /// make the camera step at 30 Hz for no reason.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class CameraRig : MonoBehaviour
    {
        [Tooltip("How many screenfuls the camera crosses per second. Zoom-independent by design.")]
        [SerializeField] private float panScreensPerSecond = 0.9f;

        [Tooltip("Zoom multiplier applied per wheel notch.")]
        [SerializeField] private float zoomStepPerNotch = 0.88f;

        [SerializeField] private float minZoom = 4f;
        [SerializeField] private float maxZoom = 18f;
        [SerializeField] private float smoothTime = 0.08f;

        private Camera _camera;
        private TileGrid _grid;
        private Vector3 _target;
        private float _targetZoom;
        private Vector3 _panVelocity;
        private float _zoomVelocity;

        public void Initialize(TileGrid grid)
        {
            _camera = GetComponent<Camera>();
            _grid = grid;

            _targetZoom = Mathf.Clamp(_camera.orthographicSize, minZoom, maxZoom);

            // Start centred on the map rather than wherever the scene happened to author the camera.
            _target = Clamp(new Vector3(grid.Width * 0.5f, grid.Height * 0.5f, transform.position.z), _targetZoom);
            transform.position = _target;
            _camera.orthographicSize = _targetZoom;
        }

        private void Update()
        {
            if (_camera == null || _grid == null) return;

            Pan();
            Zoom();
        }

        private void LateUpdate()
        {
            if (_camera == null || _grid == null) return;

            _camera.orthographicSize = Mathf.SmoothDamp(
                _camera.orthographicSize, _targetZoom, ref _zoomVelocity, smoothTime);

            // Clamp against the size we actually have this frame, not the one we are heading for,
            // or the view would overshoot the map edge while a zoom settles.
            transform.position = Vector3.SmoothDamp(
                transform.position, Clamp(_target, _camera.orthographicSize), ref _panVelocity, smoothTime);
        }

        private void Pan()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            float x = 0f;
            float y = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;
            if (x == 0f && y == 0f) return;

            var dir = new Vector2(x, y).normalized;   // so a diagonal pan is not faster than a straight one
            float height = _camera.orthographicSize;
            float width = height * _camera.aspect;
            float step = panScreensPerSecond * Time.deltaTime;

            _target += new Vector3(dir.x * width * step, dir.y * height * step, 0f);
            _target = Clamp(_target, _targetZoom);
        }

        private void Zoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            _targetZoom = Mathf.Clamp(
                _targetZoom * Mathf.Pow(zoomStepPerNotch, WheelNotches(scroll)), minZoom, maxZoom);
            _target = Clamp(_target, _targetZoom);
        }

        /// <summary>
        /// Wheel deltas arrive as 120 per notch on Windows and as 1 on some other backends, so a raw
        /// division would make the wheel dead on one of them. Take whichever reading fits.
        /// </summary>
        private static float WheelNotches(float scroll)
        {
            float notches = Mathf.Abs(scroll) <= 1.5f ? scroll : scroll / 120f;
            return Mathf.Clamp(notches, -3f, 3f);
        }

        private Vector3 Clamp(Vector3 position, float zoom)
        {
            float halfHeight = zoom;
            float halfWidth = zoom * _camera.aspect;

            return new Vector3(
                ClampAxis(position.x, halfWidth, _grid.Width),
                ClampAxis(position.y, halfHeight, _grid.Height),
                position.z);
        }

        /// <summary>Keep the view inside [0,size]; if the map is smaller than the view, centre it.</summary>
        private static float ClampAxis(float value, float halfExtent, int size)
        {
            if (size <= halfExtent * 2f) return size * 0.5f;
            return Mathf.Clamp(value, halfExtent, size - halfExtent);
        }
    }
}
