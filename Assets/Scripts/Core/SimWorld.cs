using System;

namespace Facet.Core
{
    /// <summary>
    /// The whole logic layer. Deterministic, engine-free, advanced exactly once per fixed tick
    /// by the host. Nothing here knows about Unity, time.DeltaTime, or frame rates.
    /// </summary>
    public sealed class SimWorld
    {
        /// <summary>Sentinel for "the cursor was nowhere", never a real tile.</summary>
        private static readonly Int2 NoCell = new Int2(int.MinValue, int.MinValue);

        public readonly TileGrid TileGrid;
        public readonly BeltField Belts;
        public readonly SimConfig Config;

        /// <summary>Ticks elapsed since the run started. The clock everything else reads.</summary>
        public int TickCount { get; private set; }

        /// <summary>Paused worlds do not advance and do not accept build commands.</summary>
        public bool Paused;

        /// <summary>The defended object. Losing it loses the run.</summary>
        public CoreState Core;

        private bool _dragging;
        private Int2 _dragCell;
        private bool _dragCellIsOurs;
        private Int2 _lastRemovedCell = NoCell;

        public SimWorld(TileGrid grid, SimConfig config)
        {
            TileGrid = grid ?? throw new ArgumentNullException(nameof(grid));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Belts = new BeltField(grid);

            PlaceCore(new Int2(grid.Width / 2, grid.Height / 2));
        }

        /// <summary>Advance one fixed step. Call at exactly SimConfig.TickRate Hz.</summary>
        public void Tick(InputCommand cmd)
        {
            if (Paused) return;

            ApplyBuild(cmd);
            Belts.Step(SimConfig.TickDt, Config.BeltSpeed);

            // Enemies, turrets and waves are ticked here as they land.

            TickCount++;
        }

        public bool CanPlaceBelt(Int2 cell) => TileGrid.IsBuildable(cell);

        public bool TryPlaceBelt(Int2 cell, Dir direction) => Belts.TryPlace(cell, direction);

        public bool TryRemoveBelt(Int2 cell) => Belts.TryRemove(cell);

        /// <summary>Block a belt cell. Turrets will call this on a wrong-shape delivery.</summary>
        public bool TryJamBelt(Int2 cell) => Belts.TryJam(cell);

        public bool TryClearJam(Int2 cell) => Belts.TryClearJam(cell);

        /// <summary>Drop a shape onto a belt cell. Drills use this; so do the tests and the debug key.</summary>
        public bool TrySpawnItem(Int2 cell, ShapeType shape) => Belts.TrySpawnItem(cell, shape);

        private void ApplyBuild(InputCommand cmd)
        {
            if (cmd.RemoveHeld)
            {
                // Right-drag erases: each new cell under the cursor is removed once.
                _dragging = false;
                if (cmd.CursorCell != _lastRemovedCell)
                {
                    _lastRemovedCell = cmd.CursorCell;
                    TryRemoveBelt(cmd.CursorCell);
                }
                return;
            }
            _lastRemovedCell = NoCell;

            if (!cmd.BuildHeld)
            {
                _dragging = false;
                return;
            }

            if (!_dragging)
            {
                // The press itself lays nothing: a belt needs a direction, and there is not one
                // until the cursor moves. So a bare click is a no-op and only a drag builds.
                _dragging = true;
                _dragCell = cmd.CursorCell;

                // A cell the player deliberately pressed on may be re-pointed by this drag; a cell
                // merely swept over may not. Pressing onto the end of an existing run and dragging
                // on is how a second segment continues the first, so that corner has to turn.
                _dragCellIsOurs = Belts.Has(_dragCell);
                return;
            }

            if (cmd.CursorCell == _dragCell) return;

            LayPath(cmd.CursorCell);
        }

        /// <summary>
        /// Walk from the cell the drag last reached to the cursor, one tile at a time, laying belts.
        ///
        /// The walk is Manhattan - X first, then Y. An axis-aligned drag has one component at zero
        /// so the choice never shows; on a diagonal, horizontal-then-vertical reads the way the
        /// cursor swept the screen. A blocked tile is skipped but the walk continues, and the
        /// direction link is deliberately not carried across the gap.
        ///
        /// Only cells this drag owns get re-pointed: the one it started from (if it was already a
        /// belt) and the ones it laid itself. A belt the cursor merely passes over keeps its
        /// direction, so dragging across an existing build cannot silently rewire it.
        /// </summary>
        private void LayPath(Int2 target)
        {
            Int2 cur = _dragCell;
            bool curIsOurs = _dragCellIsOurs;

            while (cur != target)
            {
                Int2 next = NextStep(cur, target);
                Dir dir = DirectionOf(cur, next);

                // On the first step the press cell becomes a belt too, so the run starts under the
                // cursor rather than one tile along. Afterwards the cell is re-pointed forward,
                // which is what turns a corner into a turn instead of a dead end.
                if (curIsOurs) Belts.TrySetDirection(cur, dir);
                else curIsOurs = Belts.TryPlace(cur, dir);

                bool placed = Belts.TryPlace(next, dir);

                cur = next;
                curIsOurs = placed;
            }

            _dragCell = cur;
            _dragCellIsOurs = curIsOurs;
        }

        private static Int2 NextStep(Int2 from, Int2 to)
        {
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

        private void PlaceCore(Int2 cell)
        {
            TileGrid.Set(cell, TileKind.Core);
            Core = new CoreState { Cell = cell, Hp = Config.CoreMaxHp, MaxHp = Config.CoreMaxHp };
        }
    }
}
