using HsEngine.Search;
using HsEngine.Simulation;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search.Tests;

/// <summary>ADR-0009: the best line becomes concrete steps (source, target, positions) for the overlay.</summary>
public class TurnPlannerTests
{
    private static ActionKey Key(Game game, Func<PlayerTask, bool> match) =>
        ActionKey.From(game.CurrentPlayer.Options().First(match));

    private static RankedAction Line(ActionKey first, params ActionKey[] continuation) =>
        new(first, "", null, 1, 0.75, 0, 0, [], continuation);

    [Fact]
    public void Spell_on_an_enemy_minion_then_end_turn()
    {
        Game game = new ScenarioBuilder().WithMana(4).WithHand("Fireball").WithOpponentBoard("Bloodfen Raptor").Build();
        ActionKey fireball = Key(game, t => t is PlayCardTask { Target: Minion });

        TurnPlan plan = TurnPlanner.From(game, Line(fireball, new ActionKey(PlayerTaskType.END_TURN, 0, -1, -1, -1, 0)));

        Assert.Equal(0.75, plan.WinProbability);
        Assert.Equal(2, plan.Steps.Count);
        PlanStep cast = plan.Steps[0];
        Assert.Equal((StepKind.PlayCard, "CS2_029", 0), (cast.Kind, cast.SourceCardId, cast.HandIndex));
        Assert.Equal(new StepTarget(Friendly: false, BoardIndex: 0, "CS2_172"), cast.Target);
        Assert.Equal(StepKind.EndTurn, plan.Steps[1].Kind);
    }

    [Fact]
    public void Attacks_keep_the_board_index_of_attacker_and_target_after_earlier_steps()
    {
        Game game = new ScenarioBuilder().WithMana(1)
            .WithHand("Abusive Sergeant")
            .WithBoard("Dire Wolf Alpha", "River Crocolisk")
            .Build();
        // Dire Wolf makes board position relevant; playing the sergeant at the left shifts the crocolisk from 1 to 2.
        ActionKey play = Key(game, t => t is PlayCardTask { ZonePosition: 0, Target: Minion { Card.Name: "River Crocolisk" } });
        Game after = game.DeterministicClone();
        after.Process(after.CurrentPlayer.Options().First(t => ActionKey.From(t) == play));
        ActionKey attack = Key(after, t => t is MinionAttackTask { Source: Minion { Card.Name: "River Crocolisk" }, Target: Hero });

        TurnPlan plan = TurnPlanner.From(game, Line(play, attack));

        Assert.Equal((StepKind.PlayCard, 0), (plan.Steps[0].Kind, plan.Steps[0].Placement));
        Assert.Equal(new StepTarget(true, 1, "CS2_120"), plan.Steps[0].Target);
        PlanStep hit = plan.Steps[1];
        Assert.Equal((StepKind.Attack, "CS2_120", 2, false), (hit.Kind, hit.SourceCardId, hit.BoardIndex, hit.SourceIsHero));
        Assert.Equal(new StepTarget(false, null, game.CurrentOpponent.Hero.Card.Id), hit.Target);
    }

    [Fact]
    public void Minion_step_omits_board_position_when_placement_does_not_matter()
    {
        Game game = new ScenarioBuilder().WithMana(4)
            .WithHand("Chillwind Yeti")
            .WithBoard("Bloodfen Raptor", "River Crocolisk")
            .Build();
        ActionKey play = Key(game, t => t is PlayCardTask && BoardPlacement.IsCanonicalOption(t));

        TurnPlan plan = TurnPlanner.From(game, Line(play));

        Assert.Equal(-1, play.Placement);
        Assert.Null(plan.Steps[0].Placement);
        Assert.DoesNotContain("posição", PlanText.Describe(plan.Steps[0]));
    }

