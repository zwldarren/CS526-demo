namespace Facet.Core
{
    /// <summary>Tunables for the simulation. Mutating these mid-run is allowed and instant.</summary>
    public sealed class SimConfig
    {
        /// <summary>Logic ticks per second. The renderer interpolates between them.</summary>
        public const float TickRate = 30f;

        public const float TickDt = 1f / TickRate;

        /// <summary>Rig movement speed in tiles per second (design doc: 4).</summary>
        public float RigSpeed = 4f;

        /// <summary>Rig collision radius in tiles. Also its minimum distance from the map edge.</summary>
        public float RigRadius = 0.42f;
    }
}
