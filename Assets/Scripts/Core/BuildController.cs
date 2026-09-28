using System;

namespace Facet.Core
{
    /// <summary>
    /// All the build-mode input state: which building is selected, which way the ghost faces, and the
    /// press/drag state machine that decides *where* things land. It lives in the simulation rather
    /// than in the view because placement is a rule (a drill only on a patch, a belt needs a direction)
    /// and rules belong with the state they change; the view only reads
    /// <see cref="Selected"/> and <see cref="PlacementDirection"/> to draw the ghost.
    ///
    /// A belt drag lays nothing on the press: the path is laid one tile at a time as the cursor moves,
    /// and only cells this drag owns may be re-pointed, so sweeping across an existing run cannot
    /// silently rewire it. Machines invert that: a single tile cannot sweep, so a drag *aims* and the
    /// machine lands where the press was.
    ///
    /// Both kinds of drag answer the same question - which way did this drag go? - with the same rule
    /// (<see cref="DominantDirection"/>): the axis the drag ran further along, with X winning a tie. A
    /// belt walk follows it too, so a run drawn upwards leaves the press cell *upwards* rather than
    /// stepping sideways first, and so a run drawn out of a machine leaves along the same side
    /// <see cref="PointMachineAtTheRun"/> aims that machine at.
    ///
    /// A drag is also one gesture in the *other* sense: what is being built is decided by the press and
    /// does not change until the button comes up, so a selection change under the player's hand cannot
    /// turn "aim a drill" into "pave from here" (<see cref="Apply"/>).
    /// </summary>
    public sealed class BuildController
    {
        private static readonly Int2 NoCell = new Int2(int.MinValue, int.MinValue);

        private readonly TileGrid _grid;
        private readonly BeltField _belts;
        private readonly MachineField _machines;
        private readonly EconomyState _economy;

        /// <summary>The stream placements and removals are reported on. Derived telemetry: the
        /// controller's decisions are unchanged by it.</summary>
        private readonly SimEventBuffer _events;

        private bool _dragging;
        private bool _machinePress;
        private Int2 _pressCell;
        private Int2 _dragCell;
        private bool _dragCellIsOurs;
        private Int2 _lastRemovedCell;

        public BuildKind Selected { get; private set; }

        public Dir GhostDirection { get; private set; }

        /// <summary>
        /// The facing the cursor ghost should draw, which is not always <see cref="GhostDirection"/>:
        /// while a belt run is being dragged the run carries its own direction, so the chevron under
        /// the cursor shows the way the belt is being laid instead of the way Q/E last left the ghost.
        /// Before the drag moves - and for every machine - the ghost's own facing is still the answer.
        /// </summary>
        public Dir PlacementDirection
            => _dragging && Selected == BuildKind.Belt && _dragCell != _pressCell
                ? DominantDirection(_pressCell, _dragCell)
                : GhostDirection;

        public BuildController(TileGrid grid, BeltField belts, MachineField machines, EconomyState economy,
            SimEventBuffer events)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _belts = belts ?? throw new ArgumentNullException(nameof(belts));
            _machines = machines ?? throw new ArgumentNullException(nameof(machines));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            Reset();
        }

        public void Reset()
        {
            Selected = BuildKind.Belt;
            GhostDirection = Dir.East;
            _dragging = false;
            _machinePress = false;
            _pressCell = NoCell;
            _dragCell = NoCell;
            _dragCellIsOurs = false;
            _lastRemovedCell = NoCell;
        }

        /// <summary>Would a placement of this kind be allowed here? Structural only - whether the
        /// stockpile can cover it is <see cref="EconomyState.CanAfford"/>, and the cursor ghost reads
        /// both. (The twist's costs live in circles, and circles live in the economy.)</summary>
        public bool CanPlace(BuildKind kind, Int2 cell)
            => kind == BuildKind.Belt ? _grid.CanLayBelt(cell) : _machines.CanPlace(kind, cell);

        /// <summary>Place one building and pay for it. A drag lays belts cell by cell through this,
        /// so a line simply stops growing when the stockpile runs dry - skipped like a blocked tile,
        /// not an error.</summary>
        public bool TryPlace(BuildKind kind, Int2 cell, Dir direction)
        {
            if (!CanPlace(kind, cell)) return false;
            if (!_economy.TrySpend(kind)) return false;

            bool placed = kind == BuildKind.Belt
                ? _belts.TryPlace(cell, direction)
                : _machines.TryPlace(cell, kind, direction);

            // Unreachable in practice - CanPlace just answered the same question - but a spend with
            // no placement must never stand.
            if (!placed)
            {
                _economy.Refund(kind);
                return false;
            }

            _events.Built(cell, _economy.CostOf(kind));
            return true;
        }

