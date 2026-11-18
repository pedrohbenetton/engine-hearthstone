using System.Text.Json;

namespace HsEngine.Simulation;

/// <summary>Card names in Portuguese (pt-BR) from the snapshot of the same build (ADR-0009).</summary>
public static class CardNames
{
    private static readonly Lazy<Dictionary<string, string>> PtBRNames = new(() =>
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "cards", $"names.ptBR.{CardSnapshot.Build}.json"));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    });

    public static string? PtBR(string cardId) => PtBRNames.Value.GetValueOrDefault(cardId);
}
