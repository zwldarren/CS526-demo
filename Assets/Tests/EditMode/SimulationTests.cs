using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the engine-free simulation itself: the clock, the map, the defended
    /// object, and the two invariants worth guarding forever - the tick rate is exactly 30 Hz so
    /// the balance numbers in the design doc stay true, and the simulation is deterministic so a
    /// save plus the same input must replay identically.
    /// </summary>
    public class SimulationTests
    {
        [Test]
        public void TickRate_IsExactly30Hz()
        {
            Assert.AreEqual(30f, SimConfig.TickRate, Sim.Tol);
            Assert.AreEqual(1f / 30f, SimConfig.TickDt, 1e-9f);
        }

        [Test]
        public void Core_SitsAtTheMapCentre_AtFullHealth_AndIsNotBuildable()
        {
            SimWorld world = Sim.NewWorld();
            float mapCentreX = world.TileGrid.Width * 0.5f;
            float mapCentreY = world.TileGrid.Height * 0.5f;

            Assert.AreEqual(mapCentreX, world.Core.Center.X, Sim.Tol, "the Core is the defended object, dead centre");
            Assert.AreEqual(mapCentreY, world.Core.Center.Y, Sim.Tol);
            Assert.AreEqual(world.Config.CoreMaxHp, world.Core.Hp, Sim.Tol);
            Assert.AreEqual(1f, world.Core.HealthFraction, Sim.Tol);
            Assert.IsTrue(world.Core.Alive);

            for (int y = 0; y < CoreState.Size; y++)
            {
                for (int x = 0; x < CoreState.Size; x++)
                {
                    var cell = new Int2(world.Core.Cell.X + x, world.Core.Cell.Y + y);
                    Assert.AreEqual(TileKind.Core, world.TileGrid.Get(cell), cell + " is part of the Core's footprint");
                    Assert.IsFalse(world.CanPlaceBelt(cell), "nothing may be built on the Core: " + cell);
                }
            }

            var beside = new Int2(world.Core.Cell.X + CoreState.Size, world.Core.Cell.Y);
            Assert.IsTrue(world.CanPlaceBelt(beside), "the tile just past the Core's footprint is free: " + beside);
        }

        [Test]
        public void PausedWorld_DoesNotAdvance()
        {
            SimWorld world = Sim.NewWorld();
            int ticks = world.TickCount;

            world.Paused = true;
            Sim.Tick(world, 10);

            Assert.AreEqual(ticks, world.TickCount);
        }

        [Test]
        public void Selection_ChangesWhilePaused_SoTheBuildBarStaysLive()
        {
            // A pause is exactly when a player re-plans, so the HUD's build bar has to keep working
            // while the world is held. Selection is not a world change - it is the one input the
            // paused branch of the tick still applies - which is what keeps the highlighted tile
            // following the click instead of freezing the moment the game does.
            SimWorld world = Sim.NewWorld();

            world.Paused = true;
            world.Tick(new InputCommand(selected: BuildKind.Mortar));

            Assert.AreEqual(BuildKind.Mortar, world.SelectedKind, "the palette selected while paused");
            Assert.AreEqual(0, world.TickCount, "and nothing else advanced");
        }

        [Test]
        public void RestartRequested_RebuildsTheRun_OnTheNextTick()
        {
            // The HUD's restart button cannot reach into the middle of a run any more than its
            // start-wave button can, so it asks the world and the request lands on a tick boundary
            // like every other input. This is R's path with the key taken out.
            SimWorld world = Sim.NewWorld(20, 12);
            Assert.IsTrue(world.TryPlaceBelt(new Int2(5, 5), Dir.East));
            Assert.Less(world.Economy.Circles, world.Map.StartCircles, "the belt was paid for");

            world.RequestRestart();
            world.Tick(InputCommand.None);

            Assert.IsFalse(world.Belts.Has(new Int2(5, 5)), "the line is gone");
            Assert.AreEqual(world.Map.StartCircles, world.Economy.Circles, "and the stockpile is back to the start");
            Assert.AreEqual(0, world.TickCount, "the run is at tick zero again");
            Assert.AreEqual(GameStatus.Playing, world.Status);
        }

        [Test]
        public void SameCommandsFromSameStart_ReplayIdentically()
        {
            SimWorld a = Sim.NewWorld(24, 12);
            SimWorld b = Sim.NewWorld(24, 12);

            // An identical mix of belt laying, item spawning and stepping.
            for (int i = 0; i < 40; i++)
            {
                var cmd = new InputCommand(true, false, new Int2(2 + (i % 9), 1 + (i % 5)));
                a.Tick(cmd);
                b.Tick(cmd);
            }

            for (int i = 0; i < 20; i++)
            {
                a.TrySpawnItem(new Int2(2, 2), ShapeType.Circle);
                b.TrySpawnItem(new Int2(2, 2), ShapeType.Circle);
                Sim.Tick(a, 7);
                Sim.Tick(b, 7);
            }

            var itemsA = Sim.ItemsIn(a);
            var itemsB = Sim.ItemsIn(b);

            Assert.AreEqual(itemsA.Count, itemsB.Count, "identical input must produce identical state");
            for (int i = 0; i < itemsA.Count; i++)
            {
                Assert.AreEqual(itemsA[i].Shape, itemsB[i].Shape, "item " + i + " diverged");
                Assert.AreEqual(0f, Vec2.Distance(itemsA[i].Position, itemsB[i].Position), 0f,
                    "item " + i + " drifted");
            }
        }

        [Test]
        public void Grid_ConvertsBetweenCellsAndWorldSpace()
        {
            var grid = new TileGrid(8, 4);

            Assert.AreEqual(new Int2(0, 0), grid.CellAt(new Vec2(0.1f, 0.9f)));
            Assert.AreEqual(new Int2(3, 2), grid.CellAt(new Vec2(3.99f, 2.01f)));
            Assert.AreEqual(new Vec2(3.5f, 2.5f), grid.CellCenter(new Int2(3, 2)));
            Assert.IsFalse(grid.InBounds(new Int2(8, 0)));
            Assert.IsFalse(grid.InBounds(new Int2(-1, 2)));
        }

        [Test]
        public void Grid_ABeltGoesOnGroundOrOre_ButNotOnABuilding()
        {
            var grid = new TileGrid(8, 4);
            var cell = new Int2(2, 2);

            Assert.IsTrue(grid.CanLayBelt(cell), "bare ground takes a belt");

            grid.Set(cell, TileKind.ShapePatch);
            Assert.IsTrue(grid.CanLayBelt(cell), "and so does a shape patch: transport crosses ore");
            Assert.IsFalse(grid.IsOccupied(cell), "but ore is not a building");

            grid.Set(cell, TileKind.Belt);

            Assert.AreEqual(TileKind.Belt, grid.Get(cell));
            Assert.IsTrue(grid.IsOccupied(cell));
            Assert.IsFalse(grid.CanLayBelt(cell), "an existing belt is in the way");
        }
    }
}
