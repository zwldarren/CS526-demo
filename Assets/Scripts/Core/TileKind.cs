namespace Facet.Core
{
    /// <summary>
    /// What occupies a tile. Terrain (Core, ShapePatch) is placed by the map and never by the player;
    /// everything else is a building.
    /// </summary>
    public enum TileKind : byte
    {
        Empty = 0,

        Belt = 1,

        /// <summary>The defended object, and the bank: a belt that delivers into it turns a circle
        /// into spendable stockpile. Not buildable, not walkable, its HP is the fail state.</summary>
        Core = 2,

        /// <summary>A shape patch in the ground. A drill is the only <em>machine</em> that may be built
        /// on it - transport crosses it (<see cref="TileGrid.CanLayBelt"/>), and because the patch is
        /// recorded in <see cref="ShapePatchField"/> rather than here, removing whatever stands on it
        /// gives the ore back.</summary>
        ShapePatch = 3,

        /// <summary>Mines the shape patch under it and pushes a shape onto the belt it faces.</summary>
        Drill = 4,

        /// <summary>Splits one circle into two half-circles, pushed out the way it faces. Eats only
        /// circles - a half-circle delivered back to it jams the feeding belt.</summary>
        Decomposer = 5,

        /// <summary>Carries one item over a single tile: eats anything delivered to it and drops it
        /// on the belt two cells ahead in its facing, so two belts can cross. Transport, not a
        /// machine with a diet - it never jams.</summary>
        Pipe = 6,

        /// <summary>One hub whose ports are read off the belts around it: a belt pointing in is an
        /// input, a belt pointing away is an output, so 1-in-3-out, 3-in-1-out and 2-in-2-out are
        /// the same building on different streets.</summary>
        Splitter = 7,

        /// <summary>Fires half-circles and eats only half-circles. See <see cref="TurretDef"/>.</summary>
        Turret = 8,

        /// <summary>Routes by shape: the shape its definition filters for leaves by the side it faces,
        /// everything else by the other wired outlets. Like a splitter it reads its ports off the
        /// belts around it, so it is the same kind of hub on a different job.</summary>
        Sorter = 9,

        /// <summary>The second converter: runs the bisect recipe (square in, half-squares out). The
        /// same <see cref="BehaviorKind.Converter"/> as the decomposer - a different row, not a
        /// different behaviour.</summary>
        Cutter = 10,

        /// <summary>The second turret: fires half-squares, slower and harder than the cannon. Same
        /// <see cref="BehaviorKind.Turret"/>; its diet and numbers come from its
        /// <see cref="TurretDef"/>.</summary>
        Mortar = 11,
    }

    public static class TileKindExtensions
    {
        /// <summary>True for a building: something the player placed, or the Core. Terrain
        /// (<see cref="TileKind.Empty"/>, <see cref="TileKind.ShapePatch"/>) is not a building, which is
        /// why a tile can be "not occupied" and still be ore.</summary>
        public static bool IsBuilding(this TileKind kind)
            => kind != TileKind.Empty && kind != TileKind.ShapePatch;

        /// <summary>True for the built machines, which all carry a <see cref="MachineState"/>.</summary>
        public static bool IsMachine(this TileKind kind)
            => kind == TileKind.Drill || kind == TileKind.Decomposer || kind == TileKind.Pipe
               || kind == TileKind.Splitter || kind == TileKind.Turret || kind == TileKind.Sorter
               || kind == TileKind.Cutter || kind == TileKind.Mortar;

        /// <summary>
        /// True for the tiles a belt can hand an item to: anything solid. Bare ground and a shape
        /// patch are the only things a belt can point at that simply receive nothing.
        ///
        /// Stated as "not the two empty kinds" rather than a list of the kinds that eat, so a new
        /// machine docks like every other one without the view's docking lanes being told about it a
        /// second time.
        /// </summary>
        public static bool Eats(this TileKind kind)
            => kind != TileKind.Empty && kind != TileKind.ShapePatch;
    }
}
