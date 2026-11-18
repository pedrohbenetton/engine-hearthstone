using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search;

public enum StepKind { PlayCard, Attack, HeroPower, Choose, EndTurn, Other }

/// <param name="Friendly">True for the local player's side.</param>
/// <param name="BoardIndex">Board index of a minion target; null for a hero.</param>
public sealed record StepTarget(bool Friendly, int? BoardIndex, string CardId);

/// <summary>One concrete step of the recommended turn, with positions as they are when the step is taken.</summary>
/// <param name="HandIndex">Hand index of a played card.</param>
/// <param name="BoardIndex">Board index of an attacking minion.</param>
/// <param name="Placement">Board index a played minion goes to.</param>
public sealed record PlanStep(
    StepKind Kind, string? SourceCardId, int? HandIndex, int? BoardIndex, bool SourceIsHero, StepTarget? Target, int? Placement);

public sealed record TurnPlan(double WinProbability, IReadOnlyList<PlanStep> Steps);

/// <summary>
/// Replays the best line on a copy of the root game (ADR-0009) and records each step with real entities.
/// Positions shift as the turn goes on (a played minion pushes the board), so they are read at each step.
/// The plan stops at the first action that is no longer legal, or at the end of the turn.
/// </summary>
public static class TurnPlanner
{
    public static TurnPlan From(Game root, RankedAction best)
    {
        Game game = root.DeterministicClone();
        var steps = new List<PlanStep>();
        foreach (ActionKey key in (IEnumerable<ActionKey>)[best.Key, .. best.ContinuationKeys ?? []])
        {
            PlayerTask? task = SelectTask(game.CurrentPlayer.Options(), key);
            if (task is null)
                break;
            steps.Add(Step(game, task, key));
            if (task.PlayerTaskType == PlayerTaskType.END_TURN || !game.Process(task))
                break;
        }
        return new TurnPlan(best.WinProbability, steps);
    }

    private static PlayerTask? SelectTask(List<PlayerTask> options, ActionKey key) =>
        key.Placement >= 0
            ? options.FirstOrDefault(t => ActionKey.From(t, preservePlacement: true) == key)
            : options.FirstOrDefault(t => BoardPlacement.IsCanonicalOption(t) && ActionKey.From(t) == key);

    private static PlanStep Step(Game game, PlayerTask task, ActionKey key)
    {
        Controller me = game.CurrentPlayer;
        // SabberStone's HeroAttackTask leaves Source empty; the attacker is the hero.
        IPlayable? source = task.PlayerTaskType == PlayerTaskType.HERO_ATTACK ? me.Hero : task.Source;
        StepKind kind = task.PlayerTaskType switch
        {
            PlayerTaskType.PLAY_CARD => StepKind.PlayCard,
            PlayerTaskType.MINION_ATTACK or PlayerTaskType.HERO_ATTACK => StepKind.Attack,
            PlayerTaskType.HERO_POWER => StepKind.HeroPower,
            PlayerTaskType.CHOOSE => StepKind.Choose,
            PlayerTaskType.END_TURN => StepKind.EndTurn,
            _ => StepKind.Other,
        };

        string? sourceCardId = task is ChooseTask { Choices.Count: 1 } choose
            ? game.IdEntityDic[choose.Choices[0]].Card.Id
            : source?.Card.Id;
        StepTarget? target = task.Target switch
        {
            Hero hero => new StepTarget(hero.Controller == me, null, hero.Card.Id),
            Minion minion => new StepTarget(minion.Controller == me, minion.ZonePosition, minion.Card.Id),
            _ => null,
        };

        return new PlanStep(
            kind,
            sourceCardId,
            source?.Zone?.Type == Zone.HAND ? source.ZonePosition : null,
            kind == StepKind.Attack && source is Minion attacker ? attacker.ZonePosition : null,
            kind == StepKind.Attack && source is Hero,
            target,
            key.Placement >= 0 && task is PlayCardTask { ZonePosition: >= 0 } play && source is Minion ? play.ZonePosition : null);
    }
}

/// <summary>Portuguese text of a plan step for the overlay and the terminal.</summary>
public static class PlanText
{
    public static string Describe(PlanStep step, Func<string, string?> names)
    {
        string Name(string? id) => id is null ? "?" : names(id) ?? id;

        string Target(StepTarget t) => (t.BoardIndex, t.Friendly) switch
        {
            (null, true) => "seu herói",
            (null, false) => "herói inimigo",
            (_, true) => $"{Name(t.CardId)} (seu)",
            (_, false) => $"{Name(t.CardId)} (inimigo)",
        };

        return step.Kind switch
        {
            StepKind.PlayCard => (step.Placement, step.Target) switch
            {
                (int p, { } t) => $"Jogue {Name(step.SourceCardId)} na posição {p + 1}, alvo {Target(t)}",
                (int p, null) => $"Jogue {Name(step.SourceCardId)} na posição {p + 1}",
                (null, { } t) => $"Jogue {Name(step.SourceCardId)} em {Target(t)}",
                _ => $"Jogue {Name(step.SourceCardId)}",
            },
            StepKind.Attack => $"Ataque com {(step.SourceIsHero ? "seu herói" : Name(step.SourceCardId))} → {(step.Target is { } t ? Target(t) : "?")}",
            StepKind.HeroPower => step.Target is { } target ? $"Use o poder heroico em {Target(target)}" : "Use o poder heroico",
            StepKind.Choose => $"Escolha {Name(step.SourceCardId)}",
            StepKind.EndTurn => "Encerre o turno",
            _ => Name(step.SourceCardId),
        };
    }

    /// <summary>Names from the pt-BR snapshot, then SabberStone's English name, then the id.</summary>
    public static string Describe(PlanStep step) =>
        Describe(step, id => CardNames.PtBR(id) ?? Cards.All.FirstOrDefault(c => c.Id == id)?.Name);
}
