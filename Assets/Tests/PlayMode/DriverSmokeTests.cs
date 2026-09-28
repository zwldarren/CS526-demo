using System.Collections;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Facet.PlayTests
{
    /// <summary>
    /// The one thing the EditMode tests cannot reach: the view layer actually running.
    ///
    /// These tests build a scene from nothing - camera, driver, views - and then check the three
    /// failures that no compile can catch and no headless simulation test touches: that the driver
    /// assembles its views at all, that it advances the world at its fixed rate while the frame rate
    /// does whatever it likes, and that a view whose simulation state changed has a mesh with geometry
    /// in it. The last one is the honest check for "is it drawn": the mesh is real even when there is
    /// no display to put it on, so an empty or never-rebuilt mesh fails here.
    /// </summary>
    public class DriverSmokeTests
    {
        private GameObject _cameraObject;
        private GameObject _driverObject;

        [SetUp]
        public void CreateScene()
        {
            _cameraObject = new GameObject("Main Camera");
            _cameraObject.tag = "MainCamera";
            Camera camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.transform.position = new Vector3(40f, 24f, -10f);

            _driverObject = new GameObject("FACET");
            _driverObject.AddComponent<SimulationDriver>();
        }

        [TearDown]
        public void DestroyScene()
        {
            // Unity's destroyed-object equality makes this safe even if a test replaced the scene.
            if (_driverObject != null) Object.Destroy(_driverObject);
            if (_cameraObject != null) Object.Destroy(_cameraObject);
        }

        /// <summary>
        /// The scene the WebGL build actually boots - not one this test built. If its camera, driver
        /// or Palette wiring were missing, the build would show an empty blue screen and no test
        /// would have noticed.
        /// </summary>
        [UnityTest]
        public IEnumerator TheShippedMainScene_RunsAsBuilt()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;

            SimulationDriver driver = Object.FindFirstObjectByType<SimulationDriver>();
            Assert.IsNotNull(driver, "Assets/Scenes/Main.unity has to contain the driver");
            Assert.IsNotNull(driver.Colors, "and has to have the Palette asset wired, not the code default");
            Assert.IsNotNull(driver.World, "and has to be running");
            Assert.AreEqual(Maps.All[0].Width, driver.World.TileGrid.Width, "on the map the design calls for");
            Assert.AreEqual(Maps.All[0].Height, driver.World.TileGrid.Height);
            Assert.IsNotNull(Camera.main, "with a MainCamera for the views to sit under");

            int ticks = driver.World.TickCount;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(driver.World.TickCount, ticks, "and the clock runs in the shipped scene too");
        }

        [UnityTest]
        public IEnumerator Driver_BuildsEveryView_AndAdvancesTheWorld()
        {
            yield return null;   // let Awake and the first Update run

            var driver = _driverObject.GetComponent<SimulationDriver>();
            Assert.IsNotNull(driver.World, "the driver owns a simulation");
            Assert.IsNotNull(driver.World.TileGrid);

            foreach (string view in new[] { "Grid", "Patches", "Belts", "Core", "Machines", "Items", "Enemies", "Shots", "Cursor", "Hud" })
                Assert.IsNotNull(_driverObject.transform.Find(view), "the driver did not build the " + view + " view");

            int ticks = driver.World.TickCount;
            yield return new WaitForSeconds(0.4f);
            Assert.Greater(driver.World.TickCount, ticks, "the fixed 30 Hz clock keeps running across frames");
        }

        [UnityTest]
        public IEnumerator PlacingABuilding_PutsGeometryOnScreen()
        {
            yield return null;

            var driver = _driverObject.GetComponent<SimulationDriver>();
            SimWorld world = driver.World;
            var belt = new Int2(20, 10);
            var turret = new Int2(21, 10);

            Assert.IsTrue(world.TryPlaceBelt(belt, Dir.East));
            Assert.IsTrue(world.TryPlace(BuildKind.Cannon, turret, Dir.East));
            Assert.IsTrue(world.TrySpawnItem(belt, ShapeType.HalfCircle));
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(24f, 10.5f));

            // One frame for the views to notice the revision bump, one for the new enemy to be drawn.
            yield return null;
            yield return null;

            Assert.Greater(Vertices("Belts"), 0, "a placed belt has a mesh");
            Assert.Greater(Vertices("Machines"), 0, "a placed turret has a mesh");
            Assert.Greater(Vertices("Patches"), 0, "the map's shape patches are drawn");
            Assert.Greater(Vertices("Items"), 0, "the shape riding the belt is drawn");
            Assert.Greater(Vertices("Enemies"), 0, "the enemy walking at the Core is drawn");
        }

        [UnityTest]
        public IEnumerator Restart_ClearsWhatTheViewsDraw()
        {
            yield return null;

            var driver = _driverObject.GetComponent<SimulationDriver>();
            SimWorld world = driver.World;
            Assert.IsTrue(world.TryPlaceBelt(new Int2(20, 10), Dir.East));

            yield return null;
            Assert.Greater(Vertices("Belts"), 0);

            world.Tick(new InputCommand(restartPressed: true));

            yield return null;
            Assert.AreEqual(0, Vertices("Belts"), "a restarted run has no belts to draw");
            Assert.AreEqual(GameStatus.Playing, world.Status);
        }

        private int Vertices(string viewName)
        {
            Transform view = _driverObject.transform.Find(viewName);
            Assert.IsNotNull(view, "no " + viewName + " view");

            var filter = view.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, viewName + " has no mesh filter");
            Assert.IsNotNull(filter.sharedMesh, viewName + " has no mesh");

            return filter.sharedMesh.vertexCount;
        }
    }
}
