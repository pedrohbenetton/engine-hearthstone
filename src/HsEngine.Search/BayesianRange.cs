using System.Runtime.InteropServices;
using System.Text.Json;
using HsEngine.Ingestion;
using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsEngine.Search;

public sealed record ArchetypeDefinition(string Name, string HeroClass, double Prior, DeckDefinition Deck);

public sealed record ArchetypePosterior(ArchetypeDefinition Archetype, double Probability);

public sealed record RangeSummary(
    string? LocalDeckName,
    string? TopArchetypeName,
    double TopArchetypeProbability,
    IReadOnlyList<ArchetypePosterior> Archetypes);

/// <summary>
/// Catalog of opponent meta archetypes (ADR-0006). Loads curated JSON lists when provided and always supplies
/// Aggro/Tempo and Control/Midrange priors for all 11 classes so no matchup ever lacks a valid 30-card range.
/// </summary>
public sealed class ArchetypeCatalog
{
    private static readonly string[] AllClasses =
    [
        "DEATHKNIGHT", "DEMONHUNTER", "DRUID", "HUNTER", "MAGE", "PALADIN",
        "PRIEST", "ROGUE", "SHAMAN", "WARLOCK", "WARRIOR",
    ];

    private static readonly Lazy<ArchetypeCatalog> BuiltIn = new(CreateDefault);

    private readonly List<ArchetypeDefinition> _archetypes;

    public ArchetypeCatalog(IEnumerable<ArchetypeDefinition> archetypes)
    {
        _archetypes = archetypes.ToList();
    }

    public static ArchetypeCatalog Default => BuiltIn.Value;

    public IReadOnlyList<ArchetypeDefinition> All => _archetypes;

