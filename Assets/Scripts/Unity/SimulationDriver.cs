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
        [Header("Simulation")]
        [Tooltip("Belt travel in tiles per second. A cell holds one item, so this is also items per second.")]
        [SerializeField] private float beltSpeedTilesPerSecond = 1f;
        [SerializeField] private float coreMaxHp = 100f;

        [Header("Look")]
        [Tooltip("Colours and screen-space thicknesses. Create one with Assets > Create > FACET > Palette.")]
        [SerializeField] private Palette palette;

        [Header("Views")]
        [Tooltip("Build the terrain / machine / belt / item / enemy / shot / core / cursor / HUD views at runtime.")]
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

        /// <summary>The frame every view is drawing right now. The driver is the only writer.</summary>
        public FrameClock Frames { get; private set; }

        private BuildInput _input;
        private Camera _camera;
        private float _accumulator;

        /// <summary>The HUD, for its panels' claim on the mouse. Null until the views are built (and
        /// for a driver that never built any).</summary>
        private HudView _hud;

        private void Awake()
        {
            // Tile coordinates ARE world coordinates here: the views build their meshes in tile
            // space, and the cursor turns a screen point straight into a tile. So this object has to
            // sit at the origin, or everything drawn drifts away from everything clicked. Pinned in
            // code rather than left to scene authoring, because the symptom is a silent offset that
            // looks like a mesh bug.
            transform.position = Vector3.zero;

            if (palette == null)
            {
                // Keeps a scene that has not been wired to a Palette asset runnable, and says so
                // rather than drawing in whatever colour a null reference happens to produce.
                Debug.LogWarning("FACET: no Palette assigned - using code defaults. " +
                    "Assign one (Assets > Create > FACET > Palette) to tune colours and line widths.", this);
                palette = ScriptableObject.CreateInstance<Palette>();
                palette.hideFlags = HideFlags.DontSave;
            }

            // The first map in the progression. Multi-map flow (advance on clear) is a driver-level
            // concern; with one map shipped, the run simply restarts here.
            MapDefinition map = Maps.All[0];
            World = new SimWorld(map, new SimConfig
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
            Frames = new FrameClock { Frame = new ViewFrame(1f, WorldPerPixel, new Int2(-1, -1)) };

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

            // Sample the devices once, then hand the same latched command to every tick this frame:
            // a click that arrived between two ticks must be applied once, not once per catch-up tick.
            // The HUD has first claim on the mouse: while the pointer is over a panel the buttons do
            // not reach the world, so pressing the start-wave button cannot also drop a belt under it.
            _input.Sample(_camera, World, _hud != null && _hud.PointerOverHud);
            InputCommand cmd = _input.Snapshot();
            CursorCell = cmd.CursorCell;

            _accumulator += Time.deltaTime;

            int ticks = 0;
            while (_accumulator >= SimConfig.TickDt && ticks < MaxTicksPerFrame)
            {
                _accumulator -= SimConfig.TickDt;

                World.Tick(cmd);
                _input.ConsumeOneShots();
                cmd = _input.Snapshot();
                ticks++;
            }

            // Drop the leftover backlog rather than trying to catch up forever.
            if (ticks >= MaxTicksPerFrame) _accumulator = 0f;

            Alpha = World.Paused || World.Status != GameStatus.Playing
                ? 1f
                : Mathf.Clamp01(_accumulator / SimConfig.TickDt);

            // Publish the frame views draw in. Written once here, after the tick loop and after
            // WorldPerPixel was refreshed, so every view in this frame reads the same value.
            Frames.Frame = new ViewFrame(Alpha, WorldPerPixel, CursorCell);
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
            GridView.Initialize(World, Frames, palette);

            ShapePatchRenderer patches = AddView<ShapePatchRenderer>("Patches");
            patches.Initialize(World, Frames, palette);

            BeltRenderer belts = AddView<BeltRenderer>("Belts");
            belts.Initialize(World, Frames, palette);

            CoreView core = AddView<CoreView>("Core");
            core.Initialize(World, Frames, palette);

            MachineRenderer machines = AddView<MachineRenderer>("Machines");
            machines.Initialize(World, Frames, palette);

            ItemRenderer items = AddView<ItemRenderer>("Items");
            items.Initialize(World, Frames, palette);

            EnemyRenderer enemies = AddView<EnemyRenderer>("Enemies");
            enemies.Initialize(World, Frames, palette);

            ProjectileRenderer shots = AddView<ProjectileRenderer>("Shots");
            shots.Initialize(World, Frames, palette);

            CursorView cursor = AddView<CursorView>("Cursor");
            cursor.Initialize(World, Frames, palette);

            _hud = AddView<HudView>("Hud");
            _hud.Initialize(World, palette);

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
            float width = Maps.All[0].Width;
            float height = Maps.All[0].Height;
            Gizmos.DrawWireCube(new Vector3(width * 0.5f, height * 0.5f, 0f),
                new Vector3(width, height, 0f));
        }
    }
}
