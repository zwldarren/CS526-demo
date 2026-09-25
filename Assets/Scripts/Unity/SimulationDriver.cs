using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The bridge between Unity and the simulation: owns one <see cref="SimWorld"/> and
    /// advances it at a fixed <see cref="SimConfig.TickRate"/> Hz regardless of frame rate.
    /// This is the ONLY component you need to add to a scene - it builds the views itself.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationDriver : MonoBehaviour
    {
        [Header("Map (tiles)")]
        [SerializeField] private int mapWidth = 64;
        [SerializeField] private int mapHeight = 36;

        [Header("Simulation")]
        [Tooltip("Belt travel in tiles per second. A cell holds one item, so this is also items per second.")]
        [SerializeField] private float beltSpeedTilesPerSecond = 1f;
        [SerializeField] private float coreMaxHp = 100f;

        [Header("Look")]
        [SerializeField] private Palette palette = new Palette();

        [Header("Views")]
        [Tooltip("Build the grid / belt / item / core / cursor views at runtime. Turn off to place views by hand.")]
        [SerializeField] private bool autoCreateViews = true;

        /// <summary>Hard cap on catch-up ticks per frame, so a long hitch cannot spiral.</summary>
        private const int MaxTicksPerFrame = 5;

        public SimWorld World { get; private set; }
        public Palette Colors => palette;

        /// <summary>How far the renderer is between the previous and the current tick, 0..1.</summary>
        public float Alpha { get; private set; }

        /// <summary>Size of one screen pixel in world units, at the current resolution and zoom.</summary>
        public float WorldPerPixel { get; private set; }

        /// <summary>Tile under the cursor as of the last poll, or (-1,-1) when there is no cursor.</summary>
        public Int2 CursorCell { get; private set; }

        public GridRenderer GridView { get; private set; }
        public BeltRenderer BeltView { get; private set; }
        public ItemRenderer ItemView { get; private set; }
        public CoreView CoreView { get; private set; }

        private BuildInput _input;
        private Camera _camera;
        private float _accumulator;

        private void Awake()
        {
            // Tile coordinates ARE world coordinates here: the views build their meshes in tile
            // space, and the cursor turns a screen point straight into a tile. So this object has to
            // sit at the origin, or everything drawn drifts away from everything clicked. Pinned in
            // code rather than left to scene authoring, because the symptom is a silent offset that
            // looks like a mesh bug.
            transform.position = Vector3.zero;

            var grid = new TileGrid(mapWidth, mapHeight);
            World = new SimWorld(grid, new SimConfig
            {
                BeltSpeed = beltSpeedTilesPerSecond,
                CoreMaxHp = coreMaxHp,
            });

            _input = new BuildInput();
            CursorCell = new Int2(-1, -1);

            _camera = Camera.main;
            if (_camera == null)
            {
                Debug.LogError("FACET: no Main Camera in the scene - the views cannot be positioned.", this);
                enabled = false;
                return;
            }

            if (!_camera.orthographic)
            {
                Debug.LogWarning("FACET: Main Camera must be orthographic for a 2D grid game - switching it.", this);
                _camera.orthographic = true;
            }

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = palette.Background;

            UpdateWorldPerPixel();

            if (autoCreateViews) BuildViews();
        }

        private void Update()
        {
            UpdateWorldPerPixel();

            if (_input.ConsumePauseToggle())
            {
                World.Paused = !World.Paused;
                Debug.Log("FACET: " + (World.Paused ? "paused" : "resumed") + " at tick " + World.TickCount, this);
            }

            InputCommand cmd = _input.Poll(_camera, World);
            CursorCell = cmd.CursorCell;

            // TEMPORARY, until drills land: T drops a triangle on the cell under the cursor, which is
            // the only way to watch transport by eye right now. See BuildInput.ConsumeDebugSpawn.
            if (_input.ConsumeDebugSpawn() && !World.TrySpawnItem(CursorCell, ShapeType.Triangle))
            {
                Debug.Log("FACET: T needs an empty belt cell - " + CursorCell + " is not one.", this);
            }

            _accumulator += Time.deltaTime;

            int ticks = 0;
            while (_accumulator >= SimConfig.TickDt && ticks < MaxTicksPerFrame)
            {
                _accumulator -= SimConfig.TickDt;
                World.Tick(cmd);
                ticks++;
            }

            // Drop the leftover backlog rather than trying to catch up forever.
            if (ticks >= MaxTicksPerFrame) _accumulator = 0f;

            Alpha = World.Paused ? 1f : Mathf.Clamp01(_accumulator / SimConfig.TickDt);
        }

        private void UpdateWorldPerPixel()
        {
            WorldPerPixel = Screen.height > 0
                ? (2f * _camera.orthographicSize) / Screen.height
                : 0.02f;
        }

        private void BuildViews()
        {
            GridView = AddView<GridRenderer>("Grid");
            GridView.Initialize(World.TileGrid, palette, WorldPerPixel);

            BeltView = AddView<BeltRenderer>("Belts");
            BeltView.Initialize(World, palette);

            CoreView = AddView<CoreView>("Core");
            CoreView.Initialize(World, this, palette);

            ItemView = AddView<ItemRenderer>("Items");
            ItemView.Initialize(World, this, palette);

            CursorView cursor = AddView<CursorView>("Cursor");
            cursor.Initialize(World, this, palette);

            CameraRig rig = _camera.GetComponent<CameraRig>();
            if (rig == null) rig = _camera.gameObject.AddComponent<CameraRig>();
            rig.Initialize(World.TileGrid);
        }

        private T AddView<T>(string viewName) where T : Component
        {
            var go = new GameObject(viewName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(new Vector3(mapWidth * 0.5f, mapHeight * 0.5f, 0f),
                new Vector3(mapWidth, mapHeight, 0f));
        }
    }
}
