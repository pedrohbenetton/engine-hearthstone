using HsEngine.Search;
using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;

namespace HsEngine.Search.Tests;

public class LethalPuzzleTests
{
    private static readonly Dictionary<string, (Func<ScenarioBuilder> Build, bool Lethal)> Puzzles = new()
    {
        ["minion to face"] = (() => new ScenarioBuilder()
            .WithOpponentHero(3).WithBoard("Bloodfen Raptor"), true),

        ["burn spell"] = (() => new ScenarioBuilder()
            .WithMana(4).WithOpponentHero(6).WithHand("Fireball"), true),

        ["burn one short"] = (() => new ScenarioBuilder()
            .WithMana(4).WithOpponentHero(7).WithHand("Fireball"), false),

        ["armor counts"] = (() => new ScenarioBuilder()
            .WithMana(4).WithOpponentHero(5, armor: 2).WithHand("Fireball"), false),

        ["clear taunt then face"] = (() => new ScenarioBuilder()
            .WithOpponentHero(3).WithBoard("Bloodfen Raptor", "Bloodfen Raptor")
            .WithOpponentBoard("Goldshire Footman"), true),

        ["taunt blocks the only attacker"] = (() => new ScenarioBuilder()
            .WithOpponentHero(3).WithBoard("Bloodfen Raptor")
            .WithOpponentBoard("Goldshire Footman"), false),

        ["buff before attacking"] = (() => new ScenarioBuilder()
            .WithMana(1).WithOpponentHero(5).WithBoard("Bloodfen Raptor").WithHand("Abusive Sergeant"), true),

        ["charge then buff"] = (() => new ScenarioBuilder()
            .WithMana(4).WithOpponentHero(5).WithHand("Abusive Sergeant", "Wolfrider"), true),

        ["charge plus buff over budget"] = (() => new ScenarioBuilder()
            .WithMana(3).WithOpponentHero(5).WithHand("Abusive Sergeant", "Wolfrider"), false),

        ["hero power finishes"] = (() => new ScenarioBuilder()
            .WithHeroes(CardClass.MAGE, CardClass.WARRIOR)
            .WithMana(2).WithOpponentHero(4).WithBoard("Bloodfen Raptor"), true),
    };

    public static TheoryData<string> Names => new(Puzzles.Keys);

    [Theory, MemberData(nameof(Names))]
    public void Solver_classifies_puzzle(string name)
    {
        (Func<ScenarioBuilder> build, bool lethal) = Puzzles[name];

        LethalResult result = new LethalSolver().Solve(build().Build());

        Assert.False(result.Exhausted);
        Assert.Equal(lethal, result.IsLethal);
    }

    [Theory, MemberData(nameof(Names))]
    public void Reported_line_actually_kills(string name)
    {
        (Func<ScenarioBuilder> build, bool lethal) = Puzzles[name];
        if (!lethal)
            return;

        Game game = build().Build();
        LethalResult result = new LethalSolver().Solve(game);

        Game replay = game.DeterministicClone();
        replay.ApplyOptionPath(result.OptionPath);
        Assert.True(replay.OpponentDefeated());
        Assert.Equal(result.OptionPath.Count, result.Line.Count);
    }

    [Fact]
    public void Solver_does_not_mutate_input()
    {
        Game game = Puzzles["clear taunt then face"].Build().Build();
        string before = game.Hash();

        new LethalSolver().Solve(game);

        Assert.Equal(before, game.Hash());
    }

    [Fact]
    public void Node_budget_is_reported_when_exceeded()
    {
        Game game = Puzzles["taunt blocks the only attacker"].Build().Build();

        LethalResult result = new LethalSolver(maxNodes: 1).Solve(game);

        Assert.True(result.Exhausted);
        Assert.False(result.IsLethal);
    }

    [Fact]
    public void Topdeck_dependent_draw_lethal_is_not_marked_deterministic()
    {
        // Arcane Intellect (3 mana) + topdecked Fireball (4 mana) = 7 mana for 6 damage, but deck also has a Raptor.
        Game game = new ScenarioBuilder()
            .WithMana(7)
            .WithOpponentHero(6)
            .WithHand("Arcane Intellect")
            .WithDeck("Fireball", "Bloodfen Raptor", "River Crocolisk")
            .Build();

        LethalResult result = new LethalSolver().Solve(game);
        Assert.False(result.IsDeterministic);
    }
}
