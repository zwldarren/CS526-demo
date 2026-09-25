using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the belt layer - the mechanic the whole design hangs off.
    ///
    /// The two that matter most are <see cref="PackedBelt_ShiftsAsATrain_InsteadOfDeadlocking"/>
    /// and <see cref="Throughput_IsTheSameFacingAnyDirection"/>. A belt packed full is the case
    /// where every item reaches its cell's exit edge on the same tick and every successor is
    /// occupied, and it is exactly where a naive implementation either deadlocks outright or
    /// silently drains at one item per tick. If those two go red, the game is lying about its
    /// own throughput, which is the one number the design asks the player to reason with.
    /// </summary>
    public class BeltTests
    {

        [Test]
        public void Item_TakesExactlyOneSecondPerCell()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 5);

            Assert.IsTrue(world.TrySpawnItem(new Int2(1, 1), ShapeType.Triangle));

            Sim.Tick(world, 29);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(1, 1)), "still on its first cell after 29 ticks");
            Assert.AreEqual(29f / 30f, Sim.ProgressAt(world, new Int2(1, 1)), Sim.ProgressTol);

            Sim.Tick(world, 1);
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(1, 1)), "one second per cell: it left on tick 30");
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(2, 1)));
            Assert.AreEqual(0f, Sim.ProgressAt(world, new Int2(2, 1)), Sim.ProgressTol);
        }

        [Test]
        public void PackedBelt_ShiftsAsATrain_InsteadOfDeadlocking()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 8);   // (1,1) .. (8,1)

            // Pack cells 1..7 and leave the head cell empty, so the train has somewhere to go.
            for (int x = 1; x <= 7; x++)
                Assert.IsTrue(world.TrySpawnItem(new Int2(x, 1), ShapeType.Square));

            Sim.Tick(world, 30);

            Assert.IsFalse(world.Belts.HasItemAt(new Int2(1, 1)), "the tail cell must empty");
            for (int x = 2; x <= 8; x++)
                Assert.IsTrue(world.Belts.HasItemAt(new Int2(x, 1)),
                    "cell (" + x + ",1) should have taken its predecessor's item in the same tick");

            Assert.AreEqual(7, Sim.ItemCount(world), "no item may be lost or duplicated");
        }

        [Test]
        public void Throughput_IsTheSameFacingAnyDirection()
        {
            // Guards against iteration order leaking into the result: if the sweep order favoured
            // one flow direction, these four would disagree.
            foreach (Dir dir in new[] { Dir.North, Dir.East, Dir.South, Dir.West })
            {
                SimWorld world = Sim.NewWorld(20, 20);        // Core sits at (10,10)
                var head = new Int2(6, 6);
                Sim.LayRun(world, head, dir, 6);

                Int2 cell = head;
                for (int i = 0; i < 5; i++)
                {
                    Assert.IsTrue(world.TrySpawnItem(cell, ShapeType.Circle));
                    cell = cell + dir.Offset();
                }

                Sim.Tick(world, 30);

                Assert.IsFalse(world.Belts.HasItemAt(head), dir + ": the tail cell must empty");
                Assert.IsTrue(world.Belts.HasItemAt(head + dir.Offset() * 5),
                    dir + ": the train must reach the free head cell");
                Assert.AreEqual(5, Sim.ItemCount(world), dir + ": no item may be lost");
            }
        }

        [Test]
        public void ItemPositions_StepNormallyAcrossAHandoff()
        {
            // Progress 1 of a cell and progress 0 of its successor are the same world point, so a
            // hand-off must cost exactly one ordinary tick of travel - not half a tile.
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 3);
            Assert.IsTrue(world.TrySpawnItem(new Int2(1, 1), ShapeType.Triangle));

            Sim.Tick(world, 29);
            Sim.Tick(world, 1);

            ItemSnapshot item = Sim.ItemsIn(world)[0];
            Assert.AreEqual(1f / 30f, Vec2.Distance(item.PreviousPosition, item.Position), Sim.ProgressTol,
                "a hand-off must move the item one normal tick step, not half a tile");

            Vec2 boundary = world.TileGrid.CellCenter(new Int2(1, 1)) + Dir.East.ToVec() * 0.5f;
            Assert.AreEqual(0f, Vec2.Distance(boundary, item.Position), Sim.ProgressTol,
                "and it should be sitting on the shared edge");
        }

        [Test]
        public void BackPressure_ABlockedItemNeverEntersTheCellAheadOfIt()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 4);   // (1,1) .. (4,1)

            Assert.IsTrue(world.TrySpawnItem(new Int2(4, 1), ShapeType.Triangle));  // head, nowhere to go
            Assert.IsTrue(world.TrySpawnItem(new Int2(3, 1), ShapeType.Square));

            Sim.Tick(world, 120);

            Assert.IsTrue(world.Belts.TryGet(new Int2(4, 1), out BeltState head));
            Assert.AreEqual(ShapeType.Triangle, head.Item, "the two items must not swap");
            Assert.AreEqual(1f, head.Progress, Sim.ProgressTol);

            Assert.IsTrue(world.Belts.TryGet(new Int2(3, 1), out BeltState queued));
            Assert.AreEqual(ShapeType.Square, queued.Item);
            Assert.AreEqual(1f, queued.Progress, Sim.ProgressTol, "queued on the boundary behind it");

            Assert.AreEqual(2, Sim.ItemCount(world));
        }

        [Test]
        public void Item_TurnsAtACorner()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Assert.IsTrue(world.TryPlaceBelt(new Int2(1, 1), Dir.East));
            Assert.IsTrue(world.TryPlaceBelt(new Int2(2, 1), Dir.East));
            Assert.IsTrue(world.TryPlaceBelt(new Int2(3, 1), Dir.North));   // the corner
            Assert.IsTrue(world.TryPlaceBelt(new Int2(3, 2), Dir.North));
            Assert.IsTrue(world.TryPlaceBelt(new Int2(3, 3), Dir.North));

            Assert.IsTrue(world.TrySpawnItem(new Int2(1, 1), ShapeType.Triangle));

            Sim.Tick(world, 60);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(3, 1)), "three cells in: the corner");

            Sim.Tick(world, 30);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(3, 2)), "and it turned north rather than stalling");
        }

        [Test]
        public void Jam_FreezesTheLine_AndClearingItReleasesIt()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 5);
            Assert.IsTrue(world.TrySpawnItem(new Int2(1, 1), ShapeType.Triangle));
            Assert.IsTrue(world.TryJamBelt(new Int2(3, 1)));

            Sim.Tick(world, 90);

            Assert.IsTrue(world.Belts.IsJammed(new Int2(3, 1)));
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(3, 1)), "nothing reaches the jammed cell");

            Assert.IsTrue(world.Belts.HasItemAt(new Int2(2, 1)), "the line backed up behind the jam");
            Assert.AreEqual(1f, Sim.ProgressAt(world, new Int2(2, 1)), Sim.ProgressTol);

            Assert.IsTrue(world.TryClearJam(new Int2(3, 1)));
            Sim.Tick(world, 1);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(3, 1)), "clearing the jam lets the line move again");
        }

        [Test]
        public void ClearingAJam_DestroysTheItemThatCausedIt()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 3);
            Assert.IsTrue(world.TrySpawnItem(new Int2(2, 1), ShapeType.Circle));
            Assert.IsTrue(world.TryJamBelt(new Int2(2, 1)));

            Sim.Tick(world, 10);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(2, 1)), "a jammed cell keeps its item where it stood");

            Assert.IsTrue(world.TryClearJam(new Int2(2, 1)));

            Assert.IsFalse(world.Belts.HasItemAt(new Int2(2, 1)),
                "leaving the offending item in place would re-jam the same cell next tick");
            Assert.IsFalse(world.Belts.IsJammed(new Int2(2, 1)));
        }

        [Test]
        public void JammedCell_RefusesNewItems()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);
            Assert.IsTrue(world.TryJamBelt(new Int2(2, 1)));

            Assert.IsFalse(world.TrySpawnItem(new Int2(2, 1), ShapeType.Triangle));
        }

        [Test]
        public void Drag_BuildsAStraightRunFacingTheDragDirection()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.Drag(world, new Int2(2, 1), new Int2(2, 4));

            for (int y = 1; y <= 4; y++)
                Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(2, y)),
                    "the run should face the way the cursor was dragged");

            Assert.IsFalse(world.Belts.Has(new Int2(2, 0)), "the run must not bleed past the press cell");
            Assert.IsFalse(world.Belts.Has(new Int2(2, 5)), "nor past the cursor");
        }

        [Test]
        public void Drag_TurnsTheCorner_WhenTheCursorJumpsDiagonally()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.Drag(world, new Int2(1, 1), new Int2(4, 3));

            // Manhattan, X first: a run east along y=1, then a run north up x=4.
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(1, 1)));
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(2, 1)));
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(3, 1)));
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(4, 1)), "the corner turns north");
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(4, 2)));
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(4, 3)));
        }

        [Test]
        public void Drag_SkipsABlockedCell_WithoutRepointingIt()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Assert.IsTrue(world.TryPlaceBelt(new Int2(3, 1), Dir.South));   // laid before the drag

            Sim.Drag(world, new Int2(1, 1), new Int2(5, 1));

            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(1, 1)));
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(2, 1)));
            Assert.AreEqual(Dir.South, Sim.DirAt(world, new Int2(3, 1)),
                "the drag must not rotate a belt it did not lay");
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(4, 1)), "and it resumes on the far side");
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(5, 1)));
        }

        [Test]
        public void PressingOntoAnExistingRun_PointsItTheWayYouDrag()
        {
            // Drawing a run in two segments is normal play, so pressing the end of a run and
            // dragging on has to turn that corner instead of leaving it facing the old way.
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 3);   // (1,1) (2,1) (3,1), all East

            Sim.Drag(world, new Int2(3, 1), new Int2(3, 3));

            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(3, 1)), "the corner must turn");
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(3, 2)));
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(3, 3)));
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(2, 1)), "the rest of the run is untouched");
        }

        [Test]
        public void ABareClickLaysNothing_BecauseABeltNeedsADirection()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.Tick(world, new InputCommand(true, false, new Int2(2, 2)), 10);
            Sim.Tick(world, 1);

            Assert.IsFalse(world.Belts.Has(new Int2(2, 2)));
            Assert.AreEqual(0, Sim.ItemCount(world));
        }

        [Test]
        public void RightDrag_ErasesTheRunItSweeps()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 4);

            var erase = new InputCommand(false, true, new Int2(2, 1));
            Sim.Tick(world, erase, 2);
            world.Tick(new InputCommand(false, true, new Int2(3, 1)));
            Sim.Tick(world, 1);

            Assert.IsFalse(world.Belts.Has(new Int2(2, 1)));
            Assert.IsFalse(world.Belts.Has(new Int2(3, 1)));
            Assert.IsTrue(world.Belts.Has(new Int2(1, 1)), "only what the cursor swept is gone");
            Assert.IsTrue(world.Belts.Has(new Int2(4, 1)));
        }

        [Test]
        public void Belt_RefusesOutOfBoundsOccupiedAndCoreCells()
        {
            SimWorld world = Sim.NewWorld(16, 10);

            Assert.IsFalse(world.TryPlaceBelt(new Int2(-1, 6), Dir.East));
            Assert.IsFalse(world.TryPlaceBelt(new Int2(16, 6), Dir.East));
            Assert.IsFalse(world.TryPlaceBelt(world.Core.Cell, Dir.East), "the Core is not buildable");

            Assert.IsTrue(world.TryPlaceBelt(new Int2(2, 2), Dir.East));
            Assert.IsFalse(world.TryPlaceBelt(new Int2(2, 2), Dir.East), "no stacking belts");
        }

        [Test]
        public void RemovingABelt_DestroysTheItemRidingIt()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);
            Assert.IsTrue(world.TrySpawnItem(new Int2(1, 1), ShapeType.Square));

            Assert.IsTrue(world.TryRemoveBelt(new Int2(1, 1)));

            Assert.IsFalse(world.Belts.Has(new Int2(1, 1)));
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(1, 1)));
            Assert.AreEqual(0, Sim.ItemCount(world));
        }

        [Test]
        public void Item_RefusesOccupiedAndBareCells()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);

            Assert.IsTrue(world.TrySpawnItem(new Int2(1, 1), ShapeType.Square));
            Assert.IsFalse(world.TrySpawnItem(new Int2(1, 1), ShapeType.Triangle), "one item per cell");
            Assert.IsFalse(world.TrySpawnItem(new Int2(9, 9), ShapeType.Triangle), "not onto bare ground");
        }

        [Test]
        public void EveryBeltMutation_BumpsTheRenderRevision()
        {
            // The belt mesh only rebuilds when this counter moves, so a mutator that forgets to bump
            // it leaves the player staring at a belt that is not on screen. Trivially easy to miss
            // when adding a mutator, and invisible in every other test, so it gets its own.
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 3);

            int revision = world.Belts.Revision;
            Assert.Greater(revision, 0, "laying a belt must be visible to the renderer");

            Assert.IsTrue(world.Belts.TrySetDirection(new Int2(1, 1), Dir.North));
            Assert.Greater(world.Belts.Revision, revision, "re-pointing");
            revision = world.Belts.Revision;

            Assert.IsTrue(world.TryJamBelt(new Int2(2, 1)));
            Assert.Greater(world.Belts.Revision, revision, "jamming");
            revision = world.Belts.Revision;

            Assert.IsTrue(world.TryClearJam(new Int2(2, 1)));
            Assert.Greater(world.Belts.Revision, revision, "clearing a jam");
            revision = world.Belts.Revision;

            Assert.IsTrue(world.TryRemoveBelt(new Int2(3, 1)));
            Assert.Greater(world.Belts.Revision, revision, "removing");
        }

        [Test]
        public void RemovingAndReplacingABelt_ClearsTheStaleState()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);
            Assert.IsTrue(world.TryJamBelt(new Int2(1, 1)));
            Assert.IsTrue(world.TrySpawnItem(new Int2(2, 1), ShapeType.Circle));

            Assert.IsTrue(world.TryRemoveBelt(new Int2(1, 1)));
            Assert.IsTrue(world.TryRemoveBelt(new Int2(2, 1)));
            Assert.IsTrue(world.TryPlaceBelt(new Int2(2, 1), Dir.North));

            Assert.IsFalse(world.Belts.IsJammed(new Int2(2, 1)), "a rebuilt cell starts clean");
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(2, 1)));
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(2, 1)));
            Assert.AreEqual(0, Sim.ItemCount(world));
        }
    }
}
