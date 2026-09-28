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
        /// across. Splitter: unused - its ports are read off the belts around it. Sorter: which side
        /// the filtered shape leaves by; the other shapes leave by the other sides. Turret: where its
        /// barrel rests when it has no target.</summary>
        public Dir Direction;

        /// <summary>Seconds until this machine may act again. Pipe: the remaining transit time of the
        /// item inside. Decomposer: the remaining split time of the circle inside.</summary>
        public float Cooldown;

        /// <summary>What the machine is holding: a decomposer's circle being split, a pipe's item in
        /// transit, a splitter's buffered item, or None.</summary>
        public ShapeType Carried;

        /// <summary>Decomposer: outputs made but not yet pushed onto the belt, 0..the recipe's buffer
        /// (4 for the shipped decomposer).</summary>
        public int OutputCount;

        /// <summary>Splitter and sorter: the port the next item tries first, so a balanced hub rotates
        /// instead of always favouring the lowest-numbered output.</summary>
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

        /// <summary>Which building this is, and which behaviour drives it. Both are carried so the view
        /// can dispatch on the behaviour ("draw a converter") while still resolving the per-building
        /// look override the player authored, without looking either up per frame.</summary>
        public BuildKind Build;
        public BehaviorKind Behavior;

        public Dir Direction;
        public Vec2 Aim;
        public bool HasTarget;
        public bool Armed;

        /// <summary>What the building draws on itself: a turret's diet, a sorter's filter shape (the
        /// badge that says which shape goes which way), a decomposer's pending halves (or the circle
        /// being split), a pipe or splitter's buffered item, None when idle.</summary>
        public ShapeType Shape;

        /// <summary>Work-in-progress fraction for the view to animate: a decomposer's split, a pipe's
        /// transit across its tile. 0 for machines with nothing in flight.</summary>
        public float Work;

        /// <summary>Sides a belt feeds this machine from, and sides it pushes onto - the same sets the
        /// tick acts on, computed once in Core by <see cref="MachineDelivery.PortMasks"/>. The view
        /// draws its stubs and its neighbours' docking lanes from these instead of re-deriving the
        /// rule, so the picture cannot drift from the simulation.</summary>
        public DirMask InMask;
        public DirMask OutMask;
    }

    /// <summary>
    /// Where the machines are and what they are doing. Storage and placement rules, the occupancy
    /// registry the tick walks, and the snapshots the view reads - the tick behaviour itself lives in
    /// the machine behaviours, dispatched by <c>MachineSystem</c>.
    ///
    /// It keeps an explicit <see cref="Occupied"/> list of the cells that hold a machine, so stepping
    /// the machines and counting turrets cost the number of machines and not the number of tiles. The
    /// list is kept in ascending linear-index order rather than insertion order: iteration order is
    /// then the map's, independent of the order the player happened to build in, which is what keeps a
    /// replay of the same commands identical.
    /// </summary>
    public sealed class MachineField
    {
        private readonly TileGrid _grid;
        private readonly ShapePatchField _patches;
        private readonly BeltField _belts;
        private readonly ContentDatabase _content;

        private readonly MachineState[] _state;

        /// <summary>Occupied cells, as linear indices, ascending. The only iteration order.</summary>
        private readonly List<int> _occupied = new List<int>();

        public MachineField(TileGrid grid, ShapePatchField patches, BeltField belts, ContentDatabase content)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _patches = patches ?? throw new ArgumentNullException(nameof(patches));
            _belts = belts ?? throw new ArgumentNullException(nameof(belts));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _state = new MachineState[grid.Width * grid.Height];
        }

        /// <summary>Every occupied cell, ascending by linear index. The tick and the turret count walk
        /// this; nothing walks the whole grid.</summary>
        public IReadOnlyList<int> Occupied => _occupied;

        /// <summary>The cell at a position in <see cref="Occupied"/>, in the same stable order.</summary>
        public Int2 CellAt(int occupiedIndex) => _grid.CellOf(_occupied[occupiedIndex]);

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

        /// <summary>One machine, ready for the view, with its port masks resolved.</summary>
        public bool TryGetSnapshot(Int2 cell, out MachineSnapshot snapshot)
        {
            if (!TryGet(cell, out MachineState state))
            {
                snapshot = default;
                return false;
            }

            snapshot = SnapshotOf(state, cell);
            return true;
        }

        /// <summary>Mutable ref for the machine behaviours, which update cooldowns, aim and armed
        /// state in place.</summary>
        public ref MachineState RefAt(Int2 cell) => ref _state[_grid.Index(cell)];

        /// <summary>
        /// May a machine of this kind stand here? A drill only on a shape patch (that is what "mined"
        /// means), every other machine only on bare ground. Patches stay reserved for drills, so a
        /// machine cannot quietly sit on the only source of a shape.
        ///
        /// Bare ground means bare: a tile carrying a belt is neither empty nor a patch, so a machine
        /// needs the belt removed first. That is the whole cost of a belt crossing a vein - the drill
        /// that could have stood on that cell - and this is where the cost is exacted.
        /// </summary>
        public bool CanPlace(BuildKind build, Int2 cell)
        {
            if (!_grid.InBounds(cell)) return false;

            TileKind kind = _content.Machine(build).Tile;
            if (kind == TileKind.Belt) return false;                       // belts are BeltField's business

            TileKind here = _grid.Get(cell);
            // A drill needs a patch to mine, and a patch only ever wants a drill.
            return kind == TileKind.Drill ? here == TileKind.ShapePatch : here == TileKind.Empty;
        }

        public bool TryPlace(Int2 cell, BuildKind build, Dir direction)
        {
            if (!CanPlace(build, cell)) return false;

            TileKind kind = _content.Machine(build).Tile;
            int i = _grid.Index(cell);
            _state[i] = new MachineState
            {
                Kind = kind,
                Direction = direction,
                Aim = direction.ToVec(),
                Build = build,
            };
            _grid.Set(cell, kind);
            AddOccupied(i);
            return true;
        }

        /// <summary>Remove a machine and put the terrain back under it.</summary>
        public bool TryRemove(Int2 cell)
        {
            if (!Has(cell)) return false;

            int i = _grid.Index(cell);
            _state[i] = default;
            RemoveOccupied(i);
            _patches.RestoreTerrain(cell);
            return true;
        }

        public void Clear()
        {
            Array.Clear(_state, 0, _state.Length);
            _occupied.Clear();
        }

        /// <summary>Every machine, in stable linear-index order.</summary>
        public void GetMachines(List<MachineSnapshot> into)
        {
            into.Clear();
            for (int k = 0; k < _occupied.Count; k++)
            {
                int i = _occupied[k];
                into.Add(SnapshotOf(_state[i], _grid.CellOf(i)));
            }
        }

        /// <summary>Turret count and how many are armed - the HUD's one diagnostic number. Asks the
        /// content table whether a building is a turret rather than testing a tile kind, so the second
        /// gun counts without this method knowing it exists.</summary>
        public void CountTurrets(out int total, out int armed)
        {
            total = 0;
            armed = 0;
            for (int k = 0; k < _occupied.Count; k++)
            {
                ref readonly MachineState m = ref _state[_occupied[k]];
                if (!_content.IsTurret(m.Build)) continue;

                total++;
                if (m.Armed) armed++;
            }
        }

        private MachineSnapshot SnapshotOf(in MachineState m, Int2 cell)
        {
            MachineDelivery.PortMasks(_belts, _content, cell, in m, out DirMask inMask, out DirMask outMask);
            MachineDef def = _content.Machine(m.Build);

            return new MachineSnapshot
            {
                Cell = cell,
                Kind = m.Kind,
                Build = m.Build,
                Behavior = def.Behavior,
                Direction = m.Direction,
                Aim = m.Aim,
                HasTarget = m.HasTarget,
                Armed = m.Armed,
                Shape = ShapeOf(m, def),
                Work = WorkOf(m, def),
                InMask = inMask,
                OutMask = outMask,
            };
        }

        /// <summary>
        /// What the building draws on itself. Keyed on the machine's <em>behaviour</em>, not on its tile
        /// kind: a converter shows what its recipe is making, a turret its diet, a sorter the shape it
        /// routes. The two converters therefore show the two different minerals without this method
        /// naming either of them - a cutter's badge is simply the second recipe's output.
        /// </summary>
        private ShapeType ShapeOf(in MachineState m, MachineDef def)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Converter:
                {
                    RecipeDef recipe = RecipeOf(def);
                    if (recipe == null) return ShapeType.None;
                    if (m.OutputCount > 0) return recipe.Output;
                    return m.Carried == recipe.Input ? recipe.Input : ShapeType.None;
                }
                case BehaviorKind.Pipe:
                case BehaviorKind.Splitter:
                    return m.Carried;
                case BehaviorKind.Turret:
                    return _content.Turret(m.Build).Ammo;
                case BehaviorKind.Sorter:
                    // The filter, not what it happens to be holding: the badge is the machine's
                    // configuration - which shape leaves by the side it faces - and a badge that
                    // flickered between two shapes would read as a flickering setting.
                    return def.Filter;
                default:
                    return ShapeType.None;
            }
        }

        /// <summary>The work fraction the view animates: the split progressing, the item crossing
        /// the pipe. A pipe whose landing belt is blocked sits at 1 - parked at the far end.</summary>
        private float WorkOf(in MachineState m, MachineDef def)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Converter:
                {
                    RecipeDef recipe = RecipeOf(def);
                    if (recipe == null || recipe.Interval <= 0f) return 0f;
                    return m.Carried == recipe.Input ? 1f - m.Cooldown / recipe.Interval : 0f;
                }
                case BehaviorKind.Pipe:
                {
                    float interval = def.Interval;
                    return m.Carried != ShapeType.None && interval > 0f
                        ? 1f - m.Cooldown / interval
                        : 0f;
                }
                default:
                    return 0f;
            }
        }

        private RecipeDef RecipeOf(MachineDef def)
        {
            ContentId id = def.RecipeId;
            return id.IsNone ? null : _content.Recipe(id);
        }

        /// <summary>Insert an occupied cell, keeping the list sorted. Occupancy is monotone per cell,
        /// so a cell can never be added twice.</summary>
        private void AddOccupied(int index)
        {
            int at = _occupied.BinarySearch(index);
            if (at < 0) _occupied.Insert(~at, index);
        }

        private void RemoveOccupied(int index)
        {
            int at = _occupied.BinarySearch(index);
            if (at >= 0) _occupied.RemoveAt(at);
        }
    }
}
