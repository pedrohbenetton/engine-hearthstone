using System.Runtime.CompilerServices;
using HsEngine.Simulation;

namespace HsEngine.Simulation.Tests;

internal static class AssemblySetup
{
    // Proxy registration writes SabberStone's process-wide card table; xUnit runs test classes in parallel, so it
    // must happen before any test reads that table.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RegisterProxyCards() => ProxyCards.EnsureRegistered();
}
