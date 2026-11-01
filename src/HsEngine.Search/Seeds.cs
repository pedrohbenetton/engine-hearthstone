namespace HsEngine.Search;

/// <summary>
/// Derives child seeds reproducibly across processes. <see cref="HashCode.Combine{T1,T2}"/> is randomised per
/// process, which made searches and arena games differ between runs with the same seed.
/// </summary>
public static class Seeds
{
    public static int Derive(long parent, long child) => unchecked((int)Mix(Mix((ulong)parent) ^ (ulong)child));

    /// <summary>SplitMix64 finalizer.</summary>
    private static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EB;
        return x ^ (x >> 31);
    }
}
