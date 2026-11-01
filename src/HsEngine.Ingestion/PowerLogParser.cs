using System.Globalization;
using System.Text.RegularExpressions;

namespace HsEngine.Ingestion;

/// <summary>
/// Incremental tokenizer for Hearthstone's Power.log (ADR-0005: passive reading only). Consumes one line at a
/// time, so it works both on a finished file and on a live tail. Only <c>GameState.DebugPrintPower</c> and
/// <c>GameState.DebugPrintGame</c> lines are used; <c>PowerTaskList</c> repeats the same stream with UI timing.
/// Entities that carry indented <c>tag=</c> lines are emitted once their tag block ends (next non-tag line or
/// <see cref="Flush"/>).
/// </summary>
public sealed partial class PowerLogParser
{
    private PendingEntity? _pending;

    public static IEnumerable<PowerEvent> Parse(IEnumerable<string> lines)
    {
        var parser = new PowerLogParser();
        foreach (string line in lines)
            foreach (PowerEvent e in parser.Feed(line))
                yield return e;
        foreach (PowerEvent e in parser.Flush())
            yield return e;
    }

    public List<PowerEvent> Feed(string line)
    {
        var events = new List<PowerEvent>(2);
        Match m = LineRegex().Match(line);
        if (!m.Success || m.Groups["src"].Value != "GameState")
            return events;

        string body = m.Groups["body"].Value.Trim();
        switch (m.Groups["method"].Value)
        {
            case "DebugPrintPower":
                if (_pending is not null && TagRegex().Match(body) is { Success: true } tag)
                {
                    _pending.Tags[tag.Groups["tag"].Value] = tag.Groups["value"].Value;
                    return events;
                }
                EmitPending(events);
                ParsePower(body, events);
                break;

            case "DebugPrintGame":
                EmitPending(events);
                ParseGame(body, events);
                break;
        }
        return events;
    }

    public List<PowerEvent> Flush()
    {
        var events = new List<PowerEvent>(1);
        EmitPending(events);
        return events;
    }

    private void ParsePower(string body, List<PowerEvent> events)
    {
        Match m;
        if (body.StartsWith("TAG_CHANGE ", StringComparison.Ordinal) && (m = TagChangeRegex().Match(body)).Success)
            events.Add(new TagChange(Ref(m.Groups["entity"].Value), m.Groups["tag"].Value, m.Groups["value"].Value));
        else if (body.StartsWith("BLOCK_START ", StringComparison.Ordinal) && (m = BlockStartRegex().Match(body)).Success)
            events.Add(new BlockStart(m.Groups["type"].Value, Ref(m.Groups["entity"].Value), m.Groups["effect"].Value,
                m.Groups["target"].Value is "0" or "" ? null : Ref(m.Groups["target"].Value)));
        else if (body.StartsWith("BLOCK_END", StringComparison.Ordinal))
            events.Add(new BlockEnd());
        else if ((m = FullEntityRegex().Match(body)).Success)
            _pending = new PendingEntity(tags => new FullEntity(Int(m.Groups["id"].Value), m.Groups["card"].Value, tags));
        else if ((m = ShowEntityRegex().Match(body)).Success)
        {
            (string kind, EntityRef entity, string card) = (m.Groups["kind"].Value, Ref(m.Groups["entity"].Value), m.Groups["card"].Value);
            _pending = new PendingEntity(tags => kind == "SHOW_ENTITY"
                ? new ShowEntity(entity, card, tags)
                : new ChangeEntity(entity, card, tags));
        }
        else if ((m = HideEntityRegex().Match(body)).Success)
            events.Add(new HideEntity(Ref(m.Groups["entity"].Value),
                new Dictionary<string, string> { [m.Groups["tag"].Value] = m.Groups["value"].Value }));
        else if ((m = GameEntityRegex().Match(body)).Success)
            _pending = new PendingEntity(tags => new GameEntityCreated(Int(m.Groups["id"].Value), tags));
        else if ((m = PlayerRegex().Match(body)).Success)
            _pending = new PendingEntity(tags => new PlayerCreated(Int(m.Groups["id"].Value), Int(m.Groups["player"].Value), tags));
        else if (body == "CREATE_GAME")
            events.Add(new CreateGame());
    }

