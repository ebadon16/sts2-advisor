using System.Collections.Generic;

namespace STS2Advisor.GameBridge
{
    /// <summary>
    /// Lightweight card info extracted from game objects.
    /// </summary>
    public class CardInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Cost { get; set; }
        public string Type { get; set; }      // "Attack", "Skill", "Power", "Status", "Curse"
        public string Rarity { get; set; }     // "Common", "Uncommon", "Rare"
        public bool Upgraded { get; set; }
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Screen position of the card (for overlay placement).
        /// Set by GameStateReader when reading from the active screen.
        /// </summary>
        public float ScreenX { get; set; }
        public float ScreenY { get; set; }
    }
}
