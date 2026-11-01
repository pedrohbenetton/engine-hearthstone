using System.Reflection;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;

namespace HsEngine.Simulation;

public readonly record struct ProxyRegistration(int Proxied, int Aliased, int Skipped);

/// <summary>
/// ADR-0008: makes current cards that SabberStone (2021) does not know usable by the simulator.
/// A Core reprint whose cost and stats match the implemented original is an alias that shares its behaviour;
/// any other unknown minion, spell or weapon becomes a proxy with cost, stats and keywords but no text effect.
/// SabberStone's card database is internal, so registration goes through reflection (as GameExtensions does
/// for Game.Random). Registration mutates a process-wide table: do it before games run in parallel.
/// </summary>
public static class ProxyCards
{
    private const string CorePrefix = "CORE_";

    /// <summary>
    /// Stand-in for a card whose identity the player does not know (opponent's hand, either deck). It costs more
    /// than any mana pool, so it is never played: hidden cards stay inert until the range model (ADR-0006) samples them.
    /// </summary>
    public const string HiddenCardId = "HSE_HIDDEN";

    private static readonly CardData HiddenCard = new(HiddenCardId, 0, "Hidden card", "SPELL", "HSE", Cost: 99);

    private static readonly Lock Gate = new();
    private static readonly HashSet<string> Proxies = [];
    private static bool _defaultRegistered;

    private static readonly ConstructorInfo CardConstructor = typeof(Card).GetConstructor(
        BindingFlags.NonPublic | BindingFlags.Instance, null,
        [typeof(string), typeof(int), typeof(Tag[]), typeof(Dictionary<PlayReq, int>), typeof(string[]), typeof(Tag[])], null)
        ?? throw new MissingMethodException("SabberStone Card constructor not found.");

    private static readonly Dictionary<string, CardType> Types = new()
    {
        ["MINION"] = CardType.MINION,
        ["SPELL"] = CardType.SPELL,
        ["WEAPON"] = CardType.WEAPON,
    };

    // HearthstoneJSON class names to the game's CLASS tag values; SabberStone's enum predates Demon Hunter.
    private static readonly Dictionary<string, int> Classes = new()
    {
        ["DEATHKNIGHT"] = 1, ["DRUID"] = 2, ["HUNTER"] = 3, ["MAGE"] = 4, ["PALADIN"] = 5, ["PRIEST"] = 6,
        ["ROGUE"] = 7, ["SHAMAN"] = 8, ["WARLOCK"] = 9, ["WARRIOR"] = 10, ["NEUTRAL"] = 12, ["DEMONHUNTER"] = 14,
    };

    private static readonly Dictionary<string, GameTag[]> Keywords = new()
    {
        ["TAUNT"] = [GameTag.TAUNT],
        ["DIVINE_SHIELD"] = [GameTag.DIVINE_SHIELD],
        ["CHARGE"] = [GameTag.CHARGE],
        ["RUSH"] = [GameTag.RUSH],
        ["STEALTH"] = [GameTag.STEALTH],
        ["WINDFURY"] = [GameTag.WINDFURY],
        ["POISONOUS"] = [GameTag.POISONOUS],
        ["LIFESTEAL"] = [GameTag.LIFESTEAL],
        ["REBORN"] = [GameTag.REBORN],
        ["FREEZE"] = [GameTag.FREEZE],
        ["ELUSIVE"] = [GameTag.CANT_BE_TARGETED_BY_SPELLS, GameTag.CANT_BE_TARGETED_BY_HERO_POWERS],
    };

    public static bool IsProxy(string cardId)
    {
        lock (Gate)
            return Proxies.Contains(cardId);
    }

    /// <summary>Registers the default snapshot once per process.</summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_defaultRegistered)
                return;
            Register(CardSnapshot.Default.Append(HiddenCard));
            _defaultRegistered = true;
        }
    }

    public static ProxyRegistration Register(IEnumerable<CardData> snapshot)
    {
        lock (Gate)
        {
            Dictionary<string, Card> known = KnownCards();
            int proxied = 0, aliased = 0, skipped = 0;
            foreach (CardData data in snapshot)
            {
                if (known.ContainsKey(data.Id))
                    continue;
                if (!Types.TryGetValue(data.Type, out CardType type))
                {
                    skipped++;
                    continue;
                }

                if (Alias(data, known) is { } alias)
                {
                    known[data.Id] = alias;
                    aliased++;
                }
                else
                {
                    known[data.Id] = Proxy(data, type);
                    Proxies.Add(data.Id);
                    proxied++;
                }
            }
            return new ProxyRegistration(proxied, aliased, skipped);
        }
    }

    private static Card? Alias(CardData data, Dictionary<string, Card> known)
    {
        if (!data.Id.StartsWith(CorePrefix, StringComparison.Ordinal)
            || !known.TryGetValue(data.Id[CorePrefix.Length..], out Card? original)
            || original.Cost != (data.Cost ?? 0)
            || original.ATK != (data.Attack ?? 0)
            || original.Health != (data.Health ?? data.Durability ?? 0))
            return null;

        Card alias = original.Clone();
        Set(alias, nameof(Card.Id), data.Id);
        Set(alias, nameof(Card.AssetId), data.DbfId);
        return alias;
    }

    private static Card Proxy(CardData data, CardType type)
    {
        var tags = new List<Tag>
        {
            new(GameTag.CARDTYPE, (int)type),
            new(GameTag.COST, data.Cost ?? 0),
            new(GameTag.CARDNAME, data.Name),
            new(GameTag.CARDTEXT, data.Text ?? ""),
            new(GameTag.CLASS, Classes.GetValueOrDefault(data.CardClass ?? "", Classes["NEUTRAL"])),
        };
        if (data.Attack is int attack)
            tags.Add(new Tag(GameTag.ATK, attack));
        // Current HearthstoneJSON puts weapon durability in "health" and leaves "durability" at 0.
        if (type == CardType.WEAPON)
            tags.Add(new Tag(GameTag.DURABILITY, data.Durability is > 0 ? data.Durability.Value : data.Health ?? 0));
        else if (data.Health is int health)
            tags.Add(new Tag(GameTag.HEALTH, health));
        foreach (string mechanic in data.Mechanics ?? [])
            if (Keywords.TryGetValue(mechanic, out GameTag[]? keywordTags))
                tags.AddRange(keywordTags.Select(t => new Tag(t, 1)));

        return (Card)CardConstructor.Invoke([data.Id, data.DbfId, tags.ToArray(), new Dictionary<PlayReq, int>(), null, Array.Empty<Tag>()]);
    }

    private static Dictionary<string, Card> KnownCards()
    {
        _ = Cards.Count; // runs SabberStone's static card loader
        object container = typeof(Cards).GetField("Data", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return (Dictionary<string, Card>)container.GetType()
            .GetProperty("Cards", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(container)!;
    }

    // Writes the auto-property's backing field: Id has a private setter, AssetId has none.
    private static void Set(Card card, string property, object value) =>
        typeof(Card).GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(card, value);
}
