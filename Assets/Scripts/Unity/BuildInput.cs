using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Facet.Game
{
    /// <summary>
    /// Translates raw device state into one <see cref="InputCommand"/> per tick, plus the pause toggle
    /// the driver consumes itself.
    ///
    /// The one-shot fields are latched here rather than sampled at tick time. The simulation runs at
    /// 30 Hz and the screen refreshes faster than that, so a click can go down and up between two
    /// ticks - and one frame can run several catch-up ticks, so a press must not be delivered to all
    /// of them. A press is remembered until a tick consumes it (<see cref="ConsumeOneShots"/>) and the
    /// held state is read fresh every frame.
    ///
    /// Not a MonoBehaviour: the driver owns it and drives it, so there is no script execution-order
    /// race between reading input and advancing the simulation.
    /// This project has only the new Input System enabled - never use UnityEngine.Input here.
    /// </summary>
    public sealed class BuildInput
    {
        /// <summary>The digits that select a building, in <see cref="BuildCatalog.All"/> order. There
        /// is one more digit than there are kinds; the loop below stops at the table's length, so the
        /// spare keys are inert until content grows into them.</summary>
        private static readonly Key[] BuildKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6,
            Key.Digit7, Key.Digit8, Key.Digit9,
        };

        private bool _pauseToggleRequested;
        private bool _startWaveRequested;
        private bool _restartRequested;
        private bool _primaryPressed;
        private bool _primaryReleased;
        private int _rotateSteps;

        private bool _buildHeld;
        private bool _removeHeld;
        private bool _wasBuildHeld;

        private BuildKind _selected = BuildKind.Belt;
        private Int2 _cursor = new Int2(-1, -1);

        /// <summary>True exactly once after P was pressed. Cleared by the call.</summary>
        public bool ConsumePauseToggle()
        {
            bool value = _pauseToggleRequested;
            _pauseToggleRequested = false;
            return value;
        }

        /// <summary>Is N being asked for right now? Read by the driver as "go on" - start the next
        /// wave while a map is live, carry on to the next map once it is won - and by the world as
        /// "start the next wave". Non-destructive because both readers act on the same press in
        /// different states, and only the tick's consumer should clear it.</summary>
        public bool StartWaveRequested => _startWaveRequested;

        /// <summary>Read every device once per frame: held state, latched one-shots, selection, cursor.
        /// <paramref name="pointerOverHud"/> is the HUD's "the mouse is mine right now": while it is
        /// true the buttons do not reach the world at all, so clicking a HUD button cannot also drop a
        /// belt under the panel.</summary>
        public void Sample(Camera camera, SimWorld world, bool pointerOverHud = false)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                for (int i = 0; i < BuildKeys.Length && i < BuildCatalog.Count; i++)
                    if (keyboard[BuildKeys[i]].wasPressedThisFrame) _selected = BuildCatalog.All[i];

                // Q/E turn the ghost the way a factory game does. Rotation is a request, not a state:
                // it is latched so that turning the ghost twice between two ticks turns it twice.
                if (keyboard.qKey.wasPressedThisFrame) _rotateSteps--;
                if (keyboard.eKey.wasPressedThisFrame) _rotateSteps++;

                // Space pauses - the reflex every player already has - and P does the same for anyone
                // who learned it here first. Starting a wave moved off Space entirely: it is N, or the
                // HUD's button, so "skip the countdown" is always a deliberate act.
                if (keyboard.spaceKey.wasPressedThisFrame) _pauseToggleRequested = true;
                if (keyboard.pKey.wasPressedThisFrame) _pauseToggleRequested = true;
                if (keyboard.nKey.wasPressedThisFrame) _startWaveRequested = true;
                if (keyboard.rKey.wasPressedThisFrame) _restartRequested = true;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                bool held = mouse.leftButton.isPressed && !pointerOverHud;

                // A release is either reported by the device or inferred from the button going up
                // unannounced (a lost focus, a WebGL canvas that stopped receiving events), so a
                // machine placement can never be left pending forever. Over the HUD the held flags go
                // to false, which is the release that ends a drag the player started outside the panel.
                if (mouse.leftButton.wasPressedThisFrame && !pointerOverHud) _primaryPressed = true;
                if (mouse.leftButton.wasReleasedThisFrame || (_wasBuildHeld && !held)) _primaryReleased = true;

                _wasBuildHeld = held;
                _buildHeld = held;
                _removeHeld = mouse.rightButton.isPressed && !pointerOverHud;
            }

            _cursor = CursorCell(camera, world);
        }

        /// <summary>The command the next tick should apply.</summary>
        public InputCommand Snapshot()
            => new InputCommand(_buildHeld, _removeHeld, _cursor, _primaryPressed, _primaryReleased,
                _selected, _rotateSteps, _startWaveRequested, _restartRequested);

        /// <summary>
        /// The HUD's build bar asking for a selection. It writes the same field a number key writes,
        /// so the next <see cref="Snapshot"/> carries it in <see cref="InputCommand.Selected"/> and
        /// the simulation stays the one place the selection is actually applied. The world applies it
        /// even while paused, so the highlighted tile follows the click immediately.
        /// </summary>
        public void RequestSelect(BuildKind kind) => _selected = kind;

        /// <summary>The HUD's rotate buttons asking for quarter turns of the placement ghost, latched
        /// exactly like the Q/E keys so two clicks between ticks turn the ghost twice.</summary>
        public void RequestRotate(int steps) => _rotateSteps += steps;

        /// <summary>Drop the one-shots, once a tick has actually applied them.</summary>
        public void ConsumeOneShots()
        {
            _primaryPressed = false;
            _primaryReleased = false;
            _rotateSteps = 0;
            _startWaveRequested = false;
            _restartRequested = false;
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
