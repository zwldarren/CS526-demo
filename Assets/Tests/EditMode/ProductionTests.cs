using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the production half of the chain: the drill that mines a patch, and the
    /// decomposer that splits a circle into the two half-circles a cannon eats. Together with the
    /// Core sink they are the only source of both currency and ammunition, so a bug here is a bug
    /// in "you cannot buy damage".
    /// </summary>
    public class ProductionTests
    {
        [Test]
        public void Drill_MinesOneItemPerSecond_OfItsPatchesShape()
        {
            SimWorld world = Sim.NewWorld(20, 12);           // Core at (8,4)-(11,7)
            world.Patches.Set(new Int2(2, 2), ShapeType.Circle);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 3);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 2), Dir.East);

            Sim.Tick(world, 29);
            Assert.AreEqual(1, Sim.ItemCount(world), "a drill mines one item per second, not one per tick");

            Sim.Tick(world, 1);
            Assert.AreEqual(1, Sim.ItemCount(world), "tick 30 is still inside the first item's second");

            Sim.Tick(world, 1);
            Assert.AreEqual(2, Sim.ItemCount(world), "and the second item lands 30 ticks after the first");

            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, new Int2(3, 2)),
                "the mined item carries the patch's shape, not a generic resource");
            Assert.AreEqual(0f, Sim.ProgressAt(world, new Int2(3, 2)), Sim.ProgressTol,
                "and it enters the belt at the belt's own speed");
        }

        [Test]
        public void Drill_OnlyOnAPatch_AndRemovingItRestoresThePatch()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            var patch = new Int2(2, 9);
            var bare = new Int2(6, 9);
            world.Patches.Set(patch, ShapeType.Circle);

            Assert.IsFalse(world.TryPlace(BuildKind.Drill, bare, Dir.East), "a drill needs something to mine");
            Assert.IsFalse(world.TryPlace(BuildKind.Belt, patch, Dir.East), "a patch is not paveable");
            Assert.IsFalse(world.TryPlace(BuildKind.Decomposer, patch, Dir.East), "nor is it a machine site");
            Assert.IsFalse(world.TryPlace(BuildKind.Cannon, patch, Dir.East));

            Assert.IsTrue(world.TryPlace(BuildKind.Drill, patch, Dir.East));
            Assert.AreEqual(TileKind.Drill, world.TileGrid.Get(patch));

            Assert.IsTrue(world.TryRemoveBuilding(patch));
            Assert.AreEqual(TileKind.ShapePatch, world.TileGrid.Get(patch), "the patch is still in the ground");
            Assert.IsTrue(world.Patches.Has(patch));
            Assert.IsTrue(world.TryPlace(BuildKind.Drill, patch, Dir.East), "so it can be drilled again");
        }

        [Test]
        public void Drill_StallsWhenItsOutputIsBlocked_InsteadOfLosingItems()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            world.Patches.Set(new Int2(2, 2), ShapeType.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 2), Dir.East);   // facing bare ground

            Sim.Tick(world, 300);
            Assert.AreEqual(0, Sim.ItemCount(world), "nowhere to put an item means the drill waits");

            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Tick(world, 30);
            Assert.AreEqual(1, Sim.ItemCount(world));

            Assert.IsTrue(world.TryJamBelt(new Int2(3, 2)));
            Sim.Tick(world, 300);
            Assert.AreEqual(1, Sim.ItemCount(world), "a blocked line must not create or destroy items");
        }

        [Test]
        public void Decomposer_SplitsOneCircleIntoTwoHalves()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);        // input
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 2);        // output: (5,2) (6,2)

            Assert.IsTrue(world.TrySpawnItem(new Int2(3, 2), ShapeType.Circle));
            Sim.Tick(world, 90);

            // The circle is a second of travel plus a 1.5 s split: at t=3 the first half is on the
            // output belt and the second is still buffered behind it.
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(5, 2)),
                "a circle comes out as halves");

            Sim.Tick(world, 30);
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(6, 2)),
                "a second of belt later the first half is a cell along");
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(5, 2)),
                "and the second half is right behind it");

            Assert.AreEqual(2, Sim.ItemCount(world), "one circle became exactly two halves, no more");
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(3, 2)), "the input was eaten, not dropped");
        }

        [Test]
        public void Decomposer_SaturatesItsOutputBelt_AndCirclesQueueBehindIt()
        {
            // The bottleneck puzzle, as numbers: the split consumes 0.67 circles/s against the input
            // belt's 1/s, so circles back up behind a fed decomposer, while its halves leave at the
            // belt's own 1/s - one decomposer is one cannon's worth of full-rate ammo, no more.
            SimWorld world = Sim.NewWorld(32, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 20);       // a long tail for the halves to fill

            for (int i = 0; i < 21 * 30; i++)
            {
                world.TrySpawnItem(new Int2(3, 2), ShapeType.Circle);
                world.Tick(InputCommand.None);
            }

            int halves = 0;
            foreach (ItemSnapshot item in Sim.ItemsIn(world))
                if (item.Shape == ShapeType.HalfCircle) halves++;

            // First half at t=2.5, one per second after that: 21 seconds of feeding means 18 or so.
            Assert.GreaterOrEqual(halves, 14, "one half per second once the split is warm");
            Assert.LessOrEqual(halves, 19, "and no faster: the output belt is the ceiling");

            Assert.IsTrue(world.Belts.HasItemAt(new Int2(3, 2)),
                "circles arrive faster than the split eats them, so the input backs up");
        }

        [Test]
        public void Decomposer_HoldsItsHalves_WhenItsOutputIsBlocked()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);   // no belt in front

            Assert.IsTrue(world.TrySpawnItem(new Int2(3, 2), ShapeType.Circle));
            Sim.Tick(world, 90);

            MachineState machine = Sim.MachineAt(world, new Int2(4, 2));
            Assert.AreEqual(2, machine.OutputCount, "the split finished and the halves wait inside");
            Assert.AreEqual(ShapeType.None, machine.Carried, "the circle is consumed by the split");
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(3, 2)), "which leaves the input line free");

            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);
            Sim.Tick(world, 90);

            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(5, 2)),
                "and the halves leave once there is room");
        }

        [Test]
        public void Decomposer_JamsABeltThatDeliversAHalfCircleBack()
        {
            // The twist holds at the splitter's own input: a line that loops a half-circle back into
            // the decomposer is a wrong-shape delivery, and it jams like any other.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);

            Assert.IsTrue(world.TrySpawnItem(new Int2(3, 2), ShapeType.HalfCircle));
            Sim.Tick(world, 60);

            Assert.IsTrue(world.Belts.IsJammed(new Int2(3, 2)), "the feeding belt pays for the loop");
            Assert.AreEqual(1, world.JamCount);
            Assert.AreEqual(ShapeType.None, Sim.MachineAt(world, new Int2(4, 2)).Carried,
                "and the decomposer never swallowed it");
        }

        [Test]
        public void Decomposer_SecondOutlet_LetsTheSecondHalfLeaveToo()
        {
            // Same machine, same feed, one belt out or two. A single outlet can only ever carry one
            // half away, so the other waits inside and the decomposer is throttled to the belt's 1/s;
            // with a belt on each of two sides both halves are away within a tick of the split and the
            // machine runs at its own 1.33 halves/s. The outlets are not stored anywhere: they are the
            // belts pointing away from the machine, read fresh on every push.
            SimWorld one = FedDecomposer(secondOutlet: false);
            SimWorld two = FedDecomposer(secondOutlet: true);

            Sim.Tick(one, 90);
            Sim.Tick(two, 90);

            Assert.AreEqual(1, Sim.MachineAt(one, new Int2(4, 2)).OutputCount,
                "one outlet takes one half, so the other waits inside");
            Assert.AreEqual(1, Sim.ItemCount(one), "and only one half is out on a belt");
            Assert.AreEqual(0, Sim.MachineAt(two, new Int2(4, 2)).OutputCount,
                "a second outlet takes the other half");
            Assert.AreEqual(2, Sim.ItemCount(two), "the same one circle, both halves out");

            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(two, new Int2(5, 2)), "east outlet");
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(two, new Int2(4, 3)), "north outlet");
        }

        /// <summary>
        /// A decomposer at (4,2) fed by one circle, with its outlet east and - optionally - north too.
        /// Both outlets are one belt long and dead-ended, so a half that arrives stays where it landed.
        /// </summary>
        private static SimWorld FedDecomposer(bool secondOutlet)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);                     // input
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);                     // always: east outlet
            if (secondOutlet) Sim.LayRun(world, new Int2(4, 3), Dir.North, 1);  // belt pointing away north

            Assert.IsTrue(world.TrySpawnItem(new Int2(3, 2), ShapeType.Circle));
            return world;
        }

        [Test]
        public void Machines_ArePlacedByAimingTheDrag_AndByARotatedClick()
        {
            SimWorld world = Sim.NewWorld(24, 12);              // Core at (10,4)-(13,7)
            world.Patches.Set(new Int2(3, 10), ShapeType.Circle);

            // A drag aims: the machine lands where the press was and faces the way the drag went.
            world.Tick(new InputCommand(true, false, new Int2(3, 10),
                primaryPressed: true, selected: BuildKind.Drill));
            world.Tick(new InputCommand(true, false, new Int2(6, 10)));
            world.Tick(new InputCommand(false, false, new Int2(6, 10), primaryReleased: true, selected: BuildKind.Drill));

            Assert.IsTrue(world.Machines.Has(new Int2(3, 10)), "the drill landed on the press cell");
            Assert.AreEqual(Dir.East, Sim.MachineAt(world, new Int2(3, 10)).Direction, "aimed east by the drag");
            Assert.IsFalse(world.Machines.Has(new Int2(6, 10)), "and nowhere else: a machine is one tile");

            // A click places with the ghost's facing, and Q/E turn the ghost.
            world.Tick(new InputCommand(cursorCell: new Int2(0, 0), selected: BuildKind.Cannon, rotateSteps: 1));
            Assert.AreEqual(Dir.South, world.PlacementDirection, "one clockwise step from the default east");

            world.Tick(new InputCommand(true, false, new Int2(16, 10),
                primaryPressed: true, selected: BuildKind.Cannon));
            world.Tick(new InputCommand(false, false, new Int2(16, 10), primaryReleased: true, selected: BuildKind.Cannon));

            Assert.AreEqual(Dir.South, Sim.MachineAt(world, new Int2(16, 10)).Direction);

            // And placement rules still hold through the input path: a turret may not sit on a patch.
            world.Tick(new InputCommand(false, false, new Int2(3, 9), selected: BuildKind.Cannon));
            Assert.IsFalse(world.CanPlace(BuildKind.Cannon, new Int2(3, 10)));
            Assert.IsFalse(world.TryPlace(BuildKind.Cannon, new Int2(3, 10), Dir.East));
        }
    }
}
