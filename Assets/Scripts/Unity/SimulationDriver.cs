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

        [Header("Rig")]
        [Tooltip("Tiles per second. Design doc: 4.")]
        [SerializeField] private float rigSpeedTilesPerSecond = 4f;
        [SerializeField] private float rigRadiusTiles = 0.42f;

        [Header("Look")]
        [SerializeField] private Palette palette = new Palette();

        [Header("Views")]
        [Tooltip("Build the grid / rig / camera rig at runtime. Turn off to place views by hand.")]
        [SerializeField] private bool autoCreateViews = true;

        /// <summary>Hard cap on catch-up ticks per frame, so a long hitch cannot spiral.</summary>
        private const int MaxTicksPerFrame = 5;

        public SimWorld World { get; private set; }
        public Palette Colors => palette;

        /// <summary>How far the renderer is between the previous and the current tick, 0..1.</summary>
        public float Alpha { get; private set; }

        /// <summary>Size of one screen pixel in world units, at the current resolution.</summary>
        public float WorldPerPixel { get; private set; }

        public RigView Rig { get; private set; }
        public GridRenderer GridView { get; private set; }

        private RigInput _input;
        private Camera _camera;
        private float _accumulator;

        private void Awake()
        {
            var grid = new TileGrid(mapWidth, mapHeight);
            World = new SimWorld(grid, new SimConfig
            {
                RigSpeed = rigSpeedTilesPerSecond,
                RigRadius = rigRadiusTiles,
            });

            _input = new RigInput();

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
            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(transform, false);
            GridView = gridGo.AddComponent<GridRenderer>();
            GridView.Initialize(World.TileGrid, palette, WorldPerPixel);

            var rigGo = new GameObject("Rig");
            rigGo.transform.SetParent(transform, false);
            Rig = rigGo.AddComponent<RigView>();
            Rig.Initialize(World, this, palette, WorldPerPixel);

            CameraFollow follow = _camera.GetComponent<CameraFollow>();
            if (follow == null) follow = _camera.gameObject.AddComponent<CameraFollow>();
            follow.Initialize(Rig.transform, World.TileGrid);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(new Vector3(mapWidth * 0.5f, mapHeight * 0.5f, 0f),
                new Vector3(mapWidth, mapHeight, 0f));
        }
    }
}
