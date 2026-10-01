using System;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the campaign: the shipped maps as data, and the progress that outlives any
    /// one run.
    ///
    /// <see cref="SimWorld"/> is one run on one map and stays that way - it is rebuilt from its map's
    /// own data and knows nothing about what came before it. So the two things worth testing here are
    /// that every shipped map is actually playable, and that the little state that does carry over
    /// (<see cref="CampaignState"/>) only ever moves forward and stops at the end.
    /// </summary>
    public class CampaignTests
    {
        [Test]
        public void EveryShippedMap_IsPlayableData()
        {
            // The maps are the one piece of content still written in code, so a typo in one - a patch
            // off the edge, a group spawning at a door that does not exist - is a data error nothing
            // else would catch. Cheaper to assert than to find by playing.
            Assert.GreaterOrEqual(Maps.All.Length, 2, "the campaign is more than one map");

            foreach (MapDefinition map in Maps.All)
            {
                Assert.IsFalse(string.IsNullOrEmpty(map.Name), "a map with no name has no HUD title");
                Assert.Greater(map.Width, CoreState.Size, map.Name + " is too narrow for a Core");
                Assert.Greater(map.Height, CoreState.Size, map.Name + " is too short for a Core");
                Assert.Greater(map.StartCircles, 0, map.Name + " cannot pay for anything");
                Assert.Greater(map.Waves.Length, 0, map.Name + " has no waves");
                Assert.Greater(map.SpawnPoints.Length, 0, map.Name + " has nowhere to walk in from");

                foreach (Int2 spawn in map.SpawnPoints)
                    Assert.IsTrue(spawn.X >= 0 && spawn.X < map.Width && spawn.Y >= 0 && spawn.Y < map.Height,
                        map.Name + " has an entry point outside the map: " + spawn);

                foreach (ShapePatch patch in map.Patches)
                {
                    Assert.IsTrue(patch.Shape.IsShape(), map.Name + " has a patch with no mineral");
                    Assert.IsTrue(patch.Origin.X >= 0 && patch.Origin.Y >= 0 &&
                                  patch.Origin.X + patch.Width <= map.Width &&
                                  patch.Origin.Y + patch.Height <= map.Height,
                        map.Name + " has a patch outside the map at " + patch.Origin);
                }

                foreach (WaveDefinition wave in map.Waves)
                {
                    Assert.Greater(wave.Total, 0, map.Name + " has an empty wave");
                    foreach (SpawnGroup group in wave.Groups)
                        Assert.IsTrue(group.SpawnPoint >= 0 && group.SpawnPoint < map.SpawnPoints.Length,
                            map.Name + " spawns a group at a door that does not exist");
                }
            }
        }

        /// <summary>
        /// Progression in the roster, the way the maps progress in minerals: the Quarry is Spikes only,
        /// which is what leaves it teaching one ammunition route, and the Foundry is where the second
        /// enemy and the second gun appear together. A Bulwark on the first map would put the weakness
        /// rule in front of a player who has not been shown the gun that answers it.
        /// </summary>
        [Test]
        public void QuarryUsesOneEnemyType_AndFoundryUsesBoth()
        {
            MapDefinition quarry = Maps.All[0];
            MapDefinition foundry = Maps.All[1];

            Assert.Greater(EnemyCount(quarry, EnemyKind.Spike), 0, "the Quarry sends Spikes");
            Assert.AreEqual(0, EnemyCount(quarry, EnemyKind.Bulwark),
                "and nothing else: the Quarry's one gun has one ammunition");

            Assert.Greater(EnemyCount(foundry, EnemyKind.Spike), 0,
                "the Foundry keeps the Spike, so the first route still has a job");
            Assert.Greater(EnemyCount(foundry, EnemyKind.Bulwark), 0,
                "and adds the Bulwark, so the second route is not optional");
        }

        /// <summary>How many of one enemy a map sends across all of its waves.</summary>
        private static int EnemyCount(MapDefinition map, EnemyKind kind)
        {
            int total = 0;
            foreach (WaveDefinition wave in map.Waves) total += wave.CountOf(kind);

            return total;
        }

        [Test]
        public void TheTutorialMap_MinesOneMineral_AndTheSecondMap_MinesBoth()
        {
            // Progression in the ground itself: map 1 teaches one chain with one mineral, and map 2
            // opens the second mineral so there is a second chain to run. Asserted as counts and
            // membership rather than as "the square patch is at (25,8)", so either map can be
            // relaid without this becoming a rubber stamp.
            Assert.AreEqual(1, Minerals(Maps.All[0]).Count, Maps.All[0].Name + " teaches one chain");
            Assert.IsTrue(Minerals(Maps.All[0]).Contains(ShapeType.Circle));

            System.Collections.Generic.List<ShapeType> second = Minerals(Maps.All[1]);
            Assert.IsTrue(second.Contains(ShapeType.Circle), "the second map still mines the currency");
            Assert.Greater(second.Count, 1, Maps.All[1].Name + " opens the second chain");
        }

        private static System.Collections.Generic.List<ShapeType> Minerals(MapDefinition map)
        {
            var mined = new System.Collections.Generic.List<ShapeType>();
            foreach (ShapePatch patch in map.Patches)
                if (!mined.Contains(patch.Shape)) mined.Add(patch.Shape);

            return mined;
        }

        [Test]
        public void EveryShippedMap_EndsWhenNothingDefendsIt()
        {
            // The cheapest end-to-end proof that a map's wave table, entry points and Core are wired
            // together: run it with nothing built. Every map has to be lost, and lost within the wave
            // table it declares rather than running forever - which is also what a wave that can never
            // be completed would look like.
            foreach (MapDefinition map in Maps.All)
            {
                var world = new SimWorld(map, new SimConfig());
                Sim.RunToEnd(world);

                Assert.AreEqual(GameStatus.Lost, world.Status, map.Name + " never ends undefended");
                Assert.Greater(world.Events.CountOf(SimEventKind.CoreDamaged), 0,
                    map.Name + " never got anything to its Core");
            }
        }

        [Test]
        public void TheSecondMap_IsHarderThanTheFirst()
        {
            // Progression has to be progression. Asserted as "more enemies and more waves", which is
            // the shape of every later map rather than one number, so retuning either map keeps this
            // meaningful.
            MapDefinition first = Maps.All[0];
            MapDefinition second = Maps.All[1];

            Assert.Greater(second.Waves.Length, first.Waves.Length, "the second map runs longer");
            Assert.Greater(TotalEnemies(second), TotalEnemies(first), "and sends more");
        }

        private static int TotalEnemies(MapDefinition map)
        {
            int total = 0;
            foreach (WaveDefinition wave in map.Waves) total += wave.Total;
            return total;
        }

        [Test]
        public void CampaignState_WalksForward_AndStopsAtTheEnd()
        {
            var campaign = new CampaignState(Maps.All.Length);

            Assert.AreEqual(0, campaign.MapIndex, "a campaign starts on the first map");
            Assert.AreEqual(0, campaign.ClearedCount);
            Assert.AreEqual(Maps.All.Length, campaign.MapCount);
            Assert.IsTrue(campaign.HasNext);

            // Clearing is idempotent, because the host polls the world's status every frame rather than
            // having to watch for the edge itself.
            campaign.MarkCleared();
            campaign.MarkCleared();
            Assert.AreEqual(1, campaign.ClearedCount);

            Assert.IsTrue(campaign.EnterNext());
            Assert.AreEqual(1, campaign.MapIndex);
            campaign.MarkCleared();
            Assert.AreEqual(2, campaign.ClearedCount);

            // The last map: there is nowhere after it, and asking does not move the campaign.
            Assert.IsFalse(campaign.HasNext);
            Assert.IsFalse(campaign.EnterNext());
            Assert.AreEqual(1, campaign.MapIndex);
            Assert.AreEqual(Maps.All.Length, campaign.ClearedCount);
        }

        [Test]
        public void CampaignState_ClampsWhereItStarts()
        {
            // The driver's start-map field is authored data, so a stale index - a map deleted from the
            // list - has to land on a real map instead of throwing in Awake.
            Assert.AreEqual(0, new CampaignState(2, -5).MapIndex);
            Assert.AreEqual(1, new CampaignState(2, 99).MapIndex);
            Assert.AreEqual(0, new CampaignState(0).MapIndex, "a campaign always has at least one map");
            Assert.AreEqual(1, new CampaignState(0).MapCount);
        }

        [Test]
        public void MapTwo_RunsOnItsOwnMineral()
        {
            // The map and the chain, wired together: a square patch on map 2 has a cutter's worth of
            // mineral in it, so the second chain is playable there and not only in a test world.
            MapDefinition map = Maps.All[1];
            var world = new SimWorld(map, new SimConfig());
            Int2 square = FirstPatchOf(map, ShapeType.Square);

            Assert.AreNotEqual(new Int2(-1, -1), square, map.Name + " mines the second mineral");
            Assert.IsTrue(world.CanPlace(BuildKind.Drill, square), "a drill belongs on it");
        }

        [Test]
        public void WaveSpawnIndices_WrapOntoTheMapsEntryPoints()
        {
            // A wave table names its door by position in the map's spawn list, and the index wraps so a
            // table stays loadable by a map with fewer doors instead of indexing off the end of the
            // entry points - and off the end of the marker list the view sizes from them.
            var map = new MapDefinition("two doors", 8, 8, 10, new ShapePatch[0],
                new[] { new Int2(0, 0), new Int2(7, 0) },
                new[] { new WaveDefinition(0f, new SpawnGroup(EnemyKind.Spike, 1, 3, 0f, 1f)) });

            Assert.AreEqual(0, map.ResolveSpawn(0), "the first door");
            Assert.AreEqual(1, map.ResolveSpawn(1), "the second");
            Assert.AreEqual(0, map.ResolveSpawn(2), "and a third wraps back to the first");
            Assert.AreEqual(1, map.ResolveSpawn(3), "which is what the table's own index resolves to");

            Assert.AreEqual(0, map.SpawnIndexOf(new Int2(0, 0)), "a cell reads back as its door");
            Assert.AreEqual(1, map.SpawnIndexOf(new Int2(7, 0)));
            Assert.AreEqual(-1, map.SpawnIndexOf(new Int2(4, 4)), "and bare ground is no door at all");

            var doorless = new MapDefinition("no doors", 8, 8, 10, new ShapePatch[0], new Int2[0],
                new WaveDefinition[0]);
            Assert.AreEqual(-1, doorless.ResolveSpawn(0), "a map with no doors resolves none");
            Assert.AreEqual(-1, map.ResolveSpawn(-1), "and a negative position is not a door either");
        }

        private static Int2 FirstPatchOf(MapDefinition map, ShapeType shape)
        {
            foreach (ShapePatch patch in map.Patches)
                if (patch.Shape == shape) return patch.Origin;

            return new Int2(-1, -1);
        }
    }
}
