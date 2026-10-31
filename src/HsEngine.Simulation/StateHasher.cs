using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsEngine.Simulation;

/// <summary>
/// 64-bit structural state hash; replaces SabberStone's string <c>Game.Hash()</c> in search.
/// Zobrist-style with an implicit random table: every (feature, value) pair is mixed through SplitMix64.
/// Collections are combined with wrapping addition (a multiset hash), so order-free zones need no sorting
/// and duplicate cards do not cancel out the way XOR would make them.
/// Covers what <c>Game.Hash()</c> covers: native tags, effective (aura-adjusted) stats, enchantments,
/// controller auras and every zone except set-aside.
/// </summary>
public static class StateHasher
{
    private const ulong GameSalt = 0x01, PlayerSalt = 0x02, HeroSalt = 0x03, HeroPowerSalt = 0x04, WeaponSalt = 0x05,
        HandSalt = 0x06, BoardSalt = 0x07, DeckSalt = 0x08, GraveyardSalt = 0x09, SecretSalt = 0x0A,
        EnchantmentSalt = 0x0B, AttackSalt = 0x0C, HealthSalt = 0x0D, CostSalt = 0x0E, ControllerAuraSalt = 0x0F;

    // Controller-level aura effects that are not stored as native tags.
    private static readonly GameTag[] ControllerAuraTags =
    [
        GameTag.TIMEOUT, GameTag.SPELLPOWER_DOUBLE, GameTag.HERO_POWER_DOUBLE, GameTag.HEALING_DOES_DAMAGE,
        GameTag.EXTRA_BATTLECRIES_BASE, GameTag.CHOOSE_BOTH, GameTag.SPELLS_COST_HEALTH,
        GameTag.EXTRA_END_TURN_EFFECT, GameTag.HERO_POWER_DISABLED,
    ];

    public static ulong Compute(Game game) =>
        Feature(GameSalt, Tags(game, positional: true))
        + Feature(PlayerSalt, Controller(game.Player1))
        + Feature(PlayerSalt + 1, Controller(game.Player2));

    private static ulong Controller(Controller c)
    {
        ulong h = Tags(c, positional: true);
        foreach (GameTag tag in ControllerAuraTags)
            h += Feature(ControllerAuraSalt, ((ulong)tag << 32) | (uint)c.ControllerAuraEffects[tag]);

        h += Feature(HeroSalt, Entity(c.Hero, positional: true));
        h += Feature(HeroPowerSalt, Entity(c.Hero.HeroPower, positional: true));
        if (c.Hero.Weapon is { } weapon)
            h += Feature(WeaponSalt, Entity(weapon, positional: true));

        h += Feature(HandSalt, Zone(c.HandZone, positional: false));
        h += Feature(BoardSalt, Zone(c.BoardZone, positional: true));
        h += Feature(DeckSalt, Zone(c.DeckZone, positional: false));
        h += Feature(GraveyardSalt, Zone(c.GraveyardZone, positional: false));
        h += Feature(SecretSalt, Zone(c.SecretZone, positional: false));
        return h;
    }

    /// <param name="positional">
    /// Board order matters (adjacency); hand, deck and graveyard order is ignored, as in <c>Game.Hash()</c>.
    /// SabberStone keeps board position as the zone index, not as a ZONE_POSITION tag, so it is mixed in here.
    /// </param>
    private static ulong Zone<T>(IEnumerable<T> zone, bool positional) where T : IPlayable
    {
        ulong sum = 0;
        int index = 0;
        foreach (T entity in zone)
        {
            ulong h = Entity(entity, positional);
            sum += positional ? Feature((ulong)index, h) : h;
            index++;
        }
        return sum;
    }
    private static ulong Entity(IPlayable entity, bool positional)
    {
        ulong h = CardKey(entity.Card) + Tags(entity, positional);
        h += Feature(CostSalt, (uint)entity.Cost);

        if (entity is ICharacter character)
        {
            h += Feature(AttackSalt, (uint)character.AttackDamage);
            h += Feature(HealthSalt, (uint)character.Health);
        }

        if (entity.AppliedEnchantments is { Count: > 0 } enchantments)
            foreach (Enchantment enchantment in enchantments)
                h += Feature(EnchantmentSalt, CardKey(enchantment.Card) + Tags(enchantment, positional));

        return Mix(h);
    }

    private static ulong CardKey(Card card) => Mix((ulong)card.AssetId);

    /// <summary>Effective tag values (the entity indexer adds aura effects to the native value).</summary>
    private static ulong Tags(IEntity entity, bool positional)
    {
        ulong sum = 0;
        foreach (KeyValuePair<GameTag, int> tag in entity.NativeTags)
        {
            if (!positional && tag.Key == GameTag.ZONE_POSITION)
                continue;
            sum += Mix(((ulong)tag.Key << 32) | (uint)entity[tag.Key]);
        }
        return sum;
    }

    private static ulong Feature(ulong salt, ulong value) => Mix(value ^ Mix(salt));

    /// <summary>SplitMix64 finalizer.</summary>
    private static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EB;
        return x ^ (x >> 31);
    }
}