        /// <summary>
        /// Take whatever is on this tile away. A **jammed belt is un-jammed instead of deleted**: that
        /// is the doc's "clears the jam" action, it destroys the wrong-shape item that caused the
        /// jam (leaving it would re-jam the same cell on the next tick), and it keeps the line's shape
        /// so the player does not have to redraw the run they just diagnosed. Anything actually
        /// removed refunds its full cost.
        /// </summary>
        public bool Remove(Int2 cell)
        {
            if (_belts.IsJammed(cell)) return _belts.TryClearJam(cell);
            if (_belts.Has(cell))
            {
                if (!_belts.TryRemove(cell)) return false;
                _economy.Refund(BuildKind.Belt);
                _events.Removed(cell, _economy.CostOf(BuildKind.Belt));
                return true;
            }
            if (_machines.TryGet(cell, out MachineState machine) && _machines.TryRemove(cell))
            {
                _economy.Refund(machine.Build);
                _events.Removed(cell, _economy.CostOf(machine.Build));
                return true;
            }
            return false;
        }

        /// <summary>
        /// Change the selection outside the press/release gesture. This is how the HUD's build bar
        /// selects, and it is deliberately the same door a number key comes through: the next tick
        /// carries it in <see cref="InputCommand.Selected"/> so there is still exactly one owner of
        /// the state. A gesture already in progress keeps its own selection, so clicking a palette
        /// tile can never re-purpose the drag the player is halfway through making.
        /// </summary>
        public void Select(BuildKind kind)
        {
            if (_dragging) return;
            Selected = kind;
        }

        public void Apply(InputCommand cmd)
        {
            // The press decides what is being built; the selection waits for the next gesture. Updating
            // it mid-drag would re-purpose the gesture half way through it: aiming a drill, tapping the
            // belt key and letting go would lay a belt run out of the press cell instead of placing the
            // drill. That is the destructive surprise the belt walk already refuses to allow (only cells
            // this drag owns may be re-pointed), and latching is the same rule one level up. The ghost
            // therefore shows what the release will actually build.
            Select(cmd.Selected);

            if (cmd.RotateSteps != 0) GhostDirection = Rotate(GhostDirection, cmd.RotateSteps);

            if (cmd.RemoveHeld)
            {
                // Right-drag erases: each new cell under the cursor is removed once.
                _dragging = false;
                _machinePress = false;
                if (cmd.CursorCell != _lastRemovedCell)
                {
                    _lastRemovedCell = cmd.CursorCell;
                    Remove(cmd.CursorCell);
                }
                return;
            }

            _lastRemovedCell = NoCell;

            bool isMachine = Selected != BuildKind.Belt;

            // A press starts the drag. An input source that only reports "held" (a test, a replay)
            // still works: the press is simply taken to be wherever the cursor already is.
            if (!_dragging && cmd.BuildHeld)
            {
                _dragging = true;
                _machinePress = isMachine;
                _pressCell = cmd.CursorCell;
                _dragCell = cmd.CursorCell;
                _dragCellIsOurs = _belts.Has(cmd.CursorCell);
            }

            if (_dragging && cmd.BuildHeld && cmd.CursorCell != _dragCell)
            {
                if (isMachine)
                {
                    // Aim: the building still lands on the press cell, but it will face the way the
                    // drag went, and the ghost shows that while the button is down.
                    GhostDirection = DominantDirection(_pressCell, cmd.CursorCell);
                    _dragCell = cmd.CursorCell;
                }
                else
                {
                    // The first step is the one that leaves the press cell, so it is also the step that
                    // decides which side of a machine under the press the run leaves from: point that
                    // machine at the run before laying it, or the run and the machine disagree.
                    if (_dragCell == _pressCell) PointMachineAtTheRun(cmd.CursorCell);
                    LayPath(cmd.CursorCell);
                }
            }

            bool released = cmd.PrimaryReleased || (_dragging && !cmd.BuildHeld);
            if (!released) return;

            _dragging = false;
            if (!_machinePress) return;

            _machinePress = false;
            TryPlace(Selected, _pressCell, GhostDirection);
        }

