using HsEngine.Search;
using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search.Tests;

public class BoardPlacementTests
{
    [Fact]
    public void Collapses_minion_slots_into_a_single_root_action_when_nothing_is_positional()
    {
        Game game = new ScenarioBuilder()
            .WithMana(4)
            .WithHand("Chillwind Yeti")
            .WithBoard("Bloodfen Raptor", "River Crocolisk", "Goldshire Footman")
            .Build();

        SearchResult result = new Ismcts().Search(game, new SearchOptions { Iterations = 100, Seed = 1 });

        List<RankedAction> plays = result.Actions.Where(a => a.Key.Type == PlayerTaskType.PLAY_CARD).ToList();
        RankedAction single = Assert.Single(plays);
        Assert.Equal(-1, single.Key.Placement);
    }

    [Fact]
    public void Canonical_option_leaves_existing_board_indices_unchanged()
    {
        Game game = new ScenarioBuilder()
            .WithMana(3)
            .WithHand("Wolfrider")
            .WithBoard("Bloodfen Raptor", "River Crocolisk")
            .Build();

        PlayerTask canonical = Assert.Single(
            game.CurrentPlayer.Options(), t => t is PlayCardTask && BoardPlacement.IsCanonicalOption(t));
        Assert.Equal(2, ((PlayCardTask)canonical).ZonePosition);
    }

    [Fact]
    public void Empty_board_never_splits_or_reports_a_slot_even_for_positional_minions()
    {
        Game game = new ScenarioBuilder()
            .WithMana(4)
            .WithHand("Defender of Argus")
            .Build();

        PlayCardTask play = Assert.Single(game.CurrentPlayer.Options().OfType<PlayCardTask>());
        Assert.False(BoardPlacement.Matters(play));
        Assert.Equal(-1, ActionKey.From(play).Placement);
    }

    [Fact]
    public void Preserves_all_slots_and_finds_between_placement_for_Defender_of_Argus()
    {
        // Hero at 4 HP facing two 4/5 attackers: only placing Defender of Argus at index 1 (between both minions)
        // gives TWO taunts and stops lethal on the opponent's reply.
        Game game = new ScenarioBuilder()
            .WithHero(4)
            .WithMana(4)
            .WithHand("Defender of Argus")
            .WithBoard("River Crocolisk", "Bloodfen Raptor")
            .WithOpponentBoard("Chillwind Yeti", "Chillwind Yeti")
            .Build();
        foreach (var minion in game.CurrentPlayer.BoardZone)
            minion.IsExhausted = true;

        SearchResult result = new Ismcts().Search(game, new SearchOptions { Iterations = 300, Seed = 3 });

        List<RankedAction> placements = result.Actions.Where(a => a.Key.Type == PlayerTaskType.PLAY_CARD).ToList();
        Assert.Equal(3, placements.Count);
        Assert.Equal(1, result.Actions[0].Key.Placement);

        TurnPlan plan = TurnPlanner.From(game, result.Actions[0]);
        Assert.Equal(1, plan.Steps[0].Placement);
        Assert.Contains("posição 2", PlanText.Describe(plan.Steps[0]));
    }

    [Fact]
    public void Friendly_adjacency_aura_on_board_keeps_slots_distinct_for_vanilla_minions()
    {
        Game game = new ScenarioBuilder()
            .WithMana(2)
            .WithHand("Bloodfen Raptor")
            .WithBoard("Dire Wolf Alpha", "River Crocolisk")
            .Build();

        List<ActionKey> keys = game.CurrentPlayer.Options()
            .OfType<PlayCardTask>()
            .Select(t => ActionKey.From(t))
            .Distinct()
            .ToList();

        Assert.Equal([0, 1, 2], keys.Select(k => k.Placement).OrderBy(p => p));
    }

    [Fact]
    public void Lethal_solver_still_finds_positional_Dire_Wolf_lethal()
    {
        // Crocolisk (2) + Raptor (3) = 5 damage; opponent is at 7 HP. Placing Dire Wolf Alpha at index 1 buffs BOTH
        // adjacent minions by +1 Attack for exact 7 damage.
        Game game = new ScenarioBuilder()
            .WithMana(2)
            .WithOpponentHero(7)
            .WithHand("Dire Wolf Alpha")
            .WithBoard("River Crocolisk", "Bloodfen Raptor")
            .Build();

        LethalResult lethal = new LethalSolver().Solve(game);

        Assert.True(lethal.IsLethal);
    }
}
