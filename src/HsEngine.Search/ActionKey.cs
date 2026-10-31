using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search;

/// <summary>
/// Identity of an action that is stable across determinizations, so SO-ISMCTS can share statistics between
/// sampled worlds. <c>PlayerTask</c> references and option indices are not: they change with hidden cards.
/// Cards in hand are keyed by card (duplicates are interchangeable); board entities by side and position.
/// </summary>
/// <param name="Type">Kind of action.</param>
/// <param name="Source">Asset id of the source card; 0 when there is none (end turn).</param>
/// <param name="SourcePosition">Board index of an attacking minion; -1 otherwise.</param>
/// <param name="Target">Encoded target: -1 none, 0/1 friendly/enemy hero, 100+i friendly board i, 200+i enemy board i.</param>
/// <param name="Placement">Board position a played minion goes to; -1 for default or non-minions.</param>
/// <param name="Choice">Choose-one index, or the asset id of the picked card for a choice (discover).</param>
public readonly record struct ActionKey(
    PlayerTaskType Type, int Source, int SourcePosition, int Target, int Placement, int Choice)
{
    public static ActionKey From(PlayerTask task)
    {
        Controller me = task.Controller;
        int source = task.Source?.Card.AssetId ?? 0;
        int sourcePosition = task.PlayerTaskType == PlayerTaskType.MINION_ATTACK && task.Source is Minion m
            ? m.ZonePosition
            : -1;

        int choice = task switch
        {
            ChooseTask choose => choose.Choices.Count == 1 ? task.Game.IdEntityDic[choose.Choices[0]].Card.AssetId : -1,
            _ => task.ChooseOne,
        };
        int placement = task is PlayCardTask play ? play.ZonePosition : -1;

        return new ActionKey(task.PlayerTaskType, source, sourcePosition, EncodeTarget(me, task.Target), placement, choice);
    }

    private static int EncodeTarget(Controller me, ICharacter? target) => target switch
    {
        null => -1,
        Hero hero => hero.Controller == me ? 0 : 1,
        Minion minion => (minion.Controller == me ? 100 : 200) + minion.ZonePosition,
        _ => -1,
    };
}