        /// <summary>
        /// Walk from the cell the drag last reached to the cursor, one tile at a time, laying belts.
        ///
        /// The walk is Manhattan, and the axis it takes first is the drag's own dominant axis - the
        /// same rule <see cref="DominantDirection"/> uses to aim a machine. So a run dragged up the
        /// screen starts *up* from the press cell and puts its sideways step at the far end, instead
        /// of always stepping in X first and leaving the press cell to the left or the right whatever
        /// the player did. An axis-aligned drag has one component at zero, so the choice never shows;
        /// on a diagonal it is what makes the run leave the way the player dragged. A blocked tile is
        /// skipped but the walk continues, and the direction link is deliberately not carried across
        /// the gap.
        ///
        /// Only cells this drag owns get re-pointed: the one it started from (if it was already a
        /// belt) and the ones it laid itself. A belt the cursor merely passes over keeps its
        /// direction, so dragging across an existing build cannot silently rewire it.
        /// </summary>
        private void LayPath(Int2 target)
        {
            Int2 cur = _dragCell;
            bool curIsOurs = _dragCellIsOurs;

            // One decision for the whole walk: deciding per step would turn a diagonal drag into a
            // staircase instead of the two straight legs the cursor swept.
            bool yFirst = WalkYFirst(_pressCell, target);

            while (cur != target)
            {
                Int2 next = NextStep(cur, target, yFirst);
                Dir dir = DirectionOf(cur, next);

                // On the first step the press cell becomes a belt too, so the run starts under the
                // cursor rather than one tile along. Afterwards the cell is re-pointed forward,
                // which is what turns a corner into a turn instead of a dead end. Placement goes
                // through TryPlace so every cell is paid for: a dry stockpile stops the run growing,
                // exactly like a blocked tile does.
                if (curIsOurs) _belts.TrySetDirection(cur, dir);
                else curIsOurs = TryPlace(BuildKind.Belt, cur, dir);

                bool placed = TryPlace(BuildKind.Belt, next, dir);

                cur = next;
                curIsOurs = placed;
            }

            _dragCell = cur;
            _dragCellIsOurs = curIsOurs;
        }

        /// <summary>
        /// Point the machine standing on the press cell at the run just drawn out of it. A run leaving
        /// a machine's own cell can only be that machine's output - a belt that *feeds* a machine has
        /// to point into it, which means it was drawn from the other end - so the machine turns to face
        /// the run whatever way the player had aimed it.
        ///
        /// Without this a drill placed by a plain click keeps the ghost's default facing (east) and
        /// silently mines nothing into a run drawn north or south, which reads as "belts only work to
        /// the left and right". Drills and pipes only: their facing *is* their output. A decomposer's
        /// outputs are read off the belts around it, exactly like a splitter's, so turning one would
        /// promise what Q/E cannot deliver; a turret eats from all four sides and its facing is just
        /// where its barrel rests.
        /// </summary>
        private void PointMachineAtTheRun(Int2 cursorCell)
        {
            if (!_machines.TryGet(_pressCell, out MachineState machine)) return;
            if (machine.Kind != TileKind.Drill && machine.Kind != TileKind.Pipe) return;

            _machines.RefAt(_pressCell).Direction = DominantDirection(_pressCell, cursorCell);
        }

        /// <summary>
        /// Which axis a walk from <paramref name="from"/> to <paramref name="to"/> takes first: Y when
        /// the drag ran further along Y. X wins a tie, exactly as <see cref="DominantDirection"/> does,
        /// so a walk and the machine being aimed by the same drag always agree on which way it went.
        /// </summary>
        private static bool WalkYFirst(Int2 from, Int2 to)
            => Math.Abs(to.Y - from.Y) > Math.Abs(to.X - from.X);

        private static Int2 NextStep(Int2 from, Int2 to, bool yFirst)
        {
            if (yFirst && from.Y != to.Y)
                return new Int2(from.X, from.Y + Math.Sign(to.Y - from.Y));

            return from.X != to.X
                ? new Int2(from.X + Math.Sign(to.X - from.X), from.Y)
                : new Int2(from.X, from.Y + Math.Sign(to.Y - from.Y));
        }

        private static Dir DirectionOf(Int2 from, Int2 to)
        {
            if (to.X > from.X) return Dir.East;
            if (to.X < from.X) return Dir.West;
            return to.Y > from.Y ? Dir.North : Dir.South;
        }

        /// <summary>The way a drag points a single-tile building: whichever axis it ran further along,
        /// with X winning a tie exactly as <see cref="LayPath"/> does.</summary>
        private static Dir DominantDirection(Int2 from, Int2 to)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? Dir.East : Dir.West;
            return dy >= 0 ? Dir.North : Dir.South;
        }

        /// <summary>Quarter turns, clockwise for positive steps. Dir's ordinals are in clockwise order.</summary>
        private static Dir Rotate(Dir direction, int steps)
        {
            int value = ((int)direction + steps) % 4;
            if (value < 0) value += 4;
            return (Dir)value;
        }
    }
}
