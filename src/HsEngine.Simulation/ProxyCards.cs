using System.Reflection;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;

namespace HsEngine.Simulation;

public readonly record struct ProxyRegistration(int Proxied, int Aliased, int Skipped, int Compiled = 0);

/// <summary>
/// ADR-0008: makes current cards that SabberStone (2021) does not know usable by the simulator:
/// <list type="number">
/// <item>Reprints whose normalized effect text matches an implemented SabberStone card reuse its <see cref="Card.Power"/>
/// (even when Blizzard rebalanced mana cost or stats in Core).</item>
/// <item>New Standard spells, battlecries and deathrattles whose text matches exact templates are compiled by
/// <see cref="CardEffectCompiler"/>.</item>
/// <item>Remaining unknown minions, spells and weapons become stat/keyword proxies with <c>IsProxy == true</c>.</item>
/// </list>
/// </summary>
public static class ProxyCards
{
    private const string CorePrefix = "CORE_";

    /// <summary>
    /// Stand-in for a card whose identity the player does not know (opponent's hand, either deck) before range sampling.
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
            Dictionary<(string Name, CardType Type, string Text), Card> signatureMap = BuildSignatureIndex(known);
            int proxied = 0, aliased = 0, skipped = 0, compiledCount = 0;

            foreach (CardData data in snapshot)
            {
                if (known.ContainsKey(data.Id))
                    continue;
                if (!Types.TryGetValue(data.Type, out CardType type))
                {
                    skipped++;
                    continue;
                }

                if (Alias(data, type, known, signatureMap) is { } alias)
                {
                    known[data.Id] = alias;
                    aliased++;
                }
                else
                {
                    CompiledCardEffect? effect = data.Id == HiddenCardId ? null : CardEffectCompiler.TryCompile(data, type);
                    Card built = BuildCard(data, type, effect);
                    known[data.Id] = built;
                    if (effect?.Power is not null)
                    {
                        compiledCount++;
                    }
                    else
                    {
                        Proxies.Add(data.Id);
                        proxied++;
                    }
                }
            }
            return new ProxyRegistration(proxied, aliased, skipped, compiledCount);
        }
    }

    private static Dictionary<(string Name, CardType Type, string Text), Card> BuildSignatureIndex(Dictionary<string, Card> known)
    {
        var map = new Dictionary<(string, CardType, string), Card>();
        foreach (Card card in known.Values)
        {
            if (!card.Implemented || card.Power is null || string.IsNullOrWhiteSpace(card.Name))
                continue;
            string norm = CardEffectCompiler.NormalizeForAlias(card.Text);
            if (norm.Length > 0)
                map.TryAdd((card.Name, card.Type, norm), card);
        }
        return map;
    }

    private static Card? Alias(
        CardData data,
        CardType type,
        Dictionary<string, Card> known,
        Dictionary<(string Name, CardType Type, string Text), Card> signatureMap)
    {
        Card? original = null;
        string originalId = "";

        if (data.Id.StartsWith(CorePrefix, StringComparison.Ordinal))
        {
            string candidateId = data.Id[CorePrefix.Length..];
            if (known.TryGetValue(candidateId, out Card? coreOrig))
            {
                bool sameStats = coreOrig.Cost == (data.Cost ?? 0)
                                 && coreOrig.ATK == (data.Attack ?? 0)
                                 && coreOrig.Health == (data.Health ?? data.Durability ?? 0);
                bool sameEffectText = coreOrig.Implemented
                                      && CardEffectCompiler.NormalizeForAlias(coreOrig.Text) == CardEffectCompiler.NormalizeForAlias(data.Text);
                if (sameStats || sameEffectText)
                {
                    original = coreOrig;
                    originalId = candidateId;
                }
            }
        }

        if (original is null && data.Collectible)
        {
            string norm = CardEffectCompiler.NormalizeForAlias(data.Text);
            if (norm.Length > 0 && signatureMap.TryGetValue((data.Name, type, norm), out Card? sigOrig))
            {
                original = sigOrig;
                originalId = sigOrig.Id;
            }
        }

        if (original is null)
            return null;

        // SabberStone instantiates ChooseOne options by looking up Id + "a" and Id + "b" in Cards.
        if (original.ChooseOne)
        {
            if (originalId == "TRL_343"
                || !known.TryGetValue(originalId + "a", out Card? optA)
                || !known.TryGetValue(originalId + "b", out Card? optB))
                return null;

            RegisterSubOptionAlias(known, optA, data.Id + "a", data.DbfId * 10 + 1);
            RegisterSubOptionAlias(known, optB, data.Id + "b", data.DbfId * 10 + 2);
        }

        Card alias = original.Clone();
        Set(alias, nameof(Card.Id), data.Id);
        Set(alias, nameof(Card.AssetId), data.DbfId);

        int newCost = data.Cost ?? 0;
        alias.Tags[GameTag.COST] = newCost;
        Set(alias, nameof(Card.Cost), newCost);

        if (data.Attack is int newAtk)
        {
            alias.Tags[GameTag.ATK] = newAtk;
            Set(alias, nameof(Card.ATK), newAtk);
        }

        int? newHealthOrDur = type == CardType.WEAPON
            ? (data.Durability is > 0 ? data.Durability : data.Health)
            : data.Health;
        if (newHealthOrDur is int hp)
        {
            alias.Tags[type == CardType.WEAPON ? GameTag.DURABILITY : GameTag.HEALTH] = hp;
            Set(alias, nameof(Card.Health), hp);
        }

        return alias;
    }

    private static void RegisterSubOptionAlias(Dictionary<string, Card> known, Card originalSub, string aliasId, int fallbackAssetId)
    {
        if (known.ContainsKey(aliasId))
            return;
        Card subAlias = originalSub.Clone();
        Set(subAlias, nameof(Card.Id), aliasId);
        Set(subAlias, nameof(Card.AssetId), fallbackAssetId);
        known[aliasId] = subAlias;
    }

    private static Card BuildCard(CardData data, CardType type, CompiledCardEffect? compiled)
    {
        var tags = new List<Tag>
        {
            new(GameTag.CARDTYPE, (int)type),
            new(GameTag.COST, data.Cost ?? 0),
            new(GameTag.CARDNAME, data.Name),
            new(GameTag.CARDTEXT, data.Text ?? ""),
            new(GameTag.CLASS, Classes.GetValueOrDefault(data.CardClass ?? "", Classes["NEUTRAL"])),
        };
        if (data.Collectible)
            tags.Add(new Tag(GameTag.COLLECTIBLE, 1));
        if (data.Attack is int attack)
            tags.Add(new Tag(GameTag.ATK, attack));
        // Current HearthstoneJSON puts weapon durability in "health" and leaves "durability" at 0.
        if (type == CardType.WEAPON)
            tags.Add(new Tag(GameTag.DURABILITY, data.Durability is > 0 ? data.Durability.Value : data.Health ?? 0));
        else if (data.Health is int health)
            tags.Add(new Tag(GameTag.HEALTH, health));

        var addedGameTags = new HashSet<GameTag>();
        foreach (string mechanic in data.Mechanics ?? [])
            if (Keywords.TryGetValue(mechanic, out GameTag[]? keywordTags))
                foreach (GameTag t in keywordTags)
                    if (addedGameTags.Add(t))
                        tags.Add(new Tag(t, 1));

        if (compiled is { SpellPower: > 0 })
            tags.Add(new Tag(GameTag.SPELLPOWER, compiled.SpellPower));
        if (compiled is { HasDeathrattle: true } && addedGameTags.Add(GameTag.DEATHRATTLE))
            tags.Add(new Tag(GameTag.DEATHRATTLE, 1));
        if (compiled?.Power?.PowerTask is not null && type != CardType.SPELL && addedGameTags.Add(GameTag.BATTLECRY))
            tags.Add(new Tag(GameTag.BATTLECRY, 1));

        Dictionary<PlayReq, int> playReqs = compiled?.PlayRequirements ?? [];
        var card = (Card)CardConstructor.Invoke([data.Id, data.DbfId, tags.ToArray(), playReqs, null, Array.Empty<Tag>()]);
        if (compiled?.Power is not null)
        {
            card.Power = compiled.Power;
            card.Implemented = true;
        }
        return card;
    }

    private static Dictionary<string, Card> KnownCards()
    {
        _ = Cards.Count; // runs SabberStone's static card loader
        object container = typeof(Cards).GetField("Data", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return (Dictionary<string, Card>)container.GetType()
            .GetProperty("Cards", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(container)!;
    }

    // Writes the auto-property's backing field: Id, Cost, ATK, Health have private setters, AssetId has none.
    private static void Set(Card card, string property, object value) =>
        typeof(Card).GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(card, value);
}
