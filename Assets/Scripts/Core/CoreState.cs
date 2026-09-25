namespace Facet.Core
{
    /// <summary>
    /// The object the player defends. Sim-owned state; the view only reads it.
    /// Enemies path toward <see cref="Cell"/> and drain <see cref="Hp"/>;
    /// reaching zero is the loss condition.
    /// </summary>
    public struct CoreState
    {
        public Int2 Cell;

        public float Hp;

        public float MaxHp;

        public bool Alive => Hp > 0f;

        /// <summary>0..1, for drawing a damage ring without the view knowing MaxHp.</summary>
        public float HealthFraction => MaxHp <= 0f ? 0f : Hp / MaxHp;
    }
}
