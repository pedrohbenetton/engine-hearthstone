using SabberStoneCore.Model;

namespace HsEngine.Simulation;

/// <summary>Fixed Slice 0 deck lists, built only from Basic cards SabberStone implements (ADR-0003).</summary>
public static class Decks
{
    /// <summary>Midrange Mage: burn, card draw and vanilla/simple-battlecry minions. Played as a mirror.</summary>
    public static IReadOnlyList<string> BasicMage { get; } =
    [
        "Arcane Missiles", "Arcane Missiles", "Frostbolt", "Frostbolt", "Arcane Intellect", "Arcane Intellect",
        "Fireball", "Fireball", "Water Elemental", "Water Elemental", "Murloc Raider", "Murloc Raider",
        "Acidic Swamp Ooze", "Acidic Swamp Ooze", "Bloodfen Raptor", "Bloodfen Raptor", "River Crocolisk", "River Crocolisk",
        "Novice Engineer", "Novice Engineer", "Razorfen Hunter", "Razorfen Hunter", "Shattered Sun Cleric", "Shattered Sun Cleric",
        "Wolfrider", "Wolfrider", "Chillwind Yeti", "Chillwind Yeti", "Sen'jin Shieldmasta", "Boulderfist Ogre",
    ];

    public static List<Card> ToCards(IEnumerable<string> names) =>
        names.Select(n => Cards.FromName(n) ?? throw new ArgumentException($"No collectible card named \"{n}\".")).ToList();
}
