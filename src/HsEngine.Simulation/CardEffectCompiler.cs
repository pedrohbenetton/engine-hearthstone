using System.Globalization;
using System.Text.RegularExpressions;
using SabberStoneCore.Actions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;

namespace HsEngine.Simulation;

internal sealed record CompiledCardEffect(
    Power? Power,
    Dictionary<PlayReq, int> PlayRequirements,
    int SpellPower,
    bool HasDeathrattle);

/// <summary>
/// Synthesizes executable SabberStone <see cref="Power"/> + <see cref="PlayReq"/> definitions directly from structured
/// HearthstoneJSON card text (ADR-0008). Conservative by construction: a card is compiled ONLY when 100% of its
/// active effect clauses match known exact semantics; anything conditional or ambiguous returns <c>null</c> so the
/// card stays a transparent proxy.
/// </summary>
internal static partial class CardEffectCompiler
{
    public static string NormalizeForAlias(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        string s = raw;
        int at = s.IndexOf('@');
        if (at >= 0)
            s = s[..at];
        s = s.Replace("[x]", " ", StringComparison.Ordinal)
             .Replace("&", " ", StringComparison.Ordinal)
             .Replace('\u00A0', ' ')
             .Replace("_", " ", StringComparison.Ordinal);
        s = ItalicReminderRegex().Replace(s, " ");
        s = HtmlTagRegex().Replace(s, " ");
        s = s.Replace("$", "", StringComparison.Ordinal).Replace("#", "", StringComparison.Ordinal);
        return WhitespaceRegex().Replace(s, " ").Trim().ToLowerInvariant();
    }

    public static CompiledCardEffect? TryCompile(CardData data, CardType type)
    {
        if (string.IsNullOrWhiteSpace(data.Text))
            return null;

        string text = CleanText(data.Text);
        if (text.Length == 0)
            return null;

        int spellPower = 0;
        text = SpellDamageRegex().Replace(text, m =>
        {
            spellPower += int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return " ";
        });

        // Strip passive keywords already modelled by GameTags, and Start-of-Game clauses that already resolved on turn 0.
        text = StartOfGameSentenceRegex().Replace(text, " ");
        text = LeadingKeywordsRegex().Replace(text, " ").Trim();

        if (text.Length == 0)
            return spellPower > 0 ? new CompiledCardEffect(null, [], spellPower, false) : null;

        if (type == CardType.SPELL)
        {
            if (!TryCompileClauseList(text, isSpell: true, allowTargeting: true, out ISimpleTask? spellTask, out Dictionary<PlayReq, int>? reqs))
                return null;
            return new CompiledCardEffect(new Power { PowerTask = spellTask }, reqs!, spellPower, false);
        }

        if (type is CardType.MINION or CardType.WEAPON)
        {
            return TryCompileMinionOrWeapon(text, spellPower);
        }

        return null;
    }

    private static CompiledCardEffect? TryCompileMinionOrWeapon(string text, int spellPower)
    {
        string? battlecryPart = null;
        string? deathrattlePart = null;

        Match both = BattlecryThenDeathrattleRegex().Match(text);
        if (both.Success)
        {
            battlecryPart = both.Groups[1].Value.Trim();
            deathrattlePart = both.Groups[2].Value.Trim();
        }
        else if (DeathrattleThenBattlecryRegex().Match(text) is { Success: true } reverse)
        {
            deathrattlePart = reverse.Groups[1].Value.Trim();
            battlecryPart = reverse.Groups[2].Value.Trim();
        }
        else if (BattlecryOnlyRegex().Match(text) is { Success: true } bc)
        {
            battlecryPart = bc.Groups[1].Value.Trim();
        }
        else if (DeathrattleOnlyRegex().Match(text) is { Success: true } dr)
        {
            deathrattlePart = dr.Groups[1].Value.Trim();
        }
        else
        {
            return null;
        }

        ISimpleTask? powerTask = null;
        ISimpleTask? deathrattleTask = null;
        var playReqs = new Dictionary<PlayReq, int>();

        if (battlecryPart is not null)
        {
            if (!TryCompileClauseList(battlecryPart, isSpell: false, allowTargeting: true, out powerTask, out Dictionary<PlayReq, int>? bcReqs))
                return null;
            playReqs = bcReqs!;
        }

        if (deathrattlePart is not null)
        {
            if (!TryCompileClauseList(deathrattlePart, isSpell: false, allowTargeting: false, out deathrattleTask, out _))
                return null;
        }

        var power = new Power { PowerTask = powerTask, DeathrattleTask = deathrattleTask };
        return new CompiledCardEffect(power, playReqs, spellPower, deathrattleTask is not null);
    }

