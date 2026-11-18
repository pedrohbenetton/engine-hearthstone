using System.Text.RegularExpressions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search;

/// <summary>
/// Decides whether the board slot (<see cref="PlayCardTask.ZonePosition"/>) of a played minion can change the game.
/// SabberStone enumerates <c>0..BoardZone.Count</c> for every minion, which splits ISMCTS visits across identical
/// placements and fills the recommendation list with duplicate lines when nothing in play or in hand is positional.
/// Only observable zones (both boards, both weapons, and the mover's own hand) are inspected, so the decision is
/// invariant across determinizations of hidden information.
/// </summary>
public static partial class BoardPlacement
{
    public static bool CaresAboutPosition(Card card) =>
        card[GameTag.ADJACENT_BUFF] != 0
        || card.Modular
        || (card.Text is { } text && PositionalPattern().IsMatch(text));

    public static bool Matters(PlayCardTask play)
    {
        if (play.ZonePosition < 0 || play.Source is not Minion)
            return false;

        Controller me = play.Controller;
        if (me.BoardZone.Count == 0)
            return false;

        if (CaresAboutPosition(play.Source.Card))
            return true;

        foreach (Minion minion in me.BoardZone)
            if (CaresAboutPosition(minion.Card))
                return true;

        foreach (IPlayable card in me.HandZone)
            if (CaresAboutPosition(card.Card))
                return true;

        if (me.Hero.Weapon is { } myWeapon && CaresAboutPosition(myWeapon.Card))
            return true;

        Controller opponent = me.Opponent;
        foreach (Minion minion in opponent.BoardZone)
            if (CaresAboutPosition(minion.Card))
                return true;

        if (opponent.Hero.Weapon is { } opponentWeapon && CaresAboutPosition(opponentWeapon.Card))
            return true;

        return false;
    }

    /// <summary>
    /// Keeps all slots when <see cref="Matters"/> is true, and otherwise only the canonical rightmost slot
    /// (<c>ZonePosition == BoardZone.Count</c>), which leaves existing friendly minion indices untouched.
    /// </summary>
    public static bool IsCanonicalOption(PlayerTask task) =>
        task is not PlayCardTask { ZonePosition: >= 0, Source: Minion } play
        || Matters(play)
        || play.ZonePosition == play.Controller.BoardZone.Count;

    [GeneratedRegex(@"\b(adjacent|next to|neighbors?|left-?most|right-?most|opposite|either side)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PositionalPattern();
}
