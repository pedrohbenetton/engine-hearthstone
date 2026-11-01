using System.Text.Json;

namespace HsEngine.Simulation;

/// <summary>One card of the HearthstoneJSON snapshot (tools/cards/snapshot_cards.py), with the fields the engine uses.</summary>
public sealed record CardData(
    string Id,
    int DbfId,
    string Name,
    string Type,
    string Set,
    int? Cost = null,
    int? Attack = null,
    int? Health = null,
    int? Durability = null,
    string? CardClass = null,
    IReadOnlyList<string>? Mechanics = null,
    bool Collectible = false,
    string? Text = null);

/// <summary>Loads the versioned card snapshot (ADR-0008) copied next to the binaries.</summary>
public static class CardSnapshot
{
    public const int Build = 255790;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<IReadOnlyList<CardData>> DefaultSnapshot = new(() =>
        Load(Path.Combine(AppContext.BaseDirectory, "cards", $"cards.{Build}.json")));

    public static IReadOnlyList<CardData> Default => DefaultSnapshot.Value;

    public static IReadOnlyList<CardData> Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<CardData>>(stream, Options)
               ?? throw new InvalidDataException($"Empty card snapshot: {path}");
    }
}