    public IReadOnlyList<ArchetypeDefinition> ForClass(string heroClass)
    {
        string normalized = DeckStringCodec.NormalizeClass(heroClass);
        List<ArchetypeDefinition> matches = _archetypes
            .Where(a => string.Equals(DeckStringCodec.NormalizeClass(a.HeroClass), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count > 0 ? matches : _archetypes.Where(a => a.HeroClass == "MAGE").ToList();
    }

    public static ArchetypeCatalog LoadJson(string json, IEnumerable<ArchetypeDefinition>? fallback = null)
    {
        var dtoList = JsonSerializer.Deserialize<List<ArchetypeJsonDto>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var loaded = dtoList
            .Where(d => !string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.HeroClass) && d.Cards is { Count: > 0 })
            .Select(d =>
            {
                string cls = DeckStringCodec.NormalizeClass(d.HeroClass!);
                return new ArchetypeDefinition(d.Name!, cls, Math.Max(0.01, d.Prior ?? 1.0), new DeckDefinition(d.Name!, cls, d.Cards!));
            })
            .ToList();

        if (fallback is not null)
        {
            HashSet<string> coveredClasses = loaded.Select(a => a.HeroClass).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (ArchetypeDefinition fb in fallback)
            {
                // Keep a small smoothing weight on built-in archetypes even for covered classes so off-meta decks degrade gracefully.
                double prior = coveredClasses.Contains(fb.HeroClass) ? fb.Prior * 0.15 : fb.Prior;
                loaded.Add(fb with { Prior = prior });
            }
        }
        return new ArchetypeCatalog(loaded);
    }

    private static ArchetypeCatalog CreateDefault()
    {
        ProxyCards.EnsureRegistered();
        IReadOnlyList<CardData> snapshot = CardSnapshot.Default;

        // Usable collectible cards registered in SabberStone, preferring real implemented effects and keyworded minions/weapons.
        List<CardData> usable = snapshot
            .Where(c => c.Collectible
                        && c.Type is "MINION" or "SPELL" or "WEAPON"
                        && c.Cost is >= 0 and <= 10
                        && Cards.All.Any(k => k.Id == c.Id)
                        && (c.Type != "SPELL" || !ProxyCards.IsProxy(c.Id))) // never put no-op proxy spells into synthetic ranges
            .ToList();

        var archetypes = new List<ArchetypeDefinition>();
        foreach (string cls in AllClasses)
        {
            List<CardData> pool = usable
                .Where(c => string.Equals(c.CardClass, cls, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(c.CardClass, "NEUTRAL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            archetypes.Add(BuildSynthetic(cls, $"{PrettyClass(cls)} Aggro", prior: 0.5, pool, aggro: true));
            archetypes.Add(BuildSynthetic(cls, $"{PrettyClass(cls)} Control", prior: 0.5, pool, aggro: false));
        }

        string metaPath = Path.Combine(AppContext.BaseDirectory, "meta", "archetypes.json");
        return File.Exists(metaPath) ? LoadJson(File.ReadAllText(metaPath), archetypes) : new ArchetypeCatalog(archetypes);
    }

    private static ArchetypeDefinition BuildSynthetic(string cls, string name, double prior, List<CardData> pool, bool aggro)
    {
        double Score(CardData c)
        {
            int cost = c.Cost ?? 3;
            bool classCard = string.Equals(c.CardClass, cls, StringComparison.OrdinalIgnoreCase);
            bool implemented = !ProxyCards.IsProxy(c.Id);
            int keywords = c.Mechanics?.Count ?? 0;

            double curveFit = aggro
                ? (cost <= 3 ? 6.0 - cost * 0.8 : -cost * 1.2)
                : (cost >= 3 && cost <= 8 ? 4.5 : 1.0);

            bool defensive = c.Mechanics?.Any(m => m is "TAUNT" or "LIFESTEAL" or "RUSH") == true || c.Type == "SPELL";
            bool aggressive = c.Mechanics?.Any(m => m is "CHARGE" or "DIVINE_SHIELD" or "WINDFURY" or "RUSH") == true || c.Type == "WEAPON";

            return curveFit
                   + (classCard ? 4.0 : 0.0)
                   + (implemented ? 5.0 : 0.0)
                   + keywords * 0.6
                   + (aggro && aggressive ? 2.5 : 0.0)
                   + (!aggro && defensive ? 2.5 : 0.0);
        }

        var cards = new Dictionary<string, int>(StringComparer.Ordinal);
        int total = 0;
        foreach (CardData card in pool.OrderByDescending(Score).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            if (total >= 30)
                break;
            int copies = Math.Min(2, 30 - total);
            cards[card.Id] = copies;
            total += copies;
        }
        return new ArchetypeDefinition(name, cls, prior, new DeckDefinition(name, cls, cards));
    }

    private static string PrettyClass(string cls) => cls switch
    {
        "DEATHKNIGHT" => "Death Knight",
        "DEMONHUNTER" => "Demon Hunter",
        _ => char.ToUpperInvariant(cls[0]) + cls[1..].ToLowerInvariant(),
    };

    private sealed class ArchetypeJsonDto
    {
        public string? Name { get; set; }
        public string? HeroClass { get; set; }
        public double? Prior { get; set; }
        public Dictionary<string, int>? Cards { get; set; }
    }
}

/// <summary>
/// Bayesian opponent archetype &amp; hand-slot probability model (ADR-0006):
/// <list type="number">
/// <item>Posterior over archetypes <c>P(A | class, revealed main-deck cards)</c> with Laplace smoothing.</item>
/// <item>Per-slot card likelihood <c>w(card | KeptInMulligan, TurnEnteredHand, currentTurn)</c>.</item>
/// </list>
/// </summary>
public sealed class BayesianRangeEstimator(ArchetypeCatalog? catalog = null, double smoothing = 0.005)
{
    private readonly ArchetypeCatalog _catalog = catalog ?? ArchetypeCatalog.Default;

    public IReadOnlyList<ArchetypePosterior> InferArchetypes(string opponentClass, IReadOnlyList<string>? knownMainDeckCards)
    {
        IReadOnlyList<ArchetypeDefinition> candidates = _catalog.ForClass(opponentClass);
        if (candidates.Count == 0)
            return [];

        IReadOnlyList<string> seen = knownMainDeckCards ?? [];
        var logWeights = new double[candidates.Count];
        double maxLog = double.NegativeInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            ArchetypeDefinition arch = candidates[i];
            int deckSize = Math.Max(1, arch.Deck.TotalCards);
            var remaining = new Dictionary<string, int>(arch.Deck.Cards, StringComparer.Ordinal);

            double logP = Math.Log(Math.Max(1e-9, arch.Prior));
            foreach (string cardId in seen)
            {
                int copiesLeft = remaining.GetValueOrDefault(cardId);
                if (copiesLeft > 0)
                {
                    logP += Math.Log((double)copiesLeft / deckSize + smoothing);
                    remaining[cardId] = copiesLeft - 1;
                }
                else
                {
                    logP += Math.Log(smoothing);
                }
            }

            logWeights[i] = logP;
            if (logP > maxLog)
                maxLog = logP;
        }

        double sumExp = 0;
        var probs = new double[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            probs[i] = Math.Exp(logWeights[i] - maxLog);
            sumExp += probs[i];
        }

        return candidates
            .Select((arch, i) => new ArchetypePosterior(arch, sumExp > 0 ? probs[i] / sumExp : 1.0 / candidates.Count))
            .OrderByDescending(p => p.Probability)
            .ThenBy(p => p.Archetype.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Relative likelihood that hidden hand slot <paramref name="slot"/> holds <paramref name="card"/> at <paramref name="currentTurn"/>.
    /// </summary>
    public static double SlotCardWeight(Card card, ObservedHandCard slot, int currentTurn)
    {
        double weight = 1.0;
        int cost = card.Cost;

        if (slot.KeptInMulligan)
        {
            weight *= cost switch
            {
                <= 2 => 2.4,
                3 => 1.6,
                4 => 0.8,
                _ => 0.25,
            };
        }

        int opponentTurnsHeld = slot.TurnEnteredHand > 0 ? Math.Max(0, (currentTurn - slot.TurnEnteredHand + 1) / 2) : 0;
        int opponentMaxMana = Math.Clamp((currentTurn + 1) / 2, 1, 10);

        if (opponentTurnsHeld >= 2)
        {
            bool proactiveMinion = card.Type == CardType.MINION && !IsSituationalMinion(card);
            if (proactiveMinion && cost <= opponentMaxMana - 2)
            {
                int excessTurns = Math.Min(4, opponentTurnsHeld - 1);
                weight *= Math.Max(0.15, Math.Pow(0.68, excessTurns));
            }
            else if (card.Type == CardType.SPELL || cost > opponentMaxMana || IsSituationalMinion(card))
            {
                weight *= 1.35;
            }
        }

        return weight;
    }

    private static bool IsSituationalMinion(Card card) =>
        card.Text is { Length: > 0 } text
        && (text.Contains("Destroy", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Restore", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Silence", StringComparison.OrdinalIgnoreCase)
            || text.Contains("all minions", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Samples consistent worlds for SO-ISMCTS from the local player's remaining deck and the Bayesian posterior over
/// opponent archetypes and hand slots (ADR-0006). Thread-safe across parallel MCTS workers.
/// </summary>
public sealed class RangeDeterminizer : IDeterminizer
{
    private readonly ObservedState _observed;
    private readonly IReadOnlyList<ArchetypePosterior> _posteriors;
    private readonly Card[][] _opponentUnseenByArchetype;
    private readonly Card[] _opponentFallbackPool;
    private readonly Card[] _localRemainingPool;
    private readonly Card[] _localFallbackPool;

    public RangeDeterminizer(
        ObservedState observed,
        BayesianRangeEstimator? estimator = null,
        DeckDefinition? localDeck = null)
    {
        ProxyCards.EnsureRegistered();
        _observed = observed;

        BayesianRangeEstimator range = estimator ?? new BayesianRangeEstimator();
        _posteriors = range.InferArchetypes(observed.Opponent.Hero.Class, observed.Opponent.KnownDeckCards);

        _opponentFallbackPool = ClassFallbackPool(observed.Opponent.Hero.Class);
        _localFallbackPool = ClassFallbackPool(observed.Me.Hero.Class);

        _opponentUnseenByArchetype = _posteriors
            .Select(p => ResolveCards(p.Archetype.Deck.RemainingAfter(observed.Opponent.SpentDeckCards)))
            .ToArray();

        _localRemainingPool = localDeck is not null
            ? ResolveCards(localDeck.RemainingAfter(observed.Me.SpentDeckCards))
            : [];

        ArchetypePosterior? top = _posteriors.FirstOrDefault();
        Summary = new RangeSummary(localDeck?.Name, top?.Archetype.Name, top?.Probability ?? 0, _posteriors);
    }

    public RangeSummary Summary { get; }

    public Game Sample(Game observedGame, Random rng)
    {
        Game world = observedGame.CloneWithSeed(rng.NextInt64());
        PopulateLocalDeck(world.CurrentPlayer, rng);
        PopulateOpponentHandAndDeck(world.CurrentOpponent, rng);
        return world;
    }

    private void PopulateLocalDeck(Controller me, Random rng)
    {
        int totalSlots = me.DeckZone.Count;
        if (totalSlots == 0)
            return;

        // If concrete cards are already present (e.g. custom scenario), just shuffle them.
        if (me.DeckZone.All(c => c.Card.Id != ProxyCards.HiddenCardId))
        {
            ShuffleZone(me.DeckZone, rng);
            return;
        }

        List<IPlayable> concrete = me.DeckZone.Where(c => c.Card.Id != ProxyCards.HiddenCardId).ToList();
        int hiddenCount = totalSlots - concrete.Count;

        var sampled = new List<Card>(hiddenCount);
        sampled.AddRange(_localRemainingPool);
        rng.Shuffle(CollectionsMarshal.AsSpan(sampled));
        while (sampled.Count < hiddenCount && _localFallbackPool.Length > 0)
            sampled.Add(_localFallbackPool[rng.Next(_localFallbackPool.Length)]);

        foreach (IPlayable card in me.DeckZone.ToList())
            me.DeckZone.Remove(card);

        var combined = new List<Card>(totalSlots);
        combined.AddRange(concrete.Select(c => c.Card));
        combined.AddRange(sampled.Take(hiddenCount));
        rng.Shuffle(CollectionsMarshal.AsSpan(combined));

        foreach (Card card in combined)
            Entity.FromCard(me, card, zone: me.DeckZone);
    }

    private void PopulateOpponentHandAndDeck(Controller opponent, Random rng)
    {
        List<IPlayable> originalHand = opponent.HandZone.ToList();
        bool anyPlaceholder = originalHand.Any(c => c.Card.Id == ProxyCards.HiddenCardId)
                              || opponent.DeckZone.Any(c => c.Card.Id == ProxyCards.HiddenCardId);
        if (!anyPlaceholder)
        {
            new HiddenInformationDeterminizer().Sample(opponent.Game, rng);
            return;
        }

        int archIndex = SampleArchetypeIndex(rng);
        var pool = new List<Card>(archIndex >= 0 ? _opponentUnseenByArchetype[archIndex] : []);
        int neededMainDeck = originalHand.Count(c => c.Card.Id == ProxyCards.HiddenCardId) + opponent.DeckZone.Count;
        while (pool.Count < neededMainDeck && _opponentFallbackPool.Length > 0)
            pool.Add(_opponentFallbackPool[rng.Next(_opponentFallbackPool.Length)]);

        foreach (IPlayable c in originalHand)
            opponent.HandZone.Remove(c);

        IReadOnlyList<ObservedHandCard> observedHand = _observed.Opponent.Hand;
        for (int i = 0; i < originalHand.Count; i++)
        {
            if (originalHand[i].Card.Id != ProxyCards.HiddenCardId)
            {
                opponent.HandZone.Add(originalHand[i]);
                continue;
            }

            ObservedHandCard slot = i < observedHand.Count ? observedHand[i] : new ObservedHandCard(null, null);
            Card picked;
            if (slot.IsCreated && _opponentFallbackPool.Length > 0)
            {
                picked = _opponentFallbackPool[rng.Next(_opponentFallbackPool.Length)];
            }
            else if (pool.Count > 0)
            {
                int pickIdx = SampleWeightedIndex(pool, slot, _observed.Turn, rng);
                picked = pool[pickIdx];
                pool.RemoveAt(pickIdx);
            }
            else
            {
                picked = originalHand[i].Card;
            }

            Entity.FromCard(opponent, picked, zone: opponent.HandZone);
        }

        int deckSlots = opponent.DeckZone.Count;
        foreach (IPlayable c in opponent.DeckZone.ToList())
            opponent.DeckZone.Remove(c);

        rng.Shuffle(CollectionsMarshal.AsSpan(pool));
        for (int i = 0; i < deckSlots; i++)
        {
            Card card = i < pool.Count
                ? pool[i]
                : (_opponentFallbackPool.Length > 0 ? _opponentFallbackPool[rng.Next(_opponentFallbackPool.Length)] : Cards.FromId(ProxyCards.HiddenCardId));
            Entity.FromCard(opponent, card, zone: opponent.DeckZone);
        }
    }

    private int SampleArchetypeIndex(Random rng)
    {
        if (_posteriors.Count == 0)
            return -1;
        double u = rng.NextDouble();
        double cumulative = 0;
        for (int i = 0; i < _posteriors.Count; i++)
        {
            cumulative += _posteriors[i].Probability;
            if (u <= cumulative)
                return i;
        }
        return _posteriors.Count - 1;
    }

    private static int SampleWeightedIndex(List<Card> pool, ObservedHandCard slot, int turn, Random rng)
    {
        double total = 0;
        Span<double> weights = pool.Count <= 64 ? stackalloc double[pool.Count] : new double[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            double w = BayesianRangeEstimator.SlotCardWeight(pool[i], slot, turn);
            weights[i] = w;
            total += w;
        }

        if (total <= 0)
            return rng.Next(pool.Count);

        double target = rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += weights[i];
            if (target <= acc)
                return i;
        }
        return pool.Count - 1;
    }

    private static void ShuffleZone(SabberStoneCore.Model.Zones.DeckZone zone, Random rng)
    {
        List<IPlayable> cards = zone.ToList();
        foreach (IPlayable c in cards)
            zone.Remove(c);
        rng.Shuffle(CollectionsMarshal.AsSpan(cards));
        foreach (IPlayable c in cards)
            zone.Add(c);
    }

    private static Card[] ResolveCards(IEnumerable<string> cardIds) =>
        cardIds.Select(id => Cards.All.FirstOrDefault(c => c.Id == id)).OfType<Card>().ToArray();

    private static Card[] ClassFallbackPool(string heroClass)
    {
        string normalized = DeckStringCodec.NormalizeClass(heroClass);
        return CardSnapshot.Default
            .Where(c => c.Collectible
                        && c.Type is "MINION" or "SPELL" or "WEAPON"
                        && c.Cost is >= 1 and <= 8
                        && (string.Equals(c.CardClass, normalized, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(c.CardClass, "NEUTRAL", StringComparison.OrdinalIgnoreCase))
                        && (c.Type != "SPELL" || !ProxyCards.IsProxy(c.Id)))
            .Select(c => Cards.All.FirstOrDefault(k => k.Id == c.Id))
            .OfType<Card>()
            .Take(60)
            .ToArray();
    }
}
