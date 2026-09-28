namespace Facet.Core
{
    /// <summary>One rectangular shape patch in the ground.</summary>
    public readonly struct ShapePatch
    {
        public readonly Int2 Origin;
        public readonly int Width;
        public readonly int Height;
        public readonly ShapeType Shape;

        public ShapePatch(Int2 origin, int width, int height, ShapeType shape)
        {
            Origin = origin;
            Width = width;
            Height = height;
            Shape = shape;
        }

        public bool Contains(Int2 cell)
            => cell.X >= Origin.X && cell.Y >= Origin.Y &&
               cell.X < Origin.X + Width && cell.Y < Origin.Y + Height;
    }

    /// <summary>
    /// One map: its size, the stockpile the run starts with, where minerals come out of the ground,
    /// where waves walk in from, and the wave table. All fixed data, so a run is reproducible from
    /// the world's own state - and so a new map is a new entry in <see cref="Maps.All"/>, not a
    /// code change.
    /// </summary>
    public sealed class MapDefinition
    {
        public readonly string Name;
        public readonly int Width;
        public readonly int Height;

        /// <summary>Circles in the stockpile when the run starts. The bootstrap budget: the first
        /// drill, its line home, and the first defence all come out of this.</summary>
        public readonly int StartCircles;

        public readonly ShapePatch[] Patches;
        public readonly Int2[] SpawnPoints;
        public readonly WaveDefinition[] Waves;

        public MapDefinition(string name, int width, int height, int startCircles,
            ShapePatch[] patches, Int2[] spawnPoints, WaveDefinition[] waves)
        {
            Name = name;
            Width = width;
            Height = height;
            StartCircles = startCircles;
            Patches = patches;
            SpawnPoints = spawnPoints;
            Waves = waves;
        }

        /// <summary>Centre of a spawn point in world units, where enemies are created.</summary>
        public Vec2 SpawnCenter(int index)
        {
            Int2 cell = SpawnPoints[index % SpawnPoints.Length];
            return new Vec2(cell.X + 0.5f, cell.Y + 0.5f);
        }
    }

    /// <summary>
    /// The shipped maps, in progression order.
    ///
    /// Map 1 is deliberately tutorial-shaped: one mineral (circles), one wave, and patches laid so
    /// the player learns the whole loop once - mine a circle, belt it home to bank it, split one
    /// into ammunition, and hold a single approach. Later maps add minerals and waves by adding
    /// entries here, not by touching systems.
    ///
    /// Map 2 (Foundry) is where the second chain earns its keep. It is smaller, so belt runs are
    /// shorter and money goes further, but it opens two minerals at once and its waves are three
    /// times the size of the Quarry's - which is the point: one gun on one line cannot absorb them,
    /// and the answer is a second line with a second diet. The square patches sit close to the Core
    /// and the circle patches far, so the cheap firepower is the near one and the money is the walk.
    /// </summary>
    public static class Maps
    {
        public static readonly MapDefinition[] All =
        {
            new MapDefinition("Quarry", width: 80, height: 48, startCircles: 80,
                patches: new[]
                {
                    // Three circle patches around the Core: one near, two far enough that a long
                    // belt run home is a purchase, not a formality.
                    new ShapePatch(new Int2(14, 10), 3, 2, ShapeType.Circle),
                    new ShapePatch(new Int2(14, 36), 3, 2, ShapeType.Circle),
                    new ShapePatch(new Int2(62, 22), 3, 2, ShapeType.Circle),
                },
                spawnPoints: new[]
                {
                    new Int2(40, 1),    // 0 South
                    new Int2(1, 24),    // 1 West
                },
                waves: new[]
                {
                    // The one wave: a trickle to learn on, a second pulse from the same door, then a
                    // small flanking group from the west once the defence is committed south. Its
                    // intermission is 0 and unread: this is the map's first wave, which never counts
                    // down - it waits for the player to call it (WaveDirector.FirstWaveHeld).
                    new WaveDefinition(0f,
                        new SpawnGroup(EnemyKind.Spike, 6, 0, 2f, 2.4f),
                        new SpawnGroup(EnemyKind.Spike, 6, 0, 22f, 1.8f),
                        new SpawnGroup(EnemyKind.Spike, 4, 1, 40f, 2.0f)),
                }),

            new MapDefinition("Foundry", width: 64, height: 40, startCircles: 90,
                patches: new[]
                {
                    // The money is a walk: two circle patches at opposite corners, and a third down
                    // the west side, so banking has to be paid for in belts on every approach.
                    new ShapePatch(new Int2(8, 6), 3, 2, ShapeType.Circle),
                    new ShapePatch(new Int2(8, 32), 3, 2, ShapeType.Circle),
                    new ShapePatch(new Int2(52, 18), 3, 2, ShapeType.Circle),

                    // The second mineral sits within a short belt of the Core on two sides. That is
                    // deliberate: the mortar's ammunition is the cheap thing to reach, and the
                    // circle line is the one that has to be invested in.
                    new ShapePatch(new Int2(25, 8), 3, 2, ShapeType.Square),
                    new ShapePatch(new Int2(36, 30), 3, 2, ShapeType.Square),
                },
                spawnPoints: new[]
                {
                    new Int2(32, 1),    // 0 North - waves 1 and 2
                    new Int2(1, 20),    // 1 West  - waves 2 and 3
                    new Int2(62, 20),   // 2 East  - opened by the last wave, on purpose
                },
                waves: new[]
                {
                    // Held for the player like the Quarry's first wave, but heavier: the same lesson
                    // at twice the volume, so a single drill-and-decomposer line is already tight.
                    new WaveDefinition(0f,
                        new SpawnGroup(EnemyKind.Spike, 8, 0, 2f, 2.0f),
                        new SpawnGroup(EnemyKind.Spike, 6, 0, 22f, 1.6f)),

                    // The second door, and the two groups arrive together: a line sized for the first
                    // wave holds the north group and leaks the flank.
                    new WaveDefinition(45f,
                        new SpawnGroup(EnemyKind.Spike, 10, 1, 0f, 1.5f),
                        new SpawnGroup(EnemyKind.Spike, 6, 0, 8f, 1.8f)),

                    // Three doors, so the defence has to be a ring rather than a wall - and 50 s of
                    // countdown is what pays for it.
                    new WaveDefinition(50f,
                        new SpawnGroup(EnemyKind.Spike, 12, 1, 0f, 1.3f),
                        new SpawnGroup(EnemyKind.Spike, 8, 2, 10f, 1.5f)),
                }),
        };

        /// <summary>Stamp a map's patches into the terrain. Called at world construction and on restart.</summary>
        public static void PlacePatches(TileGrid grid, ShapePatchField patches, MapDefinition map)
        {
            for (int p = 0; p < map.Patches.Length; p++)
            {
                ShapePatch patch = map.Patches[p];
                for (int y = 0; y < patch.Height; y++)
                {
                    for (int x = 0; x < patch.Width; x++)
                    {
                        var cell = new Int2(patch.Origin.X + x, patch.Origin.Y + y);
                        if (!grid.InBounds(cell)) continue;

                        // The Core wins ties: a patch placed over it would make its own tiles
                        // unbuildable and hide the thing the player is defending.
                        if (grid.Get(cell) == TileKind.Core) continue;
                        patches.Set(cell, patch.Shape);
                    }
                }
            }
        }
    }
}
