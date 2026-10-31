using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsEngine.Evaluation;

/// <summary>
/// Hand-tuned weights for the Slice 0 heuristic (ADR-0006). Defaults are starting points to be fitted offline;
/// <see cref="Scale"/> and <see cref="Bias"/> are the logistic calibration Win% = σ(Scale·score + Bias).
/// </summary>
public sealed record EvaluationWeights
{
    /// <summary>Multiplies √(health + armor): concave, so damage hurts more the lower the hero is.</summary>
    public double Health { get; init; } = 6.0;
    /// <summary>Per point of minion attack + health.</summary>
    public double MinionStats { get; init; } = 1.0;
    public double Taunt { get; init; } = 1.0;
    public double DivineShield { get; init; } = 1.5;
    public double Windfury { get; init; } = 1.0;
    public double Stealth { get; init; } = 0.5;
    public double Lifesteal { get; init; } = 0.5;
    /// <summary>Per point of weapon attack × durability.</summary>
    public double Weapon { get; init; } = 0.5;
    public double CardInHand { get; init; } = 2.5;
    /// <summary>Flat term when a side's board damage reaches the other hero's effective health.</summary>
    public double LethalThreat { get; init; } = 15.0;
    public double Scale { get; init; } = 0.08;
    public double Bias { get; init; }
}

/// <summary>
/// Static evaluation of a position from one player's perspective (ARCH-0001 §3.2): health pools, board control,
/// card advantage and lethal threat. Antisymmetric by construction: score(p) = -score(opponent of p).
/// </summary>
public sealed class BoardEvaluator(EvaluationWeights? weights = null)
{
    public const double WinScore = 1_000_000;

    private readonly EvaluationWeights _w = weights ?? new EvaluationWeights();

    public double Evaluate(Game game, Controller perspective)
    {
        Controller opponent = perspective.Opponent;
        bool won = perspective.PlayState == PlayState.WON || (opponent.Hero.IsDead && !perspective.Hero.IsDead);
        bool lost = perspective.PlayState == PlayState.LOST || (perspective.Hero.IsDead && !opponent.Hero.IsDead);
        if (won)
            return WinScore;
        if (lost)
            return -WinScore;

        return Side(perspective, opponent) - Side(opponent, perspective);
    }

    public double WinProbability(Game game, Controller perspective)
    {
        double score = Evaluate(game, perspective);
        if (Math.Abs(score) >= WinScore)
            return score > 0 ? 1.0 : 0.0;
        return 1.0 / (1.0 + Math.Exp(-(_w.Scale * score + _w.Bias)));
    }

    /// <summary>Value of <paramref name="side"/>'s resources, including the threat it poses to <paramref name="other"/>.</summary>
    private double Side(Controller side, Controller other)
    {
        double value = _w.Health * Math.Sqrt(EffectiveHealth(side));

        foreach (Minion minion in side.BoardZone)
            value += MinionValue(minion);

        if (side.Hero.Weapon is { } weapon)
            value += _w.Weapon * weapon.AttackDamage * weapon.Durability;

        value += _w.CardInHand * side.HandZone.Count;

        if (BoardDamage(side, blockers: other) >= EffectiveHealth(other))
            value += _w.LethalThreat;

        return value;
    }

    private double MinionValue(Minion minion)
    {
        double value = _w.MinionStats * (minion.AttackDamage + minion.Health);
        if (minion.HasTaunt) value += _w.Taunt;
        if (minion.HasDivineShield) value += _w.DivineShield;
        if (minion.HasWindfury) value += _w.Windfury;
        if (minion.HasStealth) value += _w.Stealth;
        if (minion.HasLifeSteal) value += _w.Lifesteal;
        return value;
    }

    private static int EffectiveHealth(Controller side) => Math.Max(0, side.Hero.Health + side.Hero.Armor);

    /// <summary>
    /// Damage <paramref name="attacker"/>'s board could push face next turn. Taunts must be cleared first,
    /// approximated by subtracting the blockers' total taunt health.
    /// </summary>
    private static int BoardDamage(Controller attacker, Controller blockers)
    {
        int damage = attacker.Hero.Weapon?.AttackDamage ?? 0;
        foreach (Minion minion in attacker.BoardZone)
            damage += minion.AttackDamage * (minion.HasWindfury ? 2 : 1);

        foreach (Minion minion in blockers.BoardZone)
            if (minion.HasTaunt)
                damage -= minion.Health;

        return damage;
    }
}
