using System;

namespace Facet.Core
{
    /// <summary>
    /// The whole logic layer. Deterministic, engine-free, advanced exactly once per fixed tick
    /// by the host. Nothing here knows about Unity, time.DeltaTime, or frame rates.
    ///
    /// The tick order is the design's causal order, and each step exists because the one after it
    /// needs its result:
    ///
    ///   1. build commands     - what the player placed this tick (and paid for)
    ///   2. belts              - items advance and hand off
    ///   3. production         - drills mine, decomposers split (they read step 2's arrivals)
    ///   4. transit            - pipes carry, splitters balance (they read step 2's arrivals)
    ///   5. core sink          - deliveries into the Core bank as circles
    ///   6. turrets            - eat, jam, fire (they read the arrivals and the belt queue)
    ///   7. projectiles        - fly and burst
    ///   8. enemies            - walk and hit the Core
    ///   9. waves              - spawn, and decide whether the wave or the run is over
    /// </summary>
    public sealed class SimWorld
    {
        public readonly TileGrid TileGrid;
        public readonly ShapePatchField Patches;
        public readonly BeltField Belts;
        public readonly MachineField Machines;
        public readonly EnemyField Enemies;
        public readonly ProjectileField Projectiles;
        public readonly WaveDirector Waves;
        public readonly SimConfig Config;
        public readonly EconomyState Economy;

        /// <summary>The map this run is played on. Fixed for the world's lifetime.</summary>
        public MapDefinition Map { get; }

        /// <summary>Ticks elapsed since the run started. The clock everything else reads.</summary>
        public int TickCount { get; private set; }

        /// <summary>Paused worlds do not advance and do not accept build commands.</summary>
        public bool Paused;

        /// <summary>Playing until the Core falls or the last wave is cleared.</summary>
        public GameStatus Status { get; private set; }

        /// <summary>The defended object, and the bank. Losing it loses the run.</summary>
        public CoreState Core;

        /// <summary>Which building the player has selected right now.</summary>
        public BuildKind SelectedKind => _build.Selected;

        /// <summary>Which way the ghost under the cursor faces, so the view can draw the same facing the
        /// simulation would use: the run's own direction while a belt is being dragged, the placement
        /// ghost's otherwise.</summary>
        public Dir PlacementDirection => _build.PlacementDirection;

        private readonly ProductionSystem _production;
        private readonly TransitSystem _transit;
        private readonly CoreSinkSystem _sink;
        private readonly TurretSystem _turrets;
        private readonly BuildController _build;

        /// <summary>A start-wave request that has not been applied yet. Set between ticks, consumed
        /// by <see cref="Tick"/>.</summary>
        private bool _waveRequested;

        public SimWorld(MapDefinition map, SimConfig config)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Config = config ?? throw new ArgumentNullException(nameof(config));

            TileGrid = new TileGrid(map.Width, map.Height);
            Patches = new ShapePatchField(TileGrid);
            Belts = new BeltField(TileGrid);
            Machines = new MachineField(TileGrid, Patches);
            Enemies = new EnemyField();
            Projectiles = new ProjectileField();
            Waves = new WaveDirector(Enemies, map);
            Economy = new EconomyState(map.StartCircles);

            _production = new ProductionSystem(TileGrid, Patches, Belts, Machines);
            _transit = new TransitSystem(TileGrid, Belts, Machines);
            _sink = new CoreSinkSystem(TileGrid, Belts, Economy);
            _turrets = new TurretSystem(TileGrid, Belts, Machines, Enemies, Projectiles);
            _build = new BuildController(TileGrid, Belts, Machines, Economy);

            PlaceCore(CoreOrigin(TileGrid));
            Maps.PlacePatches(TileGrid, Patches, map);
            Status = GameStatus.Playing;
        }

        /// <summary>Advance one fixed step. Call at exactly SimConfig.TickRate Hz.</summary>
        public void Tick(InputCommand cmd)
        {
            // Restart is honoured even while paused or after the run ended: it is the only way back.
            if (cmd.RestartPressed)
            {
                Restart();
                return;
            }

            if (Paused || Status != GameStatus.Playing)
            {
                // A request that arrives while the world is stopped is dropped, not queued: pausing
                // and clicking the button in the same breath must not start a wave on resume.
                _waveRequested = false;
                return;
            }

            _build.Apply(cmd);

            Belts.Step(SimConfig.TickDt, Config.BeltSpeed);
            _production.Step(SimConfig.TickDt);
            _transit.Step(SimConfig.TickDt);
            _sink.Step(Core.Cell);
            _turrets.Step(SimConfig.TickDt);
            Projectiles.Step(SimConfig.TickDt, Enemies);
            Enemies.Step(SimConfig.TickDt, ref Core);
            Waves.Step(SimConfig.TickDt);

            // The N key and the HUD's button, applied *after* the wave step: on the tick a wave's last
            // enemy dies, step 9 is what ends the wave, so a request arriving in that same tick would
            // find a wave still running and be swallowed. Here it starts the intermission step 9 just
            // opened. A wave that is actually running ignores the request, and so does a finished run,
            // so this stays exactly the "start it now, only once" the design asks for. The countdown
            // path is untouched: start-early is this line's only job.
            if (cmd.StartWavePressed || _waveRequested)
            {
                _waveRequested = false;
                Waves.StartNextWave();
            }

            if (!Core.Alive) Status = GameStatus.Lost;
            else if (Waves.Finished) Status = GameStatus.Won;

            TickCount++;
        }

