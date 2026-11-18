using System.Text.Json;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsEngine.Evaluation;

/// <summary>
/// Hand-tuned or Python-calibrated weights for the heuristic evaluator (ADR-0002, ADR-0006).
/// <see cref="Scale"/> and <see cref="Bias"/> are the logistic calibration Win% = σ(Scale·score + Bias).
/// </summary>
public sealed record EvaluationWeights
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static EvaluationWeights LoadJson(string json) =>
        JsonSerializer.Deserialize<EvaluationWeights>(json, JsonOptions)
        ?? throw new InvalidDataException("Invalid EvaluationWeights JSON.");

    public static EvaluationWeights LoadFile(string path) => LoadJson(File.ReadAllText(path));

    public static EvaluationWeights? LoadDefaultOrNull()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "weights", "calibrated.json");
        return File.Exists(bundled) ? LoadFile(bundled) : null;
    }
    /// <summary>Multiplies √(health + armor): concave, so damage hurts more the lower the hero is.</summary>
    public double Health { get; init; } = 6.0;
    /// <summary>Per point of minion attack + health.</summary>
    public double MinionStats { get; init; } = 1.0;
    public double Taunt { get; init; } = 1.0;
    public double DivineShield { get; init; } = 1.5;
    public double Windfury { get; init; } = 1.0;
    public double Stealth { get; init; } = 0.5;
    public double Lifesteal { get; init; } = 0.5;
    public double Poisonous { get; init; } = 2.0;
    public double Reborn { get; init; } = 1.2;
    public double SpellPower { get; init; } = 1.0;
    /// <summary>Penalty applied to a frozen minion with positive attack (loses its next swing).</summary>
    public double FrozenPenalty { get; init; } = 0.8;
    /// <summary>Per point of weapon attack × durability.</summary>
    public double Weapon { get; init; } = 0.5;
    public double CardInHand { get; init; } = 2.5;
    /// <summary>Per active secret or quest in <c>SecretZone</c>.</summary>
    public double Secret { get; init; } = 2.0;
    /// <summary>Penalty per point of next fatigue tick when a side's deck is empty.</summary>
    public double FatiguePenalty { get; init; } = 1.5;
    /// <summary>Flat term when a side's board damage reaches the other hero's effective health.</summary>
    public double LethalThreat { get; init; } = 15.0;
    public double Scale { get; init; } = 0.08;
    public double Bias { get; init; }
}

/// <summary>
/// Static evaluation of a position from one player's perspective (ARCH-0001 §3.2): health pools, board control,
/// keyword/spell-power threats, card advantage and lethal threat. Antisymmetric by construction:
/// score(p) = -score(opponent of p).
/// </summary>
public sealed class BoardEvaluator(EvaluationWeights? weights = null)
{
    public const double WinScore = 1_000_000;

    private readonly EvaluationWeights _w = weights ?? EvaluationWeights.LoadDefaultOrNull() ?? new EvaluationWeights();

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
        value += _w.Secret * side.SecretZone.Count;

        if (side.DeckZone.Count == 0)
            value -= _w.FatiguePenalty * (side.Hero.Fatigue + 1);

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
        if (minion.Poisonous) value += _w.Poisonous;
        if (minion[GameTag.REBORN] > 0) value += _w.Reborn;
        if (minion.SpellPower > 0) value += _w.SpellPower * minion.SpellPower;
        if (minion.IsFrozen && minion.AttackDamage > 0) value -= _w.FrozenPenalty;
        return value;
    }

    private static int EffectiveHealth(Controller side) => Math.Max(0, side.Hero.Health + side.Hero.Armor);

    /// <summary>
    /// Damage <paramref name="attacker"/>'s board could push face next turn. Excludes frozen and non-attacking
    /// minions, and subtracts the blockers' total taunt health.
    /// </summary>
    private static int BoardDamage(Controller attacker, Controller blockers)
    {
        int damage = !attacker.Hero.IsFrozen && attacker.Hero.Weapon is { } w ? w.AttackDamage : 0;
        foreach (Minion minion in attacker.BoardZone)
        {
            if (minion.IsFrozen || minion.CantAttack || minion.CantAttackHeroes || minion.AttackDamage <= 0)
                continue;
            damage += minion.AttackDamage * (minion.HasWindfury ? 2 : 1);
        }

        foreach (Minion minion in blockers.BoardZone)
            if (minion.HasTaunt)
                damage -= minion.Health;

        return damage;
    }
}
