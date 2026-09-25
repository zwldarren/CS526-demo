using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Facet.Game
{
    /// <summary>
    /// Translates raw device state into one <see cref="InputCommand"/> per tick.
    /// Not a MonoBehaviour: the driver owns it and polls it, so there is no script
    /// execution-order race between reading input and advancing the simulation.
    /// This project has only the new Input System enabled - never use UnityEngine.Input here.
    /// </summary>
    public sealed class RigInput
    {
        private bool _pauseToggleRequested;

        /// <summary>True exactly once after Space was pressed. Cleared by the call.</summary>
        public bool ConsumePauseToggle()
        {
            bool value = _pauseToggleRequested;
            _pauseToggleRequested = false;
            return value;
        }

        public InputCommand Poll(Camera camera, SimWorld world)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                _pauseToggleRequested = true;

            Vec2 move = Vec2.Zero;
            if (keyboard != null)
            {
                float x = 0f;
                float y = 0f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;
                move = new Vec2(x, y);
            }

            bool build = false;
            bool remove = false;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                build = mouse.leftButton.isPressed;
                remove = mouse.rightButton.isPressed;
            }

            return new InputCommand(move, build, remove, CursorCell(camera, world));
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
