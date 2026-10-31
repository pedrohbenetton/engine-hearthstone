using System.Text.RegularExpressions;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search;

/// <summary>
/// ADR-0004 (reviewer adjustment): actions that reveal hidden information — drawing or discovering cards —
/// are expanded before the rest, so lines that depend on a draw's outcome are not fixed ahead of the draw.
/// Slice 0 classifies by card text; the pool is small and English-only. Replace with effect metadata when the
/// card pool grows.
/// </summary>
public static partial class InformationActions
{
    public static bool RevealsInformation(Card card) => card.Text is { } text && RevealPattern().IsMatch(text);

    public static bool RevealsInformation(PlayerTask task) =>
        task.PlayerTaskType is PlayerTaskType.PLAY_CARD or PlayerTaskType.HERO_POWER &&
        task.Source is { } source && RevealsInformation(source.Card);

    [GeneratedRegex(@"\b(draw|discover)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RevealPattern();
}
