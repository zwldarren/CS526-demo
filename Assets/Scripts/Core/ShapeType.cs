namespace Facet.Core
{
    /// <summary>
    /// The three mined resource shapes. Colours and polygon sides are the VIEW's business,
    /// not the simulation's - Core only knows the identity.
    /// </summary>
    public enum ShapeType
    {
        None = 0,
        Triangle = 1,
        Square = 2,
        Circle = 3,
    }
}
