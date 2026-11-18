using System.Text.RegularExpressions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search;

/// <summary>
/// ADR-0004 (reviewer adjustment): actions that reveal hidden information — drawing, discovering or generating random
/// cards, or trading off a draw deathrattle — are expanded before deterministic board actions so lines that depend on
/// revealed outcomes are not committed ahead of the reveal.
/// </summary>
public static partial class InformationActions
{
    public static bool RevealsInformation(Card card) => card.Text is { } text && RevealPattern().IsMatch(text);

    public static bool RevealsInformation(PlayerTask task)
    {
        if (task.PlayerTaskType is PlayerTaskType.PLAY_CARD or PlayerTaskType.HERO_POWER
            && task.Source is { } source)
        {
            Card card = source.Card;
            if (card.Type == CardType.MINION && card[GameTag.DEATHRATTLE] > 0 && card[GameTag.BATTLECRY] == 0)
                return false;
            return RevealsInformation(card);
        }

        if (task.PlayerTaskType == PlayerTaskType.MINION_ATTACK
            && task.Source is Minion attacker
            && task.Target is Minion defender)
        {
            bool attackerDiesAndDraws = attacker.HasDeathrattle
                                        && !attacker.HasDivineShield
                                        && defender.AttackDamage >= attacker.Health
                                        && RevealsInformation(attacker.Card);
            bool defenderDiesAndDraws = defender.HasDeathrattle
                                        && !defender.HasDivineShield
                                        && attacker.AttackDamage >= defender.Health
                                        && RevealsInformation(defender.Card);
            return attackerDiesAndDraws || defenderDiesAndDraws;
        }

        return false;
    }

    [GeneratedRegex(@"\b(draw|discover)\b|(?:add|get) a random\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RevealPattern();
}
