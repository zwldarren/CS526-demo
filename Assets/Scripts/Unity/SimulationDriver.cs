using System.Collections.Generic;
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

        [Header("Content")]
        [Tooltip("Costs, turret/enemy stats and recipes. Create one with Assets > Create > FACET > Content " +
            "Database. Empty uses the shipped table (ContentDatabase.Default) unchanged.")]
        [SerializeField] private ContentDatabaseAsset content = null;

        [Header("Progress")]
        [Tooltip("Which map of Maps.All to start on. The rest of the campaign follows it: clear a map, " +
            "press N, and the next one is built without reloading the scene.")]
        [SerializeField] private int startMapIndex = 0;

        [Header("Views")]
        [Tooltip("Build the terrain / machine / belt / item / enemy / shot / core / cursor / HUD views at runtime.")]
        [SerializeField] private bool autoCreateViews = true;

        /// <summary>Hard cap on catch-up ticks per frame, so a long hitch cannot spiral.</summary>
        private const int MaxTicksPerFrame = 5;

        /// <summary>The simulation being played - one run, replaced when the campaign moves on.</summary>
        public SimWorld World { get; private set; }

        public Palette Colors => palette;

        /// <summary>Where the player is in the campaign. It outlives every world it builds, which is
        /// the whole reason it is not a field on <see cref="SimWorld"/>.</summary>
        public CampaignState Campaign { get; private set; }

        /// <summary>The content table this run was built with, whether it came from the asset or the
        /// shipped defaults. Read by the HUD so what it prints is the same table the tick runs on.</summary>
        public ContentDatabase Content { get; private set; }

        /// <summary>How far the renderer is between the previous and the current tick, 0..1.</summary>
        public float Alpha { get; private set; }

        /// <summary>Size of one screen pixel in world units, at the current resolution and zoom.</summary>
        public float WorldPerPixel { get; private set; }

        /// <summary>Tile under the cursor as of the last poll, or (-1,-1) when there is no cursor.</summary>
        public Int2 CursorCell { get; private set; }

        /// <summary>The frame every view is drawing right now. The driver is the only writer.</summary>
        public FrameClock Frames { get; private set; }

        private BuildInput _input;
        private Camera _camera;
        private float _accumulator;

        /// <summary>Every mesh view, so a map change can point them all at the new world. Gathered as
        /// they are built rather than looked up by name, so a view added later cannot be forgotten.</summary>
        private readonly List<MeshView> _views = new List<MeshView>();

        /// <summary>The camera rig, whose pan bounds are the map's size and so have to follow it.</summary>
        private CameraRig _rig;

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

            // The map the campaign opens on, and the content every map of it will be played with: the
            // content table is converted once here, so a map change costs one SimWorld.
            Campaign = new CampaignState(Maps.All.Length, startMapIndex);
            Content = content != null ? content.ToCore() : ContentDatabase.Default;
            World = NewWorld(Maps.All[Campaign.MapIndex]);

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
            // Unity can call Update on an instance whose Awake never completed: a script reload during
            // play restores the scene - the one that happens when the Editor's "Reload Domain" is off
            // and the sources change under a running game - without re-awaking what it restores. There
            // is nothing to advance then: the driver stands down rather than throwing once per frame,
            // and its Awake's own bail (no camera) has already done the same.
            if (_input == null)
            {
                enabled = false;
                return;
            }

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

            // N means "go on" in both states a run can be in: start the next wave while a map is still
            // being fought, and carry on to the next map once it is won. The world drops a start-wave
            // request that arrives after the run ended, so this is the only reader that acts on it
            // there - and the one-shot is consumed here either way, so the new map does not open by
            // starting its own first wave, which is the player's call on every map.
            if (World.Status == GameStatus.Won && _input.StartWaveRequested)
            {
                _input.ConsumeOneShots();
                if (Campaign.EnterNext()) LoadMap();
            }

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

            // Recorded as it happens rather than on a keypress, so the campaign's own count is right
            // whatever got the player here. Idempotent, so polling every frame is the honest way to
            // read an edge the world does not report.
            if (World.Status == GameStatus.Won) Campaign.MarkCleared();

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

        /// <summary>
        /// Swap in the map the campaign is now on, without reloading the scene.
        ///
        /// The world is <em>replaced</em> rather than reset: a world is one run, its fields are sized by
        /// its map, and its map is fixed for its lifetime - so a new map is a new world, built from its
        /// own data with the same content table. The views take a rebind rather than a rebuild, because
        /// their meshes, material and pooled sprites are the same objects either way.
        /// </summary>
        public void LoadMap()
        {
            World = NewWorld(Maps.All[Campaign.MapIndex]);

            // The frame the views are about to draw has to describe the new world: no leftover backlog
            // of ticks from the old one, and no cursor parked on a tile that may not exist.
            _accumulator = 0f;
            Alpha = 1f;
            CursorCell = new Int2(-1, -1);
            Frames.Frame = new ViewFrame(1f, WorldPerPixel, CursorCell);

            for (int i = 0; i < _views.Count; i++) _views[i].Rebind(World);
            if (_rig != null) _rig.Initialize(World.TileGrid);
            if (_hud != null) _hud.Initialize(World, palette, Campaign, _input.RequestSelect, _input.RequestRotate);

            Debug.Log("FACET: loaded map " + (Campaign.MapIndex + 1) + " of " + Campaign.MapCount +
                " - " + World.Map.Name, this);
        }

        private SimWorld NewWorld(MapDefinition map)
        {
            return new SimWorld(map, new SimConfig
            {
                BeltSpeed = beltSpeedTilesPerSecond,
                CoreMaxHp = coreMaxHp,
            }, Content);
        }

        private void BuildViews()
        {
            AddMeshView<GridRenderer>("Grid");
            AddMeshView<ShapePatchRenderer>("Patches");
            AddMeshView<BeltRenderer>("Belts");
            AddMeshView<CoreView>("Core");
            AddMeshView<MachineRenderer>("Machines");
            AddMeshView<ItemRenderer>("Items");
            AddMeshView<EnemyRenderer>("Enemies");
            AddMeshView<ProjectileRenderer>("Shots");
            AddMeshView<CursorView>("Cursor");

            _hud = AddView<HudView>("Hud");
            _hud.Initialize(World, palette, Campaign, _input.RequestSelect, _input.RequestRotate);

            _rig = _camera.GetComponent<CameraRig>();
            if (_rig == null) _rig = _camera.gameObject.AddComponent<CameraRig>();
            _rig.Initialize(World.TileGrid);
        }

        private T AddView<T>(string viewName) where T : Component
        {
            var go = new GameObject(viewName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        /// <summary>Build a view and point it at the world, remembering it for the next map change -
        /// which is what <see cref="LoadMap"/> needs, and why views are not looked up by name.</summary>
        private T AddMeshView<T>(string viewName) where T : MeshView
        {
            T view = AddView<T>(viewName);
            view.Initialize(World, Frames, palette);
            _views.Add(view);
            return view;
        }

        private void OnDrawGizmosSelected()
        {
            int index = Application.isPlaying && Campaign != null ? Campaign.MapIndex : startMapIndex;
            MapDefinition map = Maps.All[Mathf.Clamp(index, 0, Maps.All.Length - 1)];

            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(new Vector3(map.Width * 0.5f, map.Height * 0.5f, 0f),
                new Vector3(map.Width, map.Height, 0f));
        }
    }
}
