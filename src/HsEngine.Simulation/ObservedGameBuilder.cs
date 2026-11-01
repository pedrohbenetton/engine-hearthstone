using HsEngine.Ingestion;
using SabberStoneCore.Actions;
using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsEngine.Simulation;

/// <param name="Proxies">Distinct ids of the built cards that are stat-only proxies (ADR-0008).</param>
/// <param name="Unsupported">Observed cards that could not be built (unknown id or type) and were left out.</param>
/// <param name="HiddenCards">Cards built as <see cref="ProxyCards.HiddenCardId"/> because their identity is unknown.</param>
public sealed record ConversionReport(IReadOnlyList<string> Proxies, IReadOnlyList<string> Unsupported, int HiddenCards);

public sealed record ConvertedGame(Game Game, ConversionReport Report);

/// <summary>
/// Rebuilds an <see cref="ObservedState"/> as a SabberStone game for the search. The local player is always
/// SabberStone's player 1 and must be the one to act. Observed values (stats, damage, keywords, costs, mana) are
/// written over the card definitions, so buffs and auras seen in the log carry over as plain tags. Cards whose
/// identity is unknown, including the local deck until a deck list is supplied, become hidden placeholders.
/// </summary>
public static class ObservedGameBuilder
{
    private static readonly GameTag[] Keywords =
    [
        GameTag.TAUNT, GameTag.DIVINE_SHIELD, GameTag.STEALTH, GameTag.FROZEN, GameTag.WINDFURY, GameTag.POISONOUS,
        GameTag.LIFESTEAL, GameTag.REBORN, GameTag.CHARGE, GameTag.RUSH, GameTag.CANT_ATTACK,
        GameTag.CANT_BE_TARGETED_BY_SPELLS, GameTag.CANT_BE_TARGETED_BY_HERO_POWERS, GameTag.IMMUNE,
    ];

    public static ConvertedGame Build(ObservedState observed, int seed = 0)
    {
        if (observed.CurrentPlayerId != observed.Me.PlayerId)
            throw new ArgumentException("Recommendations are only built for the local player's turn.", nameof(observed));

        ProxyCards.EnsureRegistered();
        var conversion = new Conversion();

        var game = new Game(new GameConfig
        {
            StartPlayer = 1,
            Player1HeroClass = conversion.HeroClass(observed.Me.Hero),
            Player2HeroClass = conversion.HeroClass(observed.Opponent.Hero),
            Player1Deck = [],
            Player2Deck = [],
            FillDecks = false,
            Shuffle = false,
            SkipMulligan = true,
            Logging = false,
            History = false,
            RandomSeed = seed,
        });
        game.StartGame();

        conversion.Side(game.CurrentPlayer, observed.Me);
        conversion.Side(game.CurrentOpponent, observed.Opponent);
        game.DeathProcessingAndAuraUpdate();

        return new ConvertedGame(game, conversion.Report());
    }

    private sealed class Conversion
    {
        private readonly SortedSet<string> _proxies = new(StringComparer.Ordinal);
        private readonly List<string> _unsupported = [];
        private int _hidden;

        public CardClass HeroClass(ObservedHero hero)
        {
            if (Enum.TryParse(hero.Class, out CardClass cardClass) && cardClass is not (CardClass.INVALID or CardClass.NEUTRAL))
                return cardClass;
            _unsupported.Add($"hero class {hero.Class}");
            return CardClass.MAGE;
        }

        public ConversionReport Report() => new(_proxies.ToList(), _unsupported, _hidden);

        public void Side(Controller controller, ObservedSide side)
        {
            // Opening draws from the empty deck dealt fatigue; the coin went to player 2. Both are replaced below.
            controller.Hero.Fatigue = 0;
            foreach (IPlayable card in controller.HandZone.ToList())
                controller.HandZone.Remove(card);

            Hero hero = controller.Hero;
            hero[GameTag.HEALTH] = side.Hero.Health;
            hero.Damage = side.Hero.Damage;
            hero.Armor = side.Hero.Armor;
            hero.IsExhausted = side.Hero.Exhausted;
            hero.NumAttacksThisTurn = side.Hero.AttacksThisTurn;
            hero.HeroPower.IsExhausted = side.Hero.HeroPowerUsed;

            controller.BaseMana = side.Resources;
            controller.UsedMana = side.ResourcesUsed;
            controller.TemporaryMana = side.TemporaryResources;
            controller.OverloadLocked = side.OverloadLocked;

            if (side.Weapon is { } observedWeapon && Find(observedWeapon.CardId) is { } weaponCard)
            {
                if (Entity.FromCard(controller, weaponCard) is Weapon weapon)
                {
                    hero.AddWeapon(weapon);
                    weapon[GameTag.ATK] = observedWeapon.Attack;
                    weapon[GameTag.DURABILITY] = observedWeapon.Durability;
                    weapon[GameTag.DAMAGE] = observedWeapon.Damage;
                }
                else
                    _unsupported.Add(observedWeapon.CardId);
            }

            foreach (ObservedMinion observedMinion in side.Board)
                Summon(controller, observedMinion);

            foreach (ObservedHandCard observedCard in side.Hand)
            {
                Card? card = observedCard.CardId is null ? Hidden() : Find(observedCard.CardId);
                if (card is null)
                    continue;
                IPlayable playable = Generic.DrawCard(controller, card);
                if (observedCard.Cost is int cost && playable.Cost != cost)
                    playable[GameTag.COST] = cost;
            }

            for (int i = 0; i < side.DeckCount; i++)
                Entity.FromCard(controller, Hidden(), zone: controller.DeckZone);
        }

        private void Summon(Controller controller, ObservedMinion observed)
        {
            if (Find(observed.CardId) is not { } card)
                return;
            if (Entity.FromCard(controller, card) is not Minion minion)
            {
                _unsupported.Add(observed.CardId);
                return;
            }
            Generic.SummonBlock.Invoke(controller.Game, minion, -1, null);

            minion[GameTag.ATK] = observed.Attack;
            minion[GameTag.HEALTH] = observed.Health;
            minion.Damage = observed.Damage;
            foreach (GameTag keyword in Keywords)
                minion[keyword] = observed.Keywords.Contains(keyword.ToString()) ? 1 : 0;
            minion.IsExhausted = observed.Exhausted;
            minion.NumAttacksThisTurn = observed.AttacksThisTurn;
            minion[GameTag.NUM_TURNS_IN_PLAY] = observed.TurnsInPlay;
        }

        /// <summary>Card by id, recording proxies and unknown ids for the report.</summary>
        private Card? Find(string cardId)
        {
            Card? card = Cards.All.FirstOrDefault(c => c.Id == cardId);
            if (card is null)
                _unsupported.Add(cardId);
            else if (ProxyCards.IsProxy(cardId))
                _proxies.Add(cardId);
            return card;
        }

        private Card Hidden()
        {
            _hidden++;
            return Cards.FromId(ProxyCards.HiddenCardId);
        }
    }
}