    private static bool TryCompileClauseList(
        string block,
        bool isSpell,
        bool allowTargeting,
        out ISimpleTask? combinedTask,
        out Dictionary<PlayReq, int>? requirements)
    {
        combinedTask = null;
        requirements = null;

        string[] rawSentences = block.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawSentences.Length == 0 || rawSentences.Length > 3)
            return false;

        var tasks = new List<ISimpleTask>(rawSentences.Length);
        var mergedReqs = new Dictionary<PlayReq, int>();
        bool hasTargetClause = false;

        foreach (string raw in rawSentences)
        {
            string clause = raw.Trim(' ', ',', ';', ':');
            if (clause.Length == 0)
                continue;

            if (!TryCompileSingleClause(clause, isSpell, out ISimpleTask? task, out Dictionary<PlayReq, int>? clauseReqs))
                return false;

            if (clauseReqs is { Count: > 0 })
            {
                if (!allowTargeting || hasTargetClause)
                    return false;
                hasTargetClause = true;
                foreach ((PlayReq k, int v) in clauseReqs)
                    mergedReqs[k] = v;
            }

            tasks.Add(task!);
        }

        if (tasks.Count == 0)
            return false;

        combinedTask = tasks.Count == 1 ? tasks[0] : new SequenceTask(tasks.ToArray());
        requirements = mergedReqs;
        return true;
    }

    private static bool TryCompileSingleClause(
        string clause,
        bool isSpell,
        out ISimpleTask? task,
        out Dictionary<PlayReq, int>? reqs)
    {
        task = null;
        reqs = null;

        // 1. Card draw & minion tutoring
        if (string.Equals(clause, "Draw a card", StringComparison.OrdinalIgnoreCase))
        {
            task = new DrawTask(1);
            return true;
        }
        if (DrawCardsRegex().Match(clause) is { Success: true } drawN)
        {
            task = new DrawTask(ParseCount(drawN.Groups[1].Value));
            return true;
        }
        if (string.Equals(clause, "Draw a minion", StringComparison.OrdinalIgnoreCase))
        {
            task = new DrawFilteredMinionTask(TutorMode.Any);
            return true;
        }
        if (string.Equals(clause, "Draw your highest Cost minion", StringComparison.OrdinalIgnoreCase))
        {
            task = new DrawFilteredMinionTask(TutorMode.HighestCost);
            return true;
        }
        if (string.Equals(clause, "Draw your lowest Cost minion", StringComparison.OrdinalIgnoreCase))
        {
            task = new DrawFilteredMinionTask(TutorMode.LowestCost);
            return true;
        }

        // 2. Armor & Mana Crystals
        if (GainArmorRegex().Match(clause) is { Success: true } armor)
        {
            task = new ArmorTask(int.Parse(armor.Groups[1].Value, CultureInfo.InvariantCulture));
            return true;
        }
        if (GainTempManaRegex().Match(clause) is { Success: true } tempMana)
        {
            task = new TempManaTask(ParseCount(tempMana.Groups[1].Value));
            return true;
        }
        if (GainEmptyManaRegex().Match(clause) is { Success: true } emptyMana)
        {
            task = new ManaCrystalEmptyTask(ParseCount(emptyMana.Groups[1].Value));
            return true;
        }
        if (GainFullManaRegex().Match(clause) is { Success: true } fullMana)
        {
            task = new GainFullManaTask(ParseCount(fullMana.Groups[1].Value));
            return true;
        }

        // 3. Untargeted & Targeted Damage
        if (SwipeDamageRegex().Match(clause) is { Success: true } swipe)
        {
            int primary = int.Parse(swipe.Groups[1].Value, CultureInfo.InvariantCulture);
            int splash = int.Parse(swipe.Groups[2].Value, CultureInfo.InvariantCulture);
            reqs = TargetReqs(isSpell, enemyOnly: true, minionOnly: false);
            task = new SequenceTask([
                new DamageTask(primary, EntityType.TARGET, isSpell),
                new DamageTask(splash, EntityType.ENEMIES_NOTARGET, isSpell),
            ]);
            return true;
        }
        if (SplitDamageRegex().Match(clause) is { Success: true } split)
        {
            int missiles = int.Parse(split.Groups[1].Value, CultureInfo.InvariantCulture);
            bool minionsOnly = split.Groups[2].Value.Contains("minion", StringComparison.OrdinalIgnoreCase);
            task = new SplitRandomDamageTask(missiles, minionsOnly, isSpell);
            return true;
        }
        if (DamageScopeRegex().Match(clause) is { Success: true } dmgScope)
        {
            int amount = int.Parse(dmgScope.Groups[1].Value, CultureInfo.InvariantCulture);
            string targetPhrase = dmgScope.Groups[2].Value.Trim().ToLowerInvariant();
            switch (targetPhrase)
            {
                case "":
                    reqs = TargetReqs(isSpell, enemyOnly: false, minionOnly: false);
                    task = new DamageTask(amount, EntityType.TARGET, isSpell);
                    return true;
                case "to a minion":
                    reqs = TargetReqs(isSpell, enemyOnly: false, minionOnly: true);
                    task = new DamageTask(amount, EntityType.TARGET, isSpell);
                    return true;
                case "to an enemy minion":
                    reqs = TargetReqs(isSpell, enemyOnly: true, minionOnly: true);
                    task = new DamageTask(amount, EntityType.TARGET, isSpell);
                    return true;
                case "to an undamaged minion":
                    reqs = TargetReqs(isSpell, enemyOnly: false, minionOnly: true, PlayReq.REQ_UNDAMAGED_TARGET);
                    task = new DamageTask(amount, EntityType.TARGET, isSpell);
                    return true;
                case "to an enemy":
                    reqs = TargetReqs(isSpell, enemyOnly: true, minionOnly: false);
                    task = new DamageTask(amount, EntityType.TARGET, isSpell);
                    return true;
                case "to the enemy hero":
                    task = new DamageTask(amount, EntityType.OP_HERO, isSpell);
                    return true;
                case "to your hero":
                    task = new DamageTask(amount, EntityType.HERO, isSpell);
                    return true;
                case "to all minions":
                    task = new DamageTask(amount, EntityType.ALLMINIONS, isSpell);
                    return true;
                case "to all other minions":
                    task = new DamageTask(amount, EntityType.ALLMINIONS_NOSOURCE, isSpell);
                    return true;
                case "to all enemy minions":
                    task = new DamageTask(amount, EntityType.OP_MINIONS, isSpell);
                    return true;
                case "to all enemies":
                    task = new DamageTask(amount, EntityType.ENEMIES, isSpell);
                    return true;
            }
        }

        // 4. Healing
        if (HealScopeRegex().Match(clause) is { Success: true } heal)
        {
            int amount = int.Parse(heal.Groups[1].Value, CultureInfo.InvariantCulture);
            string who = heal.Groups[2].Value.Trim().ToLowerInvariant();
            switch (who)
            {
                case "":
                    reqs = TargetReqs(isSpell, enemyOnly: false, minionOnly: false);
                    task = new HealTask(amount, EntityType.TARGET);
                    return true;
                case "to your hero":
                    task = new HealTask(amount, EntityType.HERO);
                    return true;
                case "to all friendly characters":
                    task = new HealTask(amount, EntityType.FRIENDS);
                    return true;
            }
        }

        // 5. Destroy, Silence & Freeze
        switch (clause.ToLowerInvariant())
        {
            case "destroy a minion":
                reqs = TargetReqs(isSpell, enemyOnly: false, minionOnly: true);
                task = new DestroyTask(EntityType.TARGET);
                return true;
            case "destroy an enemy minion":
                reqs = TargetReqs(isSpell, enemyOnly: true, minionOnly: true);
                task = new DestroyTask(EntityType.TARGET);
                return true;
            case "destroy a damaged minion":
                reqs = TargetReqs(isSpell, enemyOnly: false, minionOnly: true, PlayReq.REQ_DAMAGED_TARGET);
                task = new DestroyTask(EntityType.TARGET);
                return true;
            case "destroy a damaged enemy minion":
                reqs = TargetReqs(isSpell, enemyOnly: true, minionOnly: true, PlayReq.REQ_DAMAGED_TARGET);
                task = new DestroyTask(EntityType.TARGET);
                return true;
            case "destroy all minions":
                task = new DestroyTask(EntityType.ALLMINIONS);
                return true;
            case "silence a minion":
                reqs = TargetReqs(isSpell, enemyOnly: false, minionOnly: true);
                task = new SilenceTask(EntityType.TARGET);
                return true;
            case "silence an enemy minion":
                reqs = TargetReqs(isSpell, enemyOnly: true, minionOnly: true);
                task = new SilenceTask(EntityType.TARGET);
                return true;
            case "freeze a minion":
                reqs = TargetReqs(isSpell, enemyOnly: false, minionOnly: true);
                task = new FreezeEntitiesTask(EntityType.TARGET);
                return true;
            case "freeze an enemy":
                reqs = TargetReqs(isSpell, enemyOnly: true, minionOnly: false);
                task = new FreezeEntitiesTask(EntityType.TARGET);
                return true;
            case "freeze all enemy minions":
                task = new FreezeEntitiesTask(EntityType.OP_MINIONS);
                return true;
        }

        // 6. Stat & Keyword Buffs
        if (BuffTargetStatsRegex().Match(clause) is { Success: true } buff)
        {
            bool friendlyOnly = buff.Groups[1].Value.Equals("friendly ", StringComparison.OrdinalIgnoreCase);
            int atk = int.Parse(buff.Groups[2].Value, CultureInfo.InvariantCulture);
            int hp = int.Parse(buff.Groups[3].Value, CultureInfo.InvariantCulture);
            GameTag? kw = ParseBonusKeyword(buff.Groups[4].Value);
            if (buff.Groups[4].Success && kw is null)
                return false;

            reqs = TargetReqs(isSpell, enemyOnly: false, minionOnly: true, friendlyOnly ? PlayReq.REQ_FRIENDLY_TARGET : null);
            task = new StatBuffTask(EntityType.TARGET, atk, hp, kw);
            return true;
        }
        if (BuffBoardStatsRegex().Match(clause) is { Success: true } boardBuff)
        {
            int atk = int.Parse(boardBuff.Groups[1].Value, CultureInfo.InvariantCulture);
            int hp = int.Parse(boardBuff.Groups[2].Value, CultureInfo.InvariantCulture);
            task = new StatBuffTask(EntityType.MINIONS, atk, hp, null);
            return true;
        }

        return false;
    }

    private static Dictionary<PlayReq, int> TargetReqs(bool isSpell, bool enemyOnly, bool minionOnly, PlayReq? extra = null)
    {
        var reqs = new Dictionary<PlayReq, int>
        {
            [isSpell ? PlayReq.REQ_TARGET_TO_PLAY : PlayReq.REQ_TARGET_IF_AVAILABLE] = 0,
        };
        if (!isSpell)
            reqs[PlayReq.REQ_NONSELF_TARGET] = 0;
        if (enemyOnly)
            reqs[PlayReq.REQ_ENEMY_TARGET] = 0;
        if (minionOnly)
            reqs[PlayReq.REQ_MINION_TARGET] = 0;
        if (extra is PlayReq r)
            reqs[r] = 0;
        return reqs;
    }

    private static GameTag? ParseBonusKeyword(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "" => null,
        "taunt" => GameTag.TAUNT,
        "divine shield" => GameTag.DIVINE_SHIELD,
        "rush" => GameTag.RUSH,
        "lifesteal" => GameTag.LIFESTEAL,
        "windfury" => GameTag.WINDFURY,
        "poisonous" => GameTag.POISONOUS,
        _ => null,
    };

    private static int ParseCount(string wordOrDigits) => wordOrDigits.Trim().ToLowerInvariant() switch
    {
        "a" or "an" or "one" => 1,
        "two" => 2,
        "three" => 3,
        "four" => 4,
        "five" => 5,
        var s => int.Parse(s, CultureInfo.InvariantCulture),
    };

    private static string CleanText(string raw)
    {
        string s = raw;
        int at = s.IndexOf('@');
        if (at >= 0)
            s = s[..at];
        s = s.Replace("[x]", " ", StringComparison.Ordinal)
             .Replace("&", " ", StringComparison.Ordinal)
             .Replace('\u00A0', ' ')
             .Replace("_", " ", StringComparison.Ordinal);
        s = ItalicReminderRegex().Replace(s, " ");
        s = HtmlTagRegex().Replace(s, " ");
        s = s.Replace("$", "", StringComparison.Ordinal).Replace("#", "", StringComparison.Ordinal);
        return WhitespaceRegex().Replace(s, " ").Trim();
    }

    private enum TutorMode { Any, HighestCost, LowestCost }

    private sealed class SequenceTask(ISimpleTask[] tasks) : SimpleTask
    {
        public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target, in TaskStack? stack = null)
        {
            TaskStack shared = stack ?? new TaskStack();
            foreach (ISimpleTask step in tasks)
            {
                TaskState state = step.Process(in game, in controller, in source, in target, in shared);
                if (state == TaskState.STOP)
                    return TaskState.STOP;
            }
            return TaskState.COMPLETE;
        }
    }

    private sealed class DrawFilteredMinionTask(TutorMode mode) : SimpleTask
    {
        public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target, in TaskStack? stack = null)
        {
            List<IPlayable> minions = controller.DeckZone.Where(c => c is Minion).ToList();
            if (minions.Count == 0)
                return TaskState.COMPLETE;

            IPlayable chosen = mode switch
            {
                TutorMode.HighestCost => minions.OrderByDescending(m => m.Cost).First(),
                TutorMode.LowestCost => minions.OrderBy(m => m.Cost).First(),
                _ => minions[game.NextRandomInt(minions.Count)],
            };

            if (!controller.HandZone.IsFull)
            {
                controller.DeckZone.Remove(chosen);
                controller.HandZone.Add(chosen);
            }
            return TaskState.COMPLETE;
        }
    }

    private sealed class SplitRandomDamageTask(int missiles, bool minionsOnly, bool spellDmg) : SimpleTask
    {
        public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target, in TaskStack? stack = null)
        {
            int total = missiles + (spellDmg ? controller.CurrentSpellPower : 0);
            for (int i = 0; i < total; i++)
            {
                List<ICharacter> pool = controller.Opponent.BoardZone
                    .Where(m => m.Health > 0 && !m.IsImmune)
                    .Cast<ICharacter>()
                    .ToList();
                if (!minionsOnly && !controller.Opponent.Hero.IsDead && !controller.Opponent.Hero.IsImmune)
                    pool.Add(controller.Opponent.Hero);

                if (pool.Count == 0)
                    break;

                ICharacter hit = pool[game.NextRandomInt(pool.Count)];
                Generic.DamageCharFunc.Invoke(source as IPlayable, hit, 1, false);
            }
            return TaskState.COMPLETE;
        }
    }

    private sealed class FreezeEntitiesTask(EntityType scope) : SimpleTask
    {
        public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target, in TaskStack? stack = null)
        {
            foreach (IPlayable p in IncludeTask.GetEntities(scope, in controller, source, target, stack?.Playables))
                if (p is ICharacter character)
                    character.IsFrozen = true;
            return TaskState.COMPLETE;
        }
    }

    private sealed class GainFullManaTask(int amount) : SimpleTask
    {
        public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target, in TaskStack? stack = null)
        {
            int maxResources = Math.Max(10, controller[GameTag.MAXRESOURCES]);
            int newRemaining = Math.Min(maxResources, controller.RemainingMana + amount);
            controller.BaseMana = Math.Min(maxResources, controller.BaseMana + amount);
            controller.UsedMana = Math.Max(0, controller.BaseMana - newRemaining);
            return TaskState.COMPLETE;
        }
    }

    private sealed class StatBuffTask(EntityType scope, int addAtk, int addHp, GameTag? bonusKeyword) : SimpleTask
    {
        public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target, in TaskStack? stack = null)
        {
            foreach (IPlayable p in IncludeTask.GetEntities(scope, in controller, source, target, stack?.Playables))
            {
                if (p is not Minion m)
                    continue;
                if (addAtk != 0)
                    m[GameTag.ATK] = m.AttackDamage + addAtk;
                if (addHp != 0)
                    m[GameTag.HEALTH] = m[GameTag.HEALTH] + addHp;
                if (bonusKeyword is GameTag kw)
                {
                    m[kw] = 1;
                    if (kw == GameTag.RUSH && m.IsExhausted && m.NumAttacksThisTurn == 0)
                    {
                        m.IsExhausted = false;
                        m.AttackableByRush = true;
                    }
                }
            }
            return TaskState.COMPLETE;
        }
    }

    [GeneratedRegex(@"<i>.*?</i>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ItalicReminderRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"Spell Damage \+(\d+)\.?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellDamageRegex();

    [GeneratedRegex(@"Start of Game:[^.]+\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StartOfGameSentenceRegex();

    [GeneratedRegex(@"^(?:(?:Taunt|Divine Shield|Rush|Charge|Lifesteal|Windfury|Stealth|Poisonous|Reborn|Elusive|Tradeable)[,.\s]*)+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingKeywordsRegex();

    [GeneratedRegex(@"^Battlecry:\s*(.+?)\s+Deathrattle:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BattlecryThenDeathrattleRegex();

    [GeneratedRegex(@"^Deathrattle:\s*(.+?)\s+Battlecry:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeathrattleThenBattlecryRegex();

    [GeneratedRegex(@"^Battlecry:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BattlecryOnlyRegex();

    [GeneratedRegex(@"^Deathrattle:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeathrattleOnlyRegex();

    [GeneratedRegex(@"^Draw (\d+|a|an|two|three|four|five) cards?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DrawCardsRegex();

    [GeneratedRegex(@"^Gain (\d+) Armor$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GainArmorRegex();

    [GeneratedRegex(@"^Gain (\d+|a|an|two|three) (?:temporary Mana Crystals?|Mana Crystals? this turn only)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GainTempManaRegex();

    [GeneratedRegex(@"^Gain (\d+|a|an|two|three) empty Mana Crystals?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GainEmptyManaRegex();

    [GeneratedRegex(@"^Gain (\d+|a|an|two|three|four|five) Mana Crystals?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GainFullManaRegex();

    [GeneratedRegex(@"^Deal (\d+) damage to an enemy and (\d+) damage to all other enemies$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SwipeDamageRegex();

    [GeneratedRegex(@"^Deal (\d+) damage randomly split among (all enemies|all enemy minions)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SplitDamageRegex();

    [GeneratedRegex(@"^Deal (\d+) damage(?:\s+(to a minion|to an enemy minion|to an undamaged minion|to an enemy|to the enemy hero|to your hero|to all minions|to all other minions|to all enemy minions|to all enemies))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamageScopeRegex();

    [GeneratedRegex(@"^Restore (\d+) Health(?:\s+(to your hero|to all friendly characters))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HealScopeRegex();

    [GeneratedRegex(@"^Give a (friendly )?minion \+(\d+)/\+(\d+)(?:\s+and\s+(Taunt|Divine Shield|Rush|Lifesteal|Windfury|Poisonous))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BuffTargetStatsRegex();

    [GeneratedRegex(@"^Give your minions \+(\d+)/\+(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BuffBoardStatsRegex();
}
