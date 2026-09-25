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
            var expected = new Int2(world.TileGrid.Width / 2, world.TileGrid.Height / 2);

            Assert.AreEqual(expected, world.Core.Cell, "the Core is the defended object, dead centre");
            Assert.AreEqual(world.Config.CoreMaxHp, world.Core.Hp, Sim.Tol);
            Assert.AreEqual(1f, world.Core.HealthFraction, Sim.Tol);
            Assert.IsTrue(world.Core.Alive);

            Assert.AreEqual(TileKind.Core, world.TileGrid.Get(expected));
            Assert.IsFalse(world.CanPlaceBelt(expected), "nothing may be built on the Core");
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
                a.TrySpawnItem(new Int2(2, 2), ShapeType.Triangle);
                b.TrySpawnItem(new Int2(2, 2), ShapeType.Triangle);
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
        public void Grid_OccupiedTileIsNotBuildable()
        {
            var grid = new TileGrid(8, 4);
            var cell = new Int2(2, 2);

            Assert.IsTrue(grid.IsBuildable(cell));

            grid.Set(cell, TileKind.Belt);

            Assert.AreEqual(TileKind.Belt, grid.Get(cell));
            Assert.IsFalse(grid.IsBuildable(cell));
        }
    }
}
