using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the event stream - the seam every reaction to the game hangs off (a flash, a
    /// sound, a HUD ticker). Two properties matter. First, the stream says what actually happened:
    /// which shape jammed what, which enemy was hit, which wave ended. Second, it is <b>derived</b> -
    /// nothing in the simulation reads it back - so it has to be a pure function of the run, which the
    /// determinism test below pins by comparing two identical runs tick for tick.
    ///
    /// The buffer is bounded, so a test that reads a specific tick's events clears first (that is the
    /// documented protocol: the simulation appends, the reader clears).
    /// </summary>
    public class SimEventTests
    {
        [Test]
        public void APlacement_ReportsItsCost_AndARemoval_TheRefund()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            world.Events.Clear();

            Sim.Place(world, BuildKind.Splitter, new Int2(2, 2), Dir.East);

            Assert.AreEqual(1, world.Events.Count);
            Assert.AreEqual(SimEventKind.Built, world.Events[0].Kind);
            Assert.AreEqual(new Int2(2, 2), world.Events[0].Cell);
            Assert.AreEqual(world.Economy.CostOf(BuildKind.Splitter), world.Events[0].Amount, Sim.Tol,
                "the splitter's cost, reported");

            world.Events.Clear();
            Assert.IsTrue(world.TryRemoveBuilding(new Int2(2, 2)));

            Assert.AreEqual(1, world.Events.Count);
            Assert.AreEqual(SimEventKind.Removed, world.Events[0].Kind);
            Assert.AreEqual(world.Economy.CostOf(BuildKind.Splitter), world.Events[0].Amount, Sim.Tol,
                "the full refund, reported");
        }

        [Test]
        public void AWrongShapeDelivery_ReportsTheJam_AndTheShapeThatCausedIt()
        {
            // The shipped cannon eats half-circles, so a circle arriving at it is the jam the whole
            // twist is about - and the event has to name the shape, or a log line cannot say what
            // went wrong.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Cannon, new Int2(4, 2), Dir.East);
            Assert.IsTrue(world.TrySpawnItem(new Int2(3, 2), ShapeType.Circle));

            world.Events.Clear();
            Sim.Tick(world, 60);

            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.Jammed));
            Assert.IsTrue(world.Events.TryLast(SimEventKind.Jammed, out SimEvent jammed));
            Assert.AreEqual(new Int2(3, 2), jammed.Cell);
            Assert.AreEqual(ShapeType.Circle, jammed.Shape, "the shape that arrived wrong");

            world.Events.Clear();
            Assert.IsTrue(world.TryClearJam(new Int2(3, 2)));
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.JamCleared));
            Assert.AreEqual(new Int2(3, 2), world.Events[0].Cell);
        }

        [Test]
        public void ACircleReachingTheCore_ReportsTheBank_AtTheBeltThatDeliveredIt()
        {
            // The Core's footprint in the 20x12 test map is (8,4)-(11,7), so (7,4) pointing east is a
            // delivery: the tick banks it and the stream says so the same tick.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(7, 4), Dir.East, 1);
            Assert.IsTrue(world.TrySpawnItem(new Int2(7, 4), ShapeType.Circle));
            int before = world.Economy.Circles;   // read after the belt was paid for

            world.Events.Clear();
            Sim.Tick(world, 60);

            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.Banked));
            Assert.IsTrue(world.Events.TryLast(SimEventKind.Banked, out SimEvent banked));
            Assert.AreEqual(new Int2(7, 4), banked.Cell);
            Assert.AreEqual(ShapeType.Circle, banked.Shape);
            Assert.AreEqual(before + 1, world.Economy.Circles, "and the stockpile moved by one");
        }

        [Test]
        public void AFiredShot_ReportsItsAmmo_AndItsHit_ReportsTheEnemy()
        {
            // One cannon, two half-circles, one 6-HP spike: the first shot damages, the second kills -
            // so the stream has to carry a fired shot, a damage and a kill, in that order.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Cannon, new Int2(4, 2), Dir.East);

            int id = world.Enemies.Spawn(EnemyKind.Spike, new Vec2(6.5f, 2.5f));
            Assert.Greater(id, 0);

            world.Events.Clear();

            Assert.IsTrue(world.TrySpawnItem(new Int2(3, 2), ShapeType.HalfCircle));
            Sim.Tick(world, 60);

            Assert.IsTrue(world.TrySpawnItem(new Int2(3, 2), ShapeType.HalfCircle));
            Sim.Tick(world, 60);

            Assert.AreEqual(2, world.Events.CountOf(SimEventKind.ShotFired), "two shots, two reports");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.ShotFired, out SimEvent shot));
            Assert.AreEqual(new Int2(4, 2), shot.Cell, "reported at the turret that fired");
            Assert.AreEqual(ShapeType.HalfCircle, shot.Shape, "and with the ammunition it spent");
            Assert.AreEqual(3f, shot.Amount, Sim.Tol);

            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.EnemyDamaged), "the first shot hurt it");
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.EnemyKilled), "the second killed it");

            Assert.IsTrue(world.Events.TryLast(SimEventKind.EnemyKilled, out SimEvent killed));
            Assert.AreEqual(id, killed.EnemyId, "named by id, so a view can flash that one enemy");
            Assert.AreEqual(EnemyKind.Spike, killed.Enemy);
            Assert.AreEqual(0, world.Enemies.AliveCount);
        }

        [Test]
        public void AnEnemyAtTheCore_ReportsTheDamageItDid()
        {
            // The Core's attack ring is two tiles from its centre, so an enemy dropped half a tile
            // outside it arrives and hits within a few ticks.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10f, 8.5f));

            world.Events.Clear();
            Sim.Tick(world, 120);

            Assert.Greater(world.Events.CountOf(SimEventKind.CoreDamaged), 0, "it hit the Core");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.CoreDamaged, out SimEvent hit));
            Assert.AreEqual(6f, hit.Amount, Sim.Tol, "the spike's core damage");
        }

        [Test]
        public void AWave_ReportsItsStart_ItsClear_AndTheRunEnding_ExactlyOnceEach()
        {
            // The reference winning run from ScenarioTests, with the stream drained every tick so the
            // buffer's bound can never confuse the count. Every wave that started must have been
            // cleared, and a won run reports its win once - not once per tick after it.
            SimWorld world = Sim.NewWorld();
            Sim.BuildMapOneEconomyLine(world);
            Sim.TickUntilFunded(world, Balance.Cost(BuildKind.Drill) + Balance.Cost(BuildKind.Decomposer)
                + Balance.Cost(BuildKind.Pipe) + Balance.Cost(BuildKind.Cannon) + 25);
            Sim.BuildMapOneDefence(world, out _, out _);
            Sim.Tick(world, (int)(35 * SimConfig.TickRate));

            int started = 0, cleared = 0, won = 0, lost = 0;
            for (int i = 0; i < 180 * SimConfig.TickRate && world.Status == GameStatus.Playing; i++)
            {
                world.Events.Clear();
                if (world.Waves.CurrentWave == 0) world.StartNextWave();
                world.Tick(InputCommand.None);

                started += world.Events.CountOf(SimEventKind.WaveStarted);
                cleared += world.Events.CountOf(SimEventKind.WaveCleared);
                won += world.Events.CountOf(SimEventKind.RunWon);
                lost += world.Events.CountOf(SimEventKind.RunLost);
            }

            Assert.AreEqual(GameStatus.Won, world.Status);
            Assert.Greater(started, 0, "the run fought at least one wave");
            Assert.AreEqual(started, cleared, "every wave that started was cleared");
            Assert.AreEqual(1, won, "the win is reported exactly once");
            Assert.AreEqual(0, lost);
        }

        [Test]
        public void TheRingKeepsTheMostRecentEvents_AndCountsWhatItOverwrote()
        {
            // A reader that never clears still has to see the present: the buffer is a ring, so a
            // stream that outgrows it loses its oldest events and says how many.
            var events = new SimEventBuffer(capacity: 4);
            for (int i = 0; i < 10; i++)
            {
                events.Tick = i;
                events.Banked(new Int2(i, 0));
            }

            Assert.AreEqual(4, events.Count, "the ring holds its capacity");
            Assert.AreEqual(6, events.Dropped, "and reports the six that fell off the oldest end");
            Assert.AreEqual(new Int2(6, 0), events[0].Cell, "index 0 is the oldest still held");
            Assert.AreEqual(new Int2(9, 0), events[3].Cell, "and the last is the newest");
            Assert.IsTrue(events.TryLast(SimEventKind.Banked, out SimEvent last));
            Assert.AreEqual(new Int2(9, 0), last.Cell, "so asking for the last one answers with the present");

            events.Clear();
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(0, events.Dropped);
        }

        [Test]
        public void TwoIdenticalRuns_ReportIdenticalStreams()
        {
            // Derived or not, the stream has to be a function of the run: a replay that drives sound
            // and flashes must react identically, or the feedback lies about what happened.
            SimWorld a = Sim.NewWorld(24, 12);
            SimWorld b = Sim.NewWorld(24, 12);

            for (int i = 0; i < 300; i++)
            {
                a.Events.Clear();
                b.Events.Clear();

                if (i % 30 == 0)
                {
                    a.TrySpawnItem(new Int2(2, 2), ShapeType.Circle);
                    b.TrySpawnItem(new Int2(2, 2), ShapeType.Circle);
                }

                var cmd = new InputCommand(true, false, new Int2(2 + (i % 9), 1 + (i % 5)),
                    selected: BuildKind.Belt);
                a.Tick(cmd);
                b.Tick(cmd);

                Assert.AreEqual(a.Events.Count, b.Events.Count, "stream lengths diverged on tick " + i);
                for (int e = 0; e < a.Events.Count; e++)
                {
                    Assert.AreEqual(a.Events[e].Kind, b.Events[e].Kind, "tick " + i + " event " + e);
                    Assert.AreEqual(a.Events[e].Cell, b.Events[e].Cell, "tick " + i + " event " + e);
                    Assert.AreEqual(a.Events[e].Tick, b.Events[e].Tick, "tick " + i + " event " + e);
                    Assert.AreEqual(a.Events[e].Amount, b.Events[e].Amount, "tick " + i + " event " + e);
                }
            }

            Assert.AreEqual(a.Economy.Circles, b.Economy.Circles);
        }
    }
}
