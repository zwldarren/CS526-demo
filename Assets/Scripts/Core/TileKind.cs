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

        /// <summary>A shape patch in the ground. Only a drill may be built on it; removing the drill
        /// restores the patch.</summary>
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

        /// <summary>Fires half-circles and eats only half-circles. See <see cref="Balance"/>.</summary>
        Turret = 8,
    }

    public static class TileKindExtensions
    {
        /// <summary>True for the built machines, which all carry a <see cref="MachineState"/>.</summary>
        public static bool IsMachine(this TileKind kind)
            => kind == TileKind.Drill || kind == TileKind.Decomposer || kind == TileKind.Pipe
               || kind == TileKind.Splitter || kind == TileKind.Turret;
    }
}
