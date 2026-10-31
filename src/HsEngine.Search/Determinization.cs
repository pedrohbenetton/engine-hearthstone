using HsEngine.Simulation;
using SabberStoneCore.Model;

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
