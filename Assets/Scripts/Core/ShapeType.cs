namespace Facet.Core
{
    /// <summary>
    /// The item shapes. Map 1 mines one mineral (the circle) and splits it into ammunition (the
    /// half-circle); the enum grows when later maps add minerals. Colours and polygon sides are
    /// the VIEW's business, not the simulation's - Core only knows the identity.
    /// </summary>
    public enum ShapeType
    {
        None = 0,

        /// <summary>The raw mineral: mined from patches, and the currency every building costs.
        /// It becomes spendable only when a belt delivers it to the Core.</summary>
        Circle = 1,

        /// <summary>Half a circle, pushed out of the decomposer in pairs. The cannon's only diet.</summary>
        HalfCircle = 2,
    }

    public static class ShapeTypeExtensions
    {
        /// <summary>True for the real shapes, false for <see cref="ShapeType.None"/>.</summary>
        public static bool IsShape(this ShapeType shape)
            => shape == ShapeType.Circle || shape == ShapeType.HalfCircle;
    }
}
