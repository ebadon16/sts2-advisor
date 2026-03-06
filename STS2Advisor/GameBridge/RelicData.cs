namespace STS2Advisor.GameBridge
{
    /// <summary>
    /// Lightweight relic info extracted from game objects.
    /// </summary>
    public class RelicInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Rarity { get; set; }  // "Common", "Uncommon", "Rare", "Boss", "Shop", "Event"

        /// <summary>
        /// Screen position for overlay placement.
        /// </summary>
        public float ScreenX { get; set; }
        public float ScreenY { get; set; }
    }
}