    private static void ParseGame(string body, List<PowerEvent> events)
    {
        Match m = PlayerNameRegex().Match(body);
        if (m.Success)
        {
            events.Add(new PlayerName(Int(m.Groups["player"].Value), m.Groups["name"].Value.Trim()));
            return;
        }
        int eq = body.IndexOf('=');
        if (eq > 0)
            events.Add(new GameInfo(body[..eq], body[(eq + 1)..].Trim()));
    }

    private void EmitPending(List<PowerEvent> events)
    {
        if (_pending is null)
            return;
        events.Add(_pending.Build(_pending.Tags));
        _pending = null;
    }

    private static EntityRef Ref(string text)
    {
        if (BracketRegex().Match(text) is { Success: true } b)
            return new EntityRef(Int(b.Groups["id"].Value), b.Groups["name"].Value, b.Groups["zone"].Value,
                b.Groups["card"].Value.Length > 0 ? b.Groups["card"].Value : null, Int(b.Groups["player"].Value));
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int id)
            ? new EntityRef(id)
            : new EntityRef(null, text);
    }

    private static int Int(string text) => int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private sealed class PendingEntity(Func<IReadOnlyDictionary<string, string>, PowerEvent> build)
    {
        public Dictionary<string, string> Tags { get; } = [];
        public Func<IReadOnlyDictionary<string, string>, PowerEvent> Build { get; } = build;
    }

    // Bracket, bare id, or a name; names are localised and may contain spaces and commas ("Bob, o Bartender"),
    // so the lazy match is anchored by the field that follows in each pattern.
    private const string EntityPattern = @"\[.*?\]|.+?";

    [GeneratedRegex(@"^D [\d:.]+ (?<src>\w+)\.(?<method>\w+)\(\) - (?<body>.*)$")]
    private static partial Regex LineRegex();

    [GeneratedRegex(@"^tag=(?<tag>\S+) value=(?<value>\S*)$")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"^TAG_CHANGE Entity=(?<entity>" + EntityPattern + @") tag=(?<tag>\S+) value=(?<value>\S*)\s*$")]
    private static partial Regex TagChangeRegex();

    [GeneratedRegex(@"^BLOCK_START BlockType=(?<type>\w+) Entity=(?<entity>" + EntityPattern + @") EffectCardId=(?<effect>.*?) EffectIndex=-?\d+ Target=(?<target>" + EntityPattern + @")(?: SubOption=|\s*$)")]
    private static partial Regex BlockStartRegex();

    [GeneratedRegex(@"^FULL_ENTITY - Creating ID=(?<id>\d+) CardID=(?<card>\S*)")]
    private static partial Regex FullEntityRegex();

    [GeneratedRegex(@"^(?<kind>SHOW_ENTITY|CHANGE_ENTITY) - Updating Entity=(?<entity>" + EntityPattern + @") CardID=(?<card>\S*)")]
    private static partial Regex ShowEntityRegex();

    [GeneratedRegex(@"^HIDE_ENTITY - Entity=(?<entity>" + EntityPattern + @") tag=(?<tag>\S+) value=(?<value>\S*)")]
    private static partial Regex HideEntityRegex();

    [GeneratedRegex(@"^GameEntity EntityID=(?<id>\d+)")]
    private static partial Regex GameEntityRegex();

    [GeneratedRegex(@"^Player EntityID=(?<id>\d+) PlayerID=(?<player>\d+)")]
    private static partial Regex PlayerRegex();

    [GeneratedRegex(@"^PlayerID=(?<player>\d+), PlayerName=(?<name>.+)$")]
    private static partial Regex PlayerNameRegex();

    [GeneratedRegex(@"^\[entityName=(?<name>.*?) id=(?<id>\d+) zone=(?<zone>\w+) zonePos=\d+ cardId=(?<card>\S*) player=(?<player>\d+)\]$")]
    private static partial Regex BracketRegex();
}
