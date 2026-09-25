using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the engine-free simulation. No scene, no Play mode, milliseconds to run.
    ///
    /// Two invariants matter more than the rest and are worth guarding forever:
    ///   * the tick rate is exactly 30 Hz, so balance numbers in the design doc stay true;
    ///   * the simulation is deterministic, so a save + the same input must replay identically.
    /// </summary>
    public class SimulationTests
    {
        private const float Tol = 1e-4f;

        private static SimWorld NewWorld() => new SimWorld(new TileGrid(64, 36), new SimConfig());

        private static void Tick(SimWorld world, Vec2 move, int count)
        {
            var cmd = new InputCommand(move, false, false, Int2.Zero);
            for (int i = 0; i < count; i++) world.Tick(cmd);
        }

        [Test]
        public void TickRate_IsExactly30Hz()
        {
            Assert.AreEqual(30f, SimConfig.TickRate, Tol);
            Assert.AreEqual(1f / 30f, SimConfig.TickDt, 1e-9f);
        }

        [Test]
        public void HoldingEast_ForOneSecond_MovesExactlyRigSpeed()
        {
            var world = NewWorld();
            Vec2 start = world.Rig.Position;

            Tick(world, new Vec2(1f, 0f), 30);

            Assert.AreEqual(30, world.TickCount);
            Assert.AreEqual(1f, world.ElapsedSeconds, Tol);
            Assert.AreEqual(start.X + 4f, world.Rig.Position.X, Tol, "1 s of movement must equal RigSpeed in tiles");
            Assert.AreEqual(start.Y, world.Rig.Position.Y, Tol);
        }

        [Test]
        public void DiagonalInput_IsNotFasterThanCardinalInput()
        {
            var world = NewWorld();
            Vec2 start = world.Rig.Position;

            Tick(world, new Vec2(1f, 1f), 30);

            Assert.AreEqual(4f, Vec2.Distance(start, world.Rig.Position), 1e-3f);
        }

        [Test]
        public void Wall_StopsOnBlockedAxis_AndSlidesAtComponentSpeed()
        {
            var world = NewWorld();
            world.SpawnRigAt(new Vec2(world.TileGrid.Width - 0.5f, 18f));
            var diagonal = new Vec2(1f, 1f);

            Tick(world, diagonal, 60);

            Assert.AreEqual(world.TileGrid.Width - world.Config.RigRadius, world.Rig.Position.X, Tol, "must stop at the wall");

            // Sliding keeps the projected component (4 * 0.7071 tiles/s), not full speed.
            float expectedY = 18f + 60 * SimConfig.TickDt * world.Config.RigSpeed * 0.70710678f;
            Assert.AreEqual(expectedY, world.Rig.Position.Y, 1e-3f);
        }

        [Test]
        public void Rig_NeverLeavesTheMap()
        {
            var world = NewWorld();
            world.SpawnRigAt(world.TileGrid.CellCenter(new Int2(1, 1)));

            for (int i = 0; i < 600; i++)
            {
                var cmd = new InputCommand(new Vec2(-1f, -1f), false, false, Int2.Zero);
                world.Tick(cmd);

                Assert.GreaterOrEqual(world.Rig.Position.X, world.Config.RigRadius - Tol);
                Assert.GreaterOrEqual(world.Rig.Position.Y, world.Config.RigRadius - Tol);
            }
        }

        [Test]
        public void PausedWorld_AcceptsInputButDoesNotAdvance()
        {
            var world = NewWorld();
            Vec2 position = world.Rig.Position;
            int ticks = world.TickCount;

            world.Paused = true;
            Tick(world, new Vec2(1f, 0f), 10);

            Assert.AreEqual(ticks, world.TickCount);
            Assert.AreEqual(0f, Vec2.Distance(position, world.Rig.Position), Tol);
        }

        [Test]
        public void SameCommandsFromSameStart_ReplayIdentically()
        {
            var a = NewWorld();
            var b = NewWorld();

            for (int i = 0; i < 137; i++)
            {
                var cmd = new InputCommand(new Vec2((i % 7) - 3, (i % 5) - 2), false, false, Int2.Zero);
                a.Tick(cmd);
                b.Tick(cmd);
            }

            Assert.AreEqual(0f, Vec2.Distance(a.Rig.Position, b.Rig.Position), 0f);
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

        [Test]
        public void Grid_ClampToBounds_KeepsRadiusInsideTheMap()
        {
            var grid = new TileGrid(64, 36);

            Assert.AreEqual(new Vec2(0.5f, 0.5f), grid.ClampToBounds(new Vec2(-10f, -10f), 0.5f));
            Assert.AreEqual(new Vec2(63.5f, 35.5f), grid.ClampToBounds(new Vec2(999f, 999f), 0.5f));
            Assert.AreEqual(new Vec2(20f, 20f), grid.ClampToBounds(new Vec2(20f, 20f), 0.5f));
        }
    }
}
