using HsEngine.Evaluation;
using HsEngine.Simulation;
using SabberStoneCore.Model;

namespace HsEngine.Evaluation.Tests;

public class BoardEvaluatorTests
{
    private readonly BoardEvaluator _evaluator = new();

    private double Score(ScenarioBuilder builder)
    {
        Game game = builder.Build();
        return _evaluator.Evaluate(game, game.CurrentPlayer);
    }

    [Fact]
    public void Mirrored_position_scores_zero()
    {
        Game game = new ScenarioBuilder()
            .WithHero(20).WithOpponentHero(20)
            .WithHand("Fireball").WithOpponentHand("Fireball")
            .WithBoard("Bloodfen Raptor").WithOpponentBoard("Bloodfen Raptor")
            .Build();

        Assert.Equal(0, _evaluator.Evaluate(game, game.CurrentPlayer), precision: 9);
    }

    [Fact]
    public void Score_is_antisymmetric_between_players()
    {
        Game game = new ScenarioBuilder()
            .WithHero(12).WithOpponentHero(25)
            .WithHand("Fireball", "Wolfrider").WithOpponentHand("Goldshire Footman")
            .WithBoard("Bloodfen Raptor").WithOpponentBoard("Goldshire Footman", "Abusive Sergeant")
            .Build();

        Assert.Equal(
            _evaluator.Evaluate(game, game.CurrentPlayer),
            -_evaluator.Evaluate(game, game.CurrentOpponent),
            precision: 9);
    }

    [Fact]
    public void Damaging_opponent_improves_score() =>
        Assert.True(Score(new ScenarioBuilder().WithOpponentHero(20)) > Score(new ScenarioBuilder().WithOpponentHero(25)));

    [Fact]
    public void Taking_damage_worsens_score() =>
        Assert.True(Score(new ScenarioBuilder().WithHero(20)) < Score(new ScenarioBuilder().WithHero(25)));

    [Fact]
    public void Armor_counts_as_health() =>
        Assert.True(Score(new ScenarioBuilder().WithHero(20, armor: 5)) > Score(new ScenarioBuilder().WithHero(20)));

    [Fact]
    public void Same_damage_hurts_more_at_low_health()
    {
        double highLoss = Score(new ScenarioBuilder().WithHero(30)) - Score(new ScenarioBuilder().WithHero(25));
        double lowLoss = Score(new ScenarioBuilder().WithHero(10)) - Score(new ScenarioBuilder().WithHero(5));

        Assert.True(lowLoss > highLoss);
    }

    [Fact]
    public void Friendly_minion_improves_score() =>
        Assert.True(Score(new ScenarioBuilder().WithBoard("Bloodfen Raptor")) > Score(new ScenarioBuilder()));

    [Fact]
    public void Enemy_minion_worsens_score() =>
        Assert.True(Score(new ScenarioBuilder().WithOpponentBoard("Bloodfen Raptor")) < Score(new ScenarioBuilder()));

    [Fact]
    public void Bigger_minion_is_worth_more() =>
        Assert.True(Score(new ScenarioBuilder().WithBoard("Bloodfen Raptor")) > Score(new ScenarioBuilder().WithBoard("Goldshire Footman")));

    [Fact]
    public void Taunt_adds_value_over_same_stats() =>
        // Goldshire Footman (1/2 taunt) vs Abusive Sergeant (2/1): same stat total, taunt should tip it.
        Assert.True(Score(new ScenarioBuilder().WithBoard("Goldshire Footman")) > Score(new ScenarioBuilder().WithBoard("Abusive Sergeant")));

    [Fact]
    public void Card_in_hand_improves_score() =>
        Assert.True(Score(new ScenarioBuilder().WithHand("Fireball")) > Score(new ScenarioBuilder()));

    [Fact]
    public void Facing_lethal_on_board_is_penalised_beyond_linear_health()
    {
        // Opponent's raptor (3 attack) threatens lethal at 3 health but not at 4.
        double atThree = Score(new ScenarioBuilder().WithHero(3).WithOpponentBoard("Bloodfen Raptor"));
        double atFour = Score(new ScenarioBuilder().WithHero(4).WithOpponentBoard("Bloodfen Raptor"));
        double safeGap = Score(new ScenarioBuilder().WithHero(5).WithOpponentBoard("Bloodfen Raptor")) - atFour;

        Assert.True(atFour - atThree > 2 * safeGap);
    }

    [Fact]
    public void Won_game_scores_maximum()
    {
        Game game = new ScenarioBuilder().WithOpponentHero(3).WithBoard("Bloodfen Raptor").Build();
        game.ApplyOptionPath([game.CurrentPlayer.Options().FindIndex(o => o.Target == game.CurrentOpponent.Hero)]);

        Assert.Equal(BoardEvaluator.WinScore, _evaluator.Evaluate(game, game.CurrentPlayer));
        Assert.Equal(-BoardEvaluator.WinScore, _evaluator.Evaluate(game, game.CurrentOpponent));
        Assert.Equal(1.0, _evaluator.WinProbability(game, game.CurrentPlayer), precision: 9);
    }

    [Fact]
    public void Win_probability_is_calibrated_sigmoid()
    {
        Game even = new ScenarioBuilder().Build();
        Game ahead = new ScenarioBuilder().WithOpponentHero(10).WithBoard("Bloodfen Raptor").Build();

        Assert.Equal(0.5, _evaluator.WinProbability(even, even.CurrentPlayer), precision: 9);
        double p = _evaluator.WinProbability(ahead, ahead.CurrentPlayer);
        Assert.InRange(p, 0.5, 1.0);
        Assert.True(p < 1.0);
    }

    [Fact]
    public void Frozen_minion_is_penalised_and_does_not_count_toward_lethal_threat()
    {
        Game unfrozen = new ScenarioBuilder().WithHero(3).WithOpponentBoard("Bloodfen Raptor").Build();
        Game frozen = new ScenarioBuilder().WithHero(3).WithOpponentBoard("Bloodfen Raptor").Build();
        frozen.CurrentOpponent.BoardZone[0].IsFrozen = true;

        double unfrozenScore = _evaluator.Evaluate(unfrozen, unfrozen.CurrentPlayer);
        double frozenScore = _evaluator.Evaluate(frozen, frozen.CurrentPlayer);

        // Freezing a lethal attacker removes both the 15-point LethalThreat and applies the freeze penalty.
        Assert.True(frozenScore - unfrozenScore > 15.0);
    }

    [Fact]
    public void Poisonous_and_spell_power_increase_minion_value()
    {
        // Emperor Cobra (2/3 Poisonous) vs River Crocolisk (2/3 vanilla); Kobold Geomancer (2/2 +1 SP) vs Pint-Sized Summoner (2/2, no keyword).
        Assert.True(Score(new ScenarioBuilder().WithBoard("Emperor Cobra")) > Score(new ScenarioBuilder().WithBoard("River Crocolisk")));
        Assert.True(Score(new ScenarioBuilder().WithBoard("Kobold Geomancer")) > Score(new ScenarioBuilder().WithBoard("Pint-Sized Summoner")));
    }
}
