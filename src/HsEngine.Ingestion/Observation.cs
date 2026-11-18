using System.Globalization;

namespace HsEngine.Ingestion;

public sealed record ObservedHero(
    string CardId, string Class, int Health, int Damage, int Armor, int Attack, bool Exhausted, int AttacksThisTurn, bool HeroPowerUsed);

public sealed record ObservedMinion(
    string CardId, int Attack, int Health, int Damage, IReadOnlyList<string> Keywords, bool Exhausted, int AttacksThisTurn, int TurnsInPlay);

public sealed record ObservedWeapon(string CardId, int Attack, int Durability, int Damage);

/// <summary>
/// A card in hand; <see cref="CardId"/> and <see cref="Cost"/> are null when the card is hidden.
/// Hand-slot history (<see cref="TurnEnteredHand"/>, <see cref="KeptInMulligan"/>, <see cref="IsCreated"/>) is visible
/// on screen for both players and drives Bayesian hand-range narrowing (ADR-0006).
/// </summary>
public sealed record ObservedHandCard(
    string? CardId,
    int? Cost,
    int TurnEnteredHand = 0,
    bool KeptInMulligan = false,
    bool IsCreated = false);

public sealed record ObservedSide(
    int PlayerId,
    ObservedHero Hero,
    int Resources,
    int ResourcesUsed,
    int TemporaryResources,
    int OverloadLocked,
    ObservedWeapon? Weapon,
    IReadOnlyList<ObservedMinion> Board,
    IReadOnlyList<ObservedHandCard> Hand,
    int DeckCount,
    int SecretCount,
    IReadOnlyList<string>? KnownDeckCards = null,
    IReadOnlyList<string>? SpentDeckCards = null,
    IReadOnlyList<string?>? Secrets = null,
    int Fatigue = 0);

/// <summary>The game as the local player sees it: everything the recommender is allowed to use.</summary>
public sealed record ObservedState(int Turn, int CurrentPlayerId, ObservedSide Me, ObservedSide Opponent);

/// <summary>
/// Hidden-information sanitizer (ADR-0005 item 3). Projects the tracked state onto what the local client shows:
/// public zones in full, hand cards only when their identity is known, observable hand-slot history (turn drawn,
/// mulligan keep, created-by status), and main-deck cards revealed in visible zones. Raw tags of hidden entities
/// and any entity only seen in <c>DECK</c> or unrevealed <c>SECRET</c> never cross this boundary.
/// </summary>
public static class Observation
{
    private static readonly string[] MinionKeywords =
    [
        "TAUNT", "DIVINE_SHIELD", "STEALTH", "FROZEN", "WINDFURY", "POISONOUS", "LIFESTEAL", "REBORN", "CHARGE", "RUSH",
        "CANT_ATTACK", "CANT_BE_TARGETED_BY_SPELLS", "CANT_BE_TARGETED_BY_HERO_POWERS", "IMMUNE",
    ];

    public static ObservedState From(GameStateMachine state)
    {
        if (state.LocalPlayerId is not int local || state.OpponentPlayerId is not int opponent)
            throw new InvalidOperationException("The local player is not known yet (no PlayerName lines).");

        int current = state.Entities.Values
            .Where(e => e["CARDTYPE"] == "PLAYER" && e["CURRENT_PLAYER"] == "1")
            .Select(e => e.Int("PLAYER_ID"))
            .FirstOrDefault();

        return new ObservedState(
            state.Turn,
            current,
            Side(state, local, isLocal: true),
            Side(state, opponent, isLocal: false));
    }

