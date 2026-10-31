using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using Generic = SabberStoneCore.Actions.Generic;

namespace HsEngine.Simulation;

/// <summary>
/// Builds a deterministic mid-turn position for the current player (player 1): hand, board, mana and opponent hero.
/// Decks are empty, logging and history are off, and board minions start ready to attack.
/// </summary>
public sealed class ScenarioBuilder
{
    private CardClass _heroClass = CardClass.WARRIOR;
    private CardClass _opponentClass = CardClass.WARRIOR;
    private int _mana = 10;
    private int _heroHealth = 30;
    private int _heroArmor;
    private int _opponentHealth = 30;
    private int _opponentArmor;
    private readonly List<string> _hand = [];
    private readonly List<string> _opponentHand = [];
    private readonly List<string> _board = [];
    private readonly List<string> _opponentBoard = [];

    public ScenarioBuilder WithHeroes(CardClass hero, CardClass opponent)
    {
        _heroClass = hero;
        _opponentClass = opponent;
        return this;
    }

    public ScenarioBuilder WithMana(int mana)
    {
        _mana = mana;
        return this;
    }

    public ScenarioBuilder WithHero(int health, int armor = 0)
    {
        _heroHealth = health;
        _heroArmor = armor;
        return this;
    }

    public ScenarioBuilder WithOpponentHand(params string[] cardNames)
    {
        _opponentHand.AddRange(cardNames);
        return this;
    }

    public ScenarioBuilder WithOpponentHero(int health, int armor = 0)
    {
        _opponentHealth = health;
        _opponentArmor = armor;
        return this;
    }

    public ScenarioBuilder WithHand(params string[] cardNames)
    {
        _hand.AddRange(cardNames);
        return this;
    }

    public ScenarioBuilder WithBoard(params string[] cardNames)
    {
        _board.AddRange(cardNames);
        return this;
    }

    public ScenarioBuilder WithOpponentBoard(params string[] cardNames)
    {
        _opponentBoard.AddRange(cardNames);
        return this;
    }

    public Game Build()
    {
        var game = new Game(new GameConfig
        {
            StartPlayer = 1,
            Player1HeroClass = _heroClass,
            Player2HeroClass = _opponentClass,
            Player1Deck = [],
            Player2Deck = [],
            FillDecks = false,
            Shuffle = false,
            SkipMulligan = true,
            Logging = false,
            History = false,
            RandomSeed = 0,
        });
        game.StartGame();

        Controller me = game.CurrentPlayer;
        Controller opponent = game.CurrentOpponent;

        // Drawing the opening hand from an empty deck deals fatigue and advances the fatigue counter;
        // the scenario defines health explicitly and starts both counters fresh.
        me.Hero.Fatigue = 0;
        opponent.Hero.Fatigue = 0;
        me.Hero.Damage = 30 - _heroHealth;
        me.Hero.Armor = _heroArmor;
        opponent.Hero.Damage = 30 - _opponentHealth;
        opponent.Hero.Armor = _opponentArmor;

        me.BaseMana = _mana;
        me.UsedMana = 0;

        foreach (string name in _hand)
            Generic.DrawCard(me, Find(name));

        // Player 2 receives The Coin at game start; the scenario defines its hand explicitly.
        foreach (IPlayable card in opponent.HandZone.ToList())
            opponent.HandZone.Remove(card);
        foreach (string name in _opponentHand)
            Generic.DrawCard(opponent, Find(name));

        foreach (string name in _board)
            Summon(game, me, name).IsExhausted = false;

        foreach (string name in _opponentBoard)
            Summon(game, opponent, name);

        game.DeathProcessingAndAuraUpdate();
        return game;
    }

    private static Minion Summon(Game game, Controller owner, string name)
    {
        if (Entity.FromCard(owner, Find(name)) is not Minion minion)
            throw new ArgumentException($"\"{name}\" is not a minion.", nameof(name));

        Generic.SummonBlock.Invoke(game, minion, -1, null);
        return minion;
    }

    private static Card Find(string name) =>
        Cards.FromName(name) ?? throw new ArgumentException($"No collectible card named \"{name}\".", nameof(name));
}
