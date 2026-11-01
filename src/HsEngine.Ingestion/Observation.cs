using System.Globalization;

namespace HsEngine.Ingestion;

public sealed record ObservedHero(
    string CardId, string Class, int Health, int Damage, int Armor, int Attack, bool Exhausted, int AttacksThisTurn, bool HeroPowerUsed);

public sealed record ObservedMinion(
    string CardId, int Attack, int Health, int Damage, IReadOnlyList<string> Keywords, bool Exhausted, int AttacksThisTurn, int TurnsInPlay);

public sealed record ObservedWeapon(string CardId, int Attack, int Durability, int Damage);

/// <summary>A card in hand; <see cref="CardId"/> and <see cref="Cost"/> are null when the card is hidden.</summary>
public sealed record ObservedHandCard(string? CardId, int? Cost);

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
    int SecretCount);

/// <summary>The game as the local player sees it: everything the recommender is allowed to use.</summary>
public sealed record ObservedState(int Turn, int CurrentPlayerId, ObservedSide Me, ObservedSide Opponent);

/// <summary>
/// Hidden-information sanitizer (ADR-0005 item 3). Projects the tracked state onto what the local client shows:
/// public zones in full, hand cards only when their identity is known, and nothing but counts for decks and
/// secrets. Raw tags of hidden entities never cross this boundary.
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

        return new ObservedState(state.Turn, current, Side(state, local), Side(state, opponent));
    }

    private static ObservedSide Side(GameStateMachine state, int playerId)
    {
        PlayerState player = state.Player(playerId);
        string controller = playerId.ToString(CultureInfo.InvariantCulture);
        List<TrackedEntity> inPlay = state.Entities.Values.Where(e => e["CONTROLLER"] == controller && e["ZONE"] == "PLAY").ToList();
        TrackedEntity? heroPower = inPlay.FirstOrDefault(e => e["CARDTYPE"] == "HERO_POWER");
        TrackedEntity? weapon = inPlay.FirstOrDefault(e => e["CARDTYPE"] == "WEAPON");
        TrackedEntity hero = player.Hero;

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
            player.Hand.Select(c => c.CardId.Length == 0 ? new ObservedHandCard(null, null) : new ObservedHandCard(c.CardId, c.Int("COST"))).ToList(),
            player.DeckCount,
            state.Entities.Values.Count(e => e["CONTROLLER"] == controller && e["ZONE"] == "SECRET"));
    }

    private static ObservedMinion Minion(TrackedEntity m) => new(
        m.CardId, m.Int("ATK"), m.Int("HEALTH"), m.Int("DAMAGE"),
        MinionKeywords.Where(k => Flag(m, k)).ToList(),
        Flag(m, "EXHAUSTED"), m.Int("NUM_ATTACKS_THIS_TURN"), m.Int("NUM_TURNS_IN_PLAY"));

    private static bool Flag(TrackedEntity e, string tag) => e.Int(tag) != 0;
}