    [Fact]
    public void Hero_attack_is_attributed_to_the_hero()
    {
        Game game = new ScenarioBuilder().WithHeroes(SabberStoneCore.Enums.CardClass.WARRIOR, SabberStoneCore.Enums.CardClass.MAGE)
            .WithMana(3).WithHand("Fiery War Axe").Build();
        ActionKey equip = Key(game, t => t is PlayCardTask);
        Game armed = game.DeterministicClone();
        armed.Process(armed.CurrentPlayer.Options().First(t => ActionKey.From(t) == equip));
        ActionKey swing = Key(armed, t => t.PlayerTaskType == PlayerTaskType.HERO_ATTACK);

        PlanStep hit = TurnPlanner.From(game, Line(equip, swing)).Steps[1];

        Assert.Equal((StepKind.Attack, true), (hit.Kind, hit.SourceIsHero));
        Assert.Equal(game.CurrentPlayer.Hero.Card.Id, hit.SourceCardId);
    }

    [Fact]
    public void A_step_that_is_no_longer_legal_ends_the_plan()
    {
        Game game = new ScenarioBuilder().WithMana(4).WithHand("Fireball").WithOpponentBoard("Bloodfen Raptor").Build();
        ActionKey fireball = Key(game, t => t is PlayCardTask { Target: Minion });

        TurnPlan plan = TurnPlanner.From(game, Line(fireball, fireball));

        Assert.Single(plan.Steps);
    }

    [Fact]
    public void Planning_does_not_touch_the_root_game()
    {
        Game game = new ScenarioBuilder().WithMana(4).WithHand("Fireball").WithOpponentBoard("Bloodfen Raptor").Build();
        ActionKey fireball = Key(game, t => t is PlayCardTask { Target: Minion });

        TurnPlanner.From(game, Line(fireball));

        Assert.Single(game.CurrentPlayer.HandZone);
        Assert.Single(game.CurrentOpponent.BoardZone);
    }
}

public class PlanTextTests
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["CS2_029"] = "Bola de Fogo", ["CS2_172"] = "Raptor", ["CS2_120"] = "Crocolisco", ["CS2_188"] = "Sargento", ["HERO_08"] = "Jaina",
    };

    private static string Describe(PlanStep step) => PlanText.Describe(step, id => Names.GetValueOrDefault(id));

    [Fact]
    public void Describes_each_kind_of_step_in_portuguese()
    {
        Assert.Equal("Jogue Bola de Fogo em Raptor (inimigo)",
            Describe(new PlanStep(StepKind.PlayCard, "CS2_029", 0, null, false, new StepTarget(false, 0, "CS2_172"), null)));
        Assert.Equal("Jogue Sargento na posição 1, alvo Crocolisco (seu)",
            Describe(new PlanStep(StepKind.PlayCard, "CS2_188", 0, null, false, new StepTarget(true, 1, "CS2_120"), 0)));
        Assert.Equal("Jogue Crocolisco na posição 3",
            Describe(new PlanStep(StepKind.PlayCard, "CS2_120", 1, null, false, null, 2)));
        Assert.Equal("Ataque com Crocolisco → herói inimigo",
            Describe(new PlanStep(StepKind.Attack, "CS2_120", null, 2, false, new StepTarget(false, null, "HERO_08"), null)));
        Assert.Equal("Ataque com seu herói → Raptor (inimigo)",
            Describe(new PlanStep(StepKind.Attack, "HERO_08", null, null, true, new StepTarget(false, 0, "CS2_172"), null)));
        Assert.Equal("Use o poder heroico em herói inimigo",
            Describe(new PlanStep(StepKind.HeroPower, "HERO_08bp", null, null, false, new StepTarget(false, null, "HERO_08"), null)));
        Assert.Equal("Escolha Bola de Fogo",
            Describe(new PlanStep(StepKind.Choose, "CS2_029", null, null, false, null, null)));
        Assert.Equal("Encerre o turno", Describe(new PlanStep(StepKind.EndTurn, null, null, null, false, null, null)));
    }

    [Fact]
    public void Unknown_names_fall_back_to_the_card_id()
    {
        Assert.Equal("Jogue XYZ_999", Describe(new PlanStep(StepKind.PlayCard, "XYZ_999", 0, null, false, null, null)));
    }

    [Fact]
    public void Real_names_come_from_the_ptBR_snapshot()
    {
        Assert.Equal("Bordoeira Escarlate", CardNames.PtBR("JAIL_328"));
        Assert.Null(CardNames.PtBR("NOT_A_CARD"));
    }
}
