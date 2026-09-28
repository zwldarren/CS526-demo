using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>
    /// State of one built machine. One struct covers drills, decomposers, pipes, splitters and
    /// turrets because they share the two things that make the twist work: a facing (where a
    /// drill/decomposer pushes, where a pipe carries, where a turret's barrel points) and being fed
    /// from the belt cells that deliver into them.
    ///
    /// The fields a given kind does not use are deliberate: five parallel arrays with five sets of
    /// accessors would cost more to read than this costs to store (a few tens of KB for the whole map).
    /// </summary>
    public struct MachineState
    {
        /// <summary>One of the machine <see cref="TileKind"/>s. <see cref="TileKind.Empty"/> means the
        /// cell holds no machine.</summary>
        public TileKind Kind;

        /// <summary>Which building this is. Turrets read their whole spec - diet, rate, damage,
        /// range - back out of it, so one field covers the kind and its numbers.</summary>
        public BuildKind Build;

        /// <summary>Drill and decomposer: the cell they push out to. Pipe: the direction it carries
        /// across. Splitter: unused - its ports are read off the belts around it. Turret: where its
        /// barrel rests when it has no target.</summary>
        public Dir Direction;

        /// <summary>Seconds until this machine may act again. Pipe: the remaining transit time of the
        /// item inside. Decomposer: the remaining split time of the circle inside.</summary>
        public float Cooldown;

        /// <summary>What the machine is holding: a decomposer's circle being split, a pipe's item in
        /// transit, a splitter's buffered item, or None.</summary>
        public ShapeType Carried;

        /// <summary>Decomposer: half-circles split but not yet pushed onto the belt,
        /// 0..<see cref="Balance.DecomposerBuffer"/>.</summary>
        public int OutputCount;

        /// <summary>Splitter: the port the next item tries first, so a balanced hub rotates instead
        /// of always favouring the lowest-numbered output.</summary>
        public int Rotation;

        /// <summary>Turret: unit vector the barrel points at, updated every tick.</summary>
        public Vec2 Aim;

        /// <summary>Turret: the enemy it is currently shooting at, 0 for none. Shots home on the id,
        /// and <see cref="TargetPosition"/> is where that enemy was when the shot was fired.</summary>
        public int TargetId;

        public Vec2 TargetPosition;

        /// <summary>Turret: an enemy is in range this tick.</summary>
        public bool HasTarget;

        /// <summary>Turret: the right shape is waiting on one of its input belts, so it can fire the
        /// moment there is something to shoot. False is the "silenced" state the twist is about.</summary>
        public bool Armed;
    }

    /// <summary>One machine, flattened for the view layer.</summary>
    public struct MachineSnapshot
    {
        public Int2 Cell;
        public TileKind Kind;
        public Dir Direction;
        public Vec2 Aim;
        public bool HasTarget;
        public bool Armed;

        /// <summary>What the building draws on itself: a turret's diet, a decomposer's pending halves
        /// (or the circle being split), a pipe or splitter's buffered item, None when idle.</summary>
        public ShapeType Shape;

        /// <summary>Work-in-progress fraction for the view to animate: a decomposer's split, a pipe's
        /// transit across its tile. 0 for machines with nothing in flight.</summary>
        public float Work;
    }

    /// <summary>
    /// Where the machines are and what they are doing. Storage and placement rules only - the tick
    /// behaviour lives in <see cref="ProductionSystem"/> (drills, decomposers) and <see cref="TurretSystem"/>.
    /// </summary>
    public sealed class MachineField
    {
        private readonly TileGrid _grid;
        private readonly ShapePatchField _patches;

        private readonly MachineState[] _state;

        public MachineField(TileGrid grid, ShapePatchField patches)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _patches = patches ?? throw new ArgumentNullException(nameof(patches));
            _state = new MachineState[grid.Width * grid.Height];
        }

        public bool Has(Int2 cell) => _grid.InBounds(cell) && _state[_grid.Index(cell)].Kind.IsMachine();

        public bool TryGet(Int2 cell, out MachineState state)
        {
            if (!Has(cell))
            {
                state = default;
                return false;
            }

            state = _state[_grid.Index(cell)];
            return true;
        }

        /// <summary>Mutable ref for the tick systems, which update cooldowns, aim and armed state in place.</summary>
        public ref MachineState RefAt(Int2 cell) => ref _state[_grid.Index(cell)];

        /// <summary>
        /// May a machine of this kind stand here? A drill only on a shape patch (that is what "mined"
        /// means), everything else only on bare ground. Patches stay reserved for drills, so a player
        /// cannot quietly pave over the only source of a shape.
        /// </summary>
        public bool CanPlace(BuildKind build, Int2 cell)
        {
            if (!_grid.InBounds(cell)) return false;

            TileKind kind = BuildCatalog.TileFor(build);
            if (kind == TileKind.Belt) return false;                       // belts are BeltField's business

            TileKind here = _grid.Get(cell);
            // A drill needs a patch to mine, and a patch only ever wants a drill.
            return kind == TileKind.Drill ? here == TileKind.ShapePatch : here == TileKind.Empty;
        }

        public bool TryPlace(Int2 cell, BuildKind build, Dir direction)
        {
            if (!CanPlace(build, cell)) return false;

            TileKind kind = BuildCatalog.TileFor(build);
            int i = _grid.Index(cell);
            _state[i] = default;
            _state[i].Kind = kind;
            _state[i].Direction = direction;
            _state[i].Aim = direction.ToVec();
            _state[i].Build = build;
            _grid.Set(cell, kind);
            return true;
        }

        /// <summary>Remove a machine and put the terrain back under it.</summary>
        public bool TryRemove(Int2 cell)
        {
            if (!Has(cell)) return false;

            _state[_grid.Index(cell)] = default;
            _patches.RestoreTerrain(cell);
            return true;
        }

        public void Clear()
        {
            Array.Clear(_state, 0, _state.Length);
        }

        /// <summary>Every machine, in stable linear-index order.</summary>
        public void GetMachines(List<MachineSnapshot> into)
        {
            into.Clear();
            for (int i = 0; i < _state.Length; i++)
            {
                ref readonly MachineState m = ref _state[i];
                if (!m.Kind.IsMachine()) continue;

                into.Add(new MachineSnapshot
                {
                    Cell = _grid.CellOf(i),
                    Kind = m.Kind,
                    Direction = m.Direction,
                    Aim = m.Aim,
                    HasTarget = m.HasTarget,
                    Armed = m.Armed,
                    Shape = ShapeOf(m),
                    Work = WorkOf(m),
                });
            }
        }

        /// <summary>Turret count and how many are armed - the HUD's one diagnostic number.</summary>
        public void CountTurrets(out int total, out int armed)
        {
            total = 0;
            armed = 0;
            for (int i = 0; i < _state.Length; i++)
            {
                ref readonly MachineState m = ref _state[i];
                if (m.Kind != TileKind.Turret) continue;

                total++;
                if (m.Armed) armed++;
            }
        }

        /// <summary>What the building draws on itself: a turret's diet, a decomposer's output halves
        /// (or the circle mid-split), a pipe or splitter's buffered item, a drill has none.</summary>
        private static ShapeType ShapeOf(in MachineState m)
        {
            switch (m.Kind)
            {
                case TileKind.Decomposer:
                    if (m.OutputCount > 0) return ShapeType.HalfCircle;
                    return m.Carried == ShapeType.Circle ? ShapeType.Circle : ShapeType.None;
                case TileKind.Pipe:
                case TileKind.Splitter:
                    return m.Carried;
                case TileKind.Turret:
                    return Balance.Turret(m.Build).Ammo;
                default:
                    return ShapeType.None;
            }
        }

        /// <summary>The work fraction the view animates: the split progressing, the item crossing
        /// the pipe. A pipe whose landing belt is blocked sits at 1 - parked at the far end.</summary>
        private static float WorkOf(in MachineState m)
        {
            switch (m.Kind)
            {
                case TileKind.Decomposer:
                    return m.Carried == ShapeType.Circle
                        ? 1f - m.Cooldown / Balance.DecomposeInterval
                        : 0f;
                case TileKind.Pipe:
                    return m.Carried != ShapeType.None
                        ? 1f - m.Cooldown / Balance.PipeTransit
                        : 0f;
                default:
                    return 0f;
            }
        }
    }
}
