using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsEngine.Search;

/// <summary>Samples one complete world consistent with what the searching player has observed.</summary>
public interface IDeterminizer
{
    Game Sample(Game observed, Random rng);
}

/// <summary>
/// Slice 0 determinizer: samples only chance (random effects, draw order through the RNG), because the opponent's
/// deck list is known and the scenario state already holds concrete cards. Hidden-hand resampling from the range
/// estimate (ADR-0006) replaces this in Slice 2.
/// </summary>
public sealed class ChanceDeterminizer : IDeterminizer
{
    public Game Sample(Game observed, Random rng) => observed.CloneWithSeed(rng.NextInt64());
}

/// <summary>
/// Slice 0 hidden-information determinizer for full games against a known deck list (ARCH-0001 §2.2):
/// the searching player's own deck order is reshuffled, and the opponent's unseen cards (hand ∪ deck) are
/// redistributed uniformly, keeping hand and deck sizes. Cards the searcher has seen stay put: generated cards
/// (CREATOR set) and The Coin. Uniform redistribution ignores mulligan and hand-retention signals, which the
/// Bayesian range model (ADR-0006, Slice 2) adds.
/// </summary>
public sealed class HiddenInformationDeterminizer : IDeterminizer
{
    private const string TheCoin = "GAME_005";

    public Game Sample(Game observed, Random rng)
    {
        Game world = observed.CloneWithSeed(rng.NextInt64());
        Controller me = world.CurrentPlayer;
        Controller opponent = world.CurrentOpponent;

        me.DeckZone.Shuffle();

        List<IPlayable> hiddenInHand = opponent.HandZone.Where(IsHidden).ToList();
        List<IPlayable> pool = [.. hiddenInHand, .. opponent.DeckZone];
        foreach (IPlayable card in hiddenInHand)
            opponent.HandZone.Remove(card);
        foreach (IPlayable card in opponent.DeckZone.ToList())
            opponent.DeckZone.Remove(card);

        rng.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pool));
        for (int i = 0; i < pool.Count; i++)
        {
            if (i < hiddenInHand.Count)
                opponent.HandZone.Add(pool[i]);
            else
                opponent.DeckZone.Add(pool[i]);
        }

        return world;
    }

    private static bool IsHidden(IPlayable card) => card.Card.Id != TheCoin && card[GameTag.CREATOR] == 0;
}
