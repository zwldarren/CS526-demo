using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Facet.Game
{
    /// <summary>
    /// Translates raw device state into one <see cref="InputCommand"/> per tick, plus the pause toggle
    /// the driver consumes itself.
    ///
    /// The one-shot fields are latched here rather than sampled at tick time: the simulation runs at
    /// 30 Hz and the screen refreshes faster, and one frame can run several catch-up ticks, so a press
    /// is remembered until a tick consumes it (<see cref="ConsumeOneShots"/>) while the held state is
    /// read fresh every frame.
    ///
    /// Not a MonoBehaviour: the driver owns it and drives it, so there is no script execution-order
    /// race between reading input and advancing the simulation.
    /// This project has only the new Input System enabled - never use UnityEngine.Input here.
    /// </summary>
    public sealed class BuildInput
    {
        /// <summary>The digits that select a building, in <see cref="BuildCatalog.All"/> order. The
        /// tenth kind is key 0, beside 1..9; the loop below stops at the table's length, so a shorter
        /// table's spare keys are inert.</summary>
        private static readonly Key[] BuildKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6,
            Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0,
        };

        private bool _pauseToggleRequested;
        private bool _startWaveRequested;
        private bool _restartRequested;
        private bool _primaryReleased;
        private int _rotateSteps;

        private bool _buildHeld;
        private bool _removeHeld;
        private bool _wasBuildHeld;

        /// <summary>
        /// What the player is building, or null for the inspect cursor - which is where a run starts, so
        /// the game opens with no ghost promising a placement nobody asked for.
        /// </summary>
        private BuildKind? _selected;
        private bool _inspectPressed;
        private Int2 _cursor = new Int2(-1, -1);

        /// <summary>True exactly once after P was pressed. Cleared by the call.</summary>
        public bool ConsumePauseToggle()
        {
            bool value = _pauseToggleRequested;
            _pauseToggleRequested = false;
            return value;
        }

        /// <summary>Is N being asked for right now? Read by the driver as "go on" - start the next wave
        /// while a map is live, carry on to the next map once it is won - and by the world as "start the
        /// next wave". Non-destructive because both readers act on the same press in different states,
        /// and only the tick's consumer clears it.</summary>
        public bool StartWaveRequested => _startWaveRequested;

        /// <summary>Read every device once per frame: held state, latched one-shots, selection, cursor.
        /// <paramref name="pointerOverHud"/> is the HUD's "the mouse is mine right now": while it is true
        /// the buttons do not reach the world at all.</summary>
        public void Sample(Camera camera, SimWorld world, bool pointerOverHud = false)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                for (int i = 0; i < BuildKeys.Length && i < BuildCatalog.Count; i++)
                    if (keyboard[BuildKeys[i]].wasPressedThisFrame) ToggleSelect(BuildCatalog.All[i]);

                // Q/E turn the ghost the way a factory game does. Rotation is a request, not a state:
                // it is latched so that turning the ghost twice between two ticks turns it twice.
                if (keyboard.qKey.wasPressedThisFrame) _rotateSteps--;
                if (keyboard.eKey.wasPressedThisFrame) _rotateSteps++;

                // Escape drops the selection, which is the inspect cursor: the way back to reading the
                // map once the player has started building.
                if (keyboard.escapeKey.wasPressedThisFrame) _selected = null;

                // Space pauses - the reflex every player already has - and P does the same. Starting a
                // wave moved off Space entirely: it is N, or the HUD's button, so "skip the countdown"
                // is always a deliberate act.
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
                // unannounced (a lost focus, a WebGL canvas that stopped receiving events), so a machine
                // placement can never be left pending forever.
                if (mouse.leftButton.wasPressedThisFrame && !pointerOverHud && _selected == null)
                {
                    // With nothing selected the press is a read: the input source decides, because the
                    // selection is its state - the simulation only hears "read this tile".
                    _inspectPressed = true;
                }
                if (mouse.leftButton.wasReleasedThisFrame || (_wasBuildHeld && !held)) _primaryReleased = true;

                _wasBuildHeld = held;
                _buildHeld = held;
                _removeHeld = mouse.rightButton.isPressed && !pointerOverHud;
            }

            _cursor = CursorCell(camera, world);
        }

        /// <summary>The command the next tick should apply.</summary>
        public InputCommand Snapshot()
            => new InputCommand(_buildHeld, _removeHeld, _cursor, _primaryReleased,
                _selected, _rotateSteps, _startWaveRequested, _restartRequested, _inspectPressed);

        /// <summary>
        /// The HUD's build bar asking for a selection: the kind its tile names, or none at all (the
        /// inspect cursor, which the info card's own button asks for). It writes the same field the
        /// number keys write, so the next <see cref="Snapshot"/> carries it in
        /// <see cref="InputCommand.Selected"/> and the simulation stays the one place the selection is
        /// applied. The world applies it even while paused, so the highlighted tile follows the click.
        /// </summary>
        public void RequestSelect(BuildKind? kind)
        {
            if (kind.HasValue) ToggleSelect(kind.Value);
            else _selected = null;
        }

        /// <summary>
        /// Asking for the building already in hand drops it, which is how the player gets back to the
        /// read cursor without a key of its own: the digit key and the bar's tile are two doors onto
        /// this one rule.
        /// </summary>
        private void ToggleSelect(BuildKind kind) => _selected = _selected == kind ? null : kind;

        /// <summary>The HUD's rotate buttons asking for quarter turns of the placement ghost, latched
        /// exactly like the Q/E keys.</summary>
        public void RequestRotate(int steps) => _rotateSteps += steps;

        /// <summary>Drop the one-shots, once a tick has actually applied them.</summary>
        public void ConsumeOneShots()
        {
            _primaryReleased = false;
            _inspectPressed = false;
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