        /// <summary>Wipe the run and rebuild it from the map's own data, keeping this object alive so
        /// every view that holds a reference to the world keeps working.</summary>
        public void Restart()
        {
            TileGrid.Clear();

            Machines.Clear();
            Belts.Clear();
            Enemies.Clear();
            Projectiles.Clear();

            PlaceCore(CoreOrigin(TileGrid));
            Maps.PlacePatches(TileGrid, Patches, Map);

            Waves.Reset();
            _turrets.Reset();
            _build.Reset();
            Economy.Reset(Map.StartCircles);

            Paused = false;
            Status = GameStatus.Playing;
            TickCount = 0;
            _waveRequested = false;
        }

        /// <summary>Start the next wave immediately, if there is one. The N key and the HUD's button
        /// go through <see cref="RequestNextWave"/> instead, so the ordering rule lives in one place;
        /// this direct entry point is what the tests drive.</summary>
        public void StartNextWave() => Waves.StartNextWave();

        /// <summary>Ask for the next wave on the next tick. The HUD's button calls this because a
        /// click happens between ticks and this world is the only thing that knows the tick order.</summary>
        public void RequestNextWave() => _waveRequested = true;

        public bool CanPlace(BuildKind kind, Int2 cell) => _build.CanPlace(kind, cell);

        /// <summary>Structural rules AND the stockpile both allow this placement - the cursor
        /// ghost's colour reads this, so an unaffordable building shows before the click.</summary>
        public bool CanAffordPlace(BuildKind kind, Int2 cell)
            => _build.CanPlace(kind, cell) && Economy.CanAfford(kind);

        public bool TryPlace(BuildKind kind, Int2 cell, Dir direction) => _build.TryPlace(kind, cell, direction);

        /// <summary>Clear a jam or remove a building (refunding it), the way right-click does.</summary>
        public bool TryRemoveBuilding(Int2 cell) => _build.Remove(cell);

        public bool CanPlaceBelt(Int2 cell) => _build.CanPlace(BuildKind.Belt, cell);

        public bool TryPlaceBelt(Int2 cell, Dir direction) => _build.TryPlace(BuildKind.Belt, cell, direction);

        /// <summary>Low-level belt removal with no refund - the test scaffolding's tool. Players go
        /// through <see cref="TryRemoveBuilding"/>, which pays back.</summary>
        public bool TryRemoveBelt(Int2 cell) => Belts.TryRemove(cell);

        /// <summary>Block a belt cell. Turrets call this on a wrong-shape delivery; so do tests.</summary>
        public bool TryJamBelt(Int2 cell) => Belts.TryJam(cell);

        public bool TryClearJam(Int2 cell) => Belts.TryClearJam(cell);

        /// <summary>Drop a shape onto a belt cell. Drills use this; so do the tests.</summary>
        public bool TrySpawnItem(Int2 cell, ShapeType shape) => Belts.TrySpawnItem(cell, shape);

        /// <summary>How many belt segments are blocked right now.</summary>
        public int JamCount => Belts.JamCount;

        /// <summary>Turrets built, and how many of them have ammo waiting.</summary>
        public void CountTurrets(out int total, out int armed) => Machines.CountTurrets(out total, out armed);

        /// <summary>Shots fired this run. Read by the tests and available to a HUD.</summary>
        public int ShotsFired => _turrets.ShotsFired;

        private void PlaceCore(Int2 cell)
        {
            // The whole footprint is Core tiles, so nothing can be built on or under it.
            for (int y = 0; y < CoreState.Size; y++)
            {
                for (int x = 0; x < CoreState.Size; x++)
                {
                    TileGrid.Set(new Int2(cell.X + x, cell.Y + y), TileKind.Core);
                }
            }

            Core = new CoreState { Cell = cell, Hp = Config.CoreMaxHp, MaxHp = Config.CoreMaxHp };
        }

        /// <summary>Lower-left tile of the Core's footprint: the block centred on the map.</summary>
        private static Int2 CoreOrigin(TileGrid grid)
        {
            int x = Math.Clamp(grid.Width / 2 - CoreState.Size / 2, 0, Math.Max(0, grid.Width - CoreState.Size));
            int y = Math.Clamp(grid.Height / 2 - CoreState.Size / 2, 0, Math.Max(0, grid.Height - CoreState.Size));
            return new Int2(x, y);
        }
    }
}
