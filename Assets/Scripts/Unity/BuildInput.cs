using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Facet.Game
{
    /// <summary>
    /// Translates raw device state into one <see cref="InputCommand"/> per tick, plus the one-shot
    /// toggles the driver consumes. Not a MonoBehaviour: the driver owns it and polls it, so there
    /// is no script execution-order race between reading input and advancing the simulation.
    /// This project has only the new Input System enabled - never use UnityEngine.Input here.
    /// </summary>
    public sealed class BuildInput
    {
        private bool _pauseToggleRequested;
        private bool _debugSpawnRequested;

        /// <summary>True exactly once after P was pressed. Cleared by the call.</summary>
        public bool ConsumePauseToggle()
        {
            bool value = _pauseToggleRequested;
            _pauseToggleRequested = false;
            return value;
        }

        /// <summary>
        /// True exactly once after T was pressed. Cleared by the call.
        /// TEMPORARY: drills arrive in the next step of the build, so until then this is the only
        /// source of items and the only way to watch transport by eye. Delete the key when the
        /// first drill lands - keep <c>BeltField.TrySpawnItem</c>, which the drill will use.
        /// </summary>
        public bool ConsumeDebugSpawn()
        {
            bool value = _debugSpawnRequested;
            _debugSpawnRequested = false;
            return value;
        }

        public InputCommand Poll(Camera camera, SimWorld world)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // The design doc gives Space to "start the next wave", so pause lives on P rather
                // than training the wrong reflex now and moving it later.
                if (keyboard.pKey.wasPressedThisFrame) _pauseToggleRequested = true;
                if (keyboard.tKey.wasPressedThisFrame) _debugSpawnRequested = true;
            }

            bool build = false;
            bool remove = false;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                build = mouse.leftButton.isPressed;
                remove = mouse.rightButton.isPressed;
            }

            return new InputCommand(build, remove, CursorCell(camera, world));
        }

        /// <summary>Tile under the mouse cursor, or (-1,-1) when there is no camera / no mouse.</summary>
        private static Int2 CursorCell(Camera camera, SimWorld world)
        {
            var mouse = Mouse.current;
            if (camera == null || mouse == null) return new Int2(-1, -1);

            Vector3 screen = mouse.position.ReadValue();
            screen.z = -camera.transform.position.z;
            Vector3 worldPos = camera.ScreenToWorldPoint(screen);
            return world.TileGrid.CellAt(new Vec2(worldPos.x, worldPos.y));
        }
    }
}
