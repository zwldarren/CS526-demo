namespace Facet.Core
{
    /// <summary>
    /// The stockpile: circles the player has banked by belting them into the Core, minus what
    /// building has spent. There is no other currency and no other income - a circle on a belt is
    /// inventory in transit, not yet spendable, which is what makes the resource lines a logistics
    /// problem instead of a timer.
    /// </summary>
    public sealed class EconomyState
    {
        /// <summary>Spendable circles right now. Read by the HUD and by placement.</summary>
        public int Circles { get; private set; }

        /// <summary>Total banked at the Core this run, including what has been spent. Telemetry for
        /// the end-of-run report: how much economy the player built.</summary>
        public int TotalBanked { get; private set; }

        public EconomyState(int startCircles)
        {
            Circles = startCircles;
        }

        public bool CanAfford(BuildKind kind) => Circles >= Balance.Cost(kind);

        /// <summary>Charge for one placement. False when the stockpile cannot cover it.</summary>
        public bool TrySpend(BuildKind kind)
        {
            int cost = Balance.Cost(kind);
            if (Circles < cost) return false;

            Circles -= cost;
            return true;
        }

        /// <summary>Full refund on removal: the cost gates how fast you can expand, not whether
        /// you dare to experiment. A cleared jam removes nothing and refunds nothing.</summary>
        public void Refund(BuildKind kind) => Circles += Balance.Cost(kind);

        /// <summary>Bank circles delivered to the Core.</summary>
        public void Bank(int circles)
        {
            Circles += circles;
            TotalBanked += circles;
        }

        public void Reset(int startCircles)
        {
            Circles = startCircles;
            TotalBanked = 0;
        }
    }
}
