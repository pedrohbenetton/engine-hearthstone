using System.Text.RegularExpressions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search;

/// <summary>
/// Eliminates symmetric board options in SO-ISMCTS, LethalSolver and rollout policies:
/// <list type="number">
/// <item>Minion <see cref="PlayCardTask.ZonePosition"/> slots collapse to the rightmost slot when nothing observable is positional.</item>
/// <item>Identical ready friendly twin minions attack left-to-right (collapsing <c>k!</c> token permutations to <c>1</c>).</item>
/// <item>Identical twin target minions collapse to the leftmost twin when nothing observable is positional.</item>
/// </list>
/// Only observable zones are inspected, preserving determinization invariance.
/// </summary>
public static partial class BoardPlacement
{
    public static bool CaresAboutPosition(Card card) =>
        card[GameTag.ADJACENT_BUFF] != 0
        || card.Modular
        || (card.Text is { } text && PositionalPattern().IsMatch(text));

    public static bool HasPositionalEffects(Controller me)
    {
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

    public static bool Matters(PlayCardTask play) =>
        play.ZonePosition >= 0
        && play.Source is Minion
        && play.Controller.BoardZone.Count > 0
        && (CaresAboutPosition(play.Source.Card) || HasPositionalEffects(play.Controller));

    public static bool IsCanonicalOption(PlayerTask task)
    {
        Controller me = task.Controller;

        if (task is PlayCardTask { ZonePosition: >= 0, Source: Minion } play
            && !Matters(play)
            && play.ZonePosition != me.BoardZone.Count)
        {
            return false;
        }

        if (HasPositionalEffects(me))
            return true;

        if (task is MinionAttackTask { Source: Minion attacker } && HasEarlierTwin(attacker, requireCanAttack: true))
            return false;

        if (task.Target is Minion target && HasEarlierTwin(target, requireCanAttack: false, exclude: task.Source))
            return false;

        return true;
    }

    /// <summary>
    /// ARCH-0001 §3.3 item 3: prunes strictly dominated self-harm and enemy-buff targets that SabberStone's
    /// generic <c>REQ_TARGET</c> / <c>REQ_MINION_TARGET</c> enumerates on every node.
    /// </summary>
    public static bool IsDominatedOption(PlayerTask task)
    {
        if (task.Target is not ICharacter target || task.Source?.Card.Text is not { Length: > 0 } rawText)
            return false;

        string text = CleanText(rawText);
        Controller me = task.Controller;
        bool friendlyTarget = target.Controller == me;

        if (friendlyTarget && PureDamagePattern().IsMatch(text) && !BeneficialSideEffectPattern().IsMatch(text))
        {
            if (target is Hero)
                return me.SecretZone.Quest is null;
            if (target is Minion m && !m.HasDeathrattle && (m.Card.Text is null || !DamageTriggerPattern().IsMatch(CleanText(m.Card.Text))))
                return true;
        }

        if (!friendlyTarget && target is Minion && PurePositiveBuffPattern().IsMatch(text)
            && !me.HandZone.Any(c => c.Card.Text is { } ht && EnemyAttackThresholdPattern().IsMatch(CleanText(ht))))
        {
            return true;
        }

        return false;
    }

    private static string CleanText(string raw)
    {
        string s = raw.Replace("[x]", " ", StringComparison.Ordinal)
                      .Replace('\u00A0', ' ')
                      .Replace('_', ' ');
        s = HtmlTagPattern().Replace(s, "");
        return WhitespacePattern().Replace(s, " ").Trim();
    }

    public static bool AreIndistinguishableTwins(Minion a, Minion b) =>
        a.Card.AssetId == b.Card.AssetId
        && a.AttackDamage == b.AttackDamage
        && a.Health == b.Health
        && a.Damage == b.Damage
        && a.IsExhausted == b.IsExhausted
        && a.NumAttacksThisTurn == b.NumAttacksThisTurn
        && a.CanAttack == b.CanAttack
        && a.AttackableByRush == b.AttackableByRush
        && a.HasTaunt == b.HasTaunt
        && a.HasDivineShield == b.HasDivineShield
        && a.HasWindfury == b.HasWindfury
        && a.HasStealth == b.HasStealth
        && a.Poisonous == b.Poisonous
        && a.HasLifeSteal == b.HasLifeSteal
        && a[GameTag.REBORN] == b[GameTag.REBORN]
        && a.IsFrozen == b.IsFrozen
        && a.CantAttack == b.CantAttack
        && a.CantAttackHeroes == b.CantAttackHeroes
        && a.CantBeTargetedBySpells == b.CantBeTargetedBySpells
        && a.SpellPower == b.SpellPower
        && a.HasDeathrattle == b.HasDeathrattle
        && (a.AppliedEnchantments?.Count ?? 0) == 0
        && (b.AppliedEnchantments?.Count ?? 0) == 0;

    private static bool HasEarlierTwin(Minion minion, bool requireCanAttack, IPlayable? exclude = null)
    {
        int index = minion.ZonePosition;
        var board = minion.Controller.BoardZone;
        for (int i = 0; i < index && i < board.Count; i++)
        {
            Minion earlier = board[i];
            if (!ReferenceEquals(earlier, exclude)
                && (!requireCanAttack || earlier.CanAttack)
                && AreIndistinguishableTwins(earlier, minion))
            {
                return true;
            }
        }
        return false;
    }

    [GeneratedRegex(@"\b(adjacent|next to|neighbors?|left-?most|right-?most|opposite|either side)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PositionalPattern();

    [GeneratedRegex(@"\bdeal\s+\$?\d+\s+damage\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PureDamagePattern();

    [GeneratedRegex(@"\b(draw|discover|restore|heal|armor|summon|gain|copy|return)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BeneficialSideEffectPattern();

    [GeneratedRegex(@"\b(enrage|takes?\s+damage|damaged|frenzy|overkill|acolyt|berserker|patron)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamageTriggerPattern();

    [GeneratedRegex(@"^(?:Hero Power\s+)?(?:Battlecry:\s*)?Give\s+a\s+minion\s+\+\d+(?:/\+\d+|\s+Attack(?:\s+this\s+turn)?)\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PurePositiveBuffPattern();

    [GeneratedRegex(@"\bwith\s+\d+\s+or\s+more\s+Attack\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnemyAttackThresholdPattern();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