    private static ObservedSide Side(GameStateMachine state, int playerId, bool isLocal)
    {
        PlayerState player = state.Player(playerId);
        string controller = playerId.ToString(CultureInfo.InvariantCulture);
        List<TrackedEntity> owned = state.Entities.Values.Where(e => e["CONTROLLER"] == controller).ToList();
        List<TrackedEntity> inPlay = owned.Where(e => e["ZONE"] == "PLAY").ToList();
        TrackedEntity? heroPower = inPlay.FirstOrDefault(e => e["CARDTYPE"] == "HERO_POWER");
        TrackedEntity? weapon = inPlay.FirstOrDefault(e => e["CARDTYPE"] == "WEAPON");
        TrackedEntity hero = player.Hero;

        List<string> knownDeckCards = owned
            .Where(e => IsMainDeckCard(e) && IsRevealedToLocal(e, isLocal))
            .OrderBy(e => e.Id)
            .Select(e => e.CardId)
            .ToList();

        List<string> spentDeckCards = owned
            .Where(e => IsMainDeckCard(e) && e["ZONE"] != "DECK" && IsRevealedToLocal(e, isLocal))
            .OrderBy(e => e.Id)
            .Select(e => e.CardId)
            .ToList();

        List<TrackedEntity> activeSecrets = owned.Where(e => e["ZONE"] == "SECRET").OrderBy(e => e.Id).ToList();
        List<string?> secretIds = activeSecrets
            .Select(e => isLocal && e.CardId.Length > 0 ? e.CardId : null)
            .ToList();
        int fatigue = Math.Max(player.Entity.Int("FATIGUE"), hero.Int("FATIGUE"));

        return new ObservedSide(
            playerId,
            new ObservedHero(hero.CardId, hero["CLASS"] ?? "", hero.Int("HEALTH"), hero.Int("DAMAGE"), hero.Int("ARMOR"),
                hero.Int("ATK"), Flag(hero, "EXHAUSTED"), hero.Int("NUM_ATTACKS_THIS_TURN"),
                heroPower is not null && Flag(heroPower, "EXHAUSTED")),
            player.Resources,
            player.ResourcesUsed,
            player.Entity.Int("TEMP_RESOURCES"),
            player.Entity.Int("OVERLOAD_LOCKED"),
            weapon is null ? null : new ObservedWeapon(weapon.CardId, weapon.Int("ATK"),
                weapon.Int("DURABILITY") > 0 ? weapon.Int("DURABILITY") : weapon.Int("HEALTH"), weapon.Int("DAMAGE")),
            player.Board.Select(Minion).ToList(),
            player.Hand.Select(HandCard).ToList(),
            player.DeckCount,
            activeSecrets.Count,
            knownDeckCards,
            spentDeckCards,
            secretIds,
            fatigue);
    }

    private static ObservedHandCard HandCard(TrackedEntity c) => new(
        c.CardId.Length == 0 ? null : c.CardId,
        c.CardId.Length == 0 ? null : c.Int("COST"),
        c.TurnEnteredHand,
        c.KeptInMulligan,
        !IsMainDeckCard(c));

    private static bool IsMainDeckCard(TrackedEntity e) =>
        e.WasInDeck && e.Int("CREATOR") == 0 && e.Int("DISPLAYED_CREATOR") == 0;

    private static bool IsRevealedToLocal(TrackedEntity e, bool isLocal) =>
        e.CardId.Length > 0
        && (e.VisitedZones.Contains("PLAY")
            || e.VisitedZones.Contains("GRAVEYARD")
            || e.VisitedZones.Contains("REMOVEDFROMGAME")
            || (isLocal && e.VisitedZones.Contains("HAND"))
            || (!isLocal && e["ZONE"] == "HAND"));

    private static ObservedMinion Minion(TrackedEntity m) => new(
        m.CardId, m.Int("ATK"), m.Int("HEALTH"), m.Int("DAMAGE"),
        MinionKeywords.Where(k => Flag(m, k)).ToList(),
        Flag(m, "EXHAUSTED"), m.Int("NUM_ATTACKS_THIS_TURN"), m.Int("NUM_TURNS_IN_PLAY"));

    private static bool Flag(TrackedEntity e, string tag) => e.Int(tag) != 0;
}
