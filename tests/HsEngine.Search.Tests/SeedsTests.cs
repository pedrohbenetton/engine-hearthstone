using HsEngine.Search;

namespace HsEngine.Search.Tests;

public class SeedsTests
{
    /// <summary>Pinned values: a per-process randomised hash would pass an equality test but fail these.</summary>
    [Theory]
    [InlineData(1, 2, -1770846613)]
    [InlineData(1000, 0, -362012033)]
    [InlineData(-5, 7, -1073068936)]
    public void Derive_is_stable_across_processes(long parent, long child, int expected) =>
        Assert.Equal(expected, Seeds.Derive(parent, child));
}
