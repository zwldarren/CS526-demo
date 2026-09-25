using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>Shared scaffolding for the EditMode tests. Deliberately tiny - the simulation
    /// needs no scene, no Play mode and no fixtures, so these are just conveniences.</summary>
    internal static class Sim
    {
        public const float Tol = 1e-4f;

        /// <summary>Tolerance for comparing a belt item's progress against a computed value.</summary>
        public const float ProgressTol = 1e-3f;

        public static SimWorld NewWorld(int width = 64, int height = 36)
            => new SimWorld(new TileGrid(width, height), new SimConfig());

        public static void Tick(SimWorld world, InputCommand cmd, int count)
        {
            for (int i = 0; i < count; i++) world.Tick(cmd);
        }

        public static void Tick(SimWorld world, int count) => Tick(world, InputCommand.None, count);

        /// <summary>Press the left button on a cell, hold it and drag the cursor to another.</summary>
        public static void Drag(SimWorld world, Int2 from, Int2 to)
        {
            world.Tick(new InputCommand(true, false, from));
            world.Tick(new InputCommand(true, false, to));
            world.Tick(InputCommand.None);
        }

        /// <summary>Lay a straight run of belts, asserting every cell was free.</summary>
        public static void LayRun(SimWorld world, Int2 start, Dir direction, int length)
        {
            Int2 cell = start;
            for (int i = 0; i < length; i++)
            {
                Assert.IsTrue(world.TryPlaceBelt(cell, direction), "expected " + cell + " to be free");
                cell = cell + direction.Offset();
            }
        }

        public static Dir DirAt(SimWorld world, Int2 cell)
        {
            Assert.IsTrue(world.Belts.TryGet(cell, out BeltState state), "expected a belt at " + cell);
            return state.Direction;
        }

        public static float ProgressAt(SimWorld world, Int2 cell)
        {
            Assert.IsTrue(world.Belts.TryGet(cell, out BeltState state), "expected a belt at " + cell);
            return state.Progress;
        }

        public static List<ItemSnapshot> ItemsIn(SimWorld world)
        {
            var items = new List<ItemSnapshot>();
            world.Belts.GetItems(items);
            return items;
        }

        public static int ItemCount(SimWorld world) => ItemsIn(world).Count;
    }
}
