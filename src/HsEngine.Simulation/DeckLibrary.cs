using System.Globalization;
using System.Xml.Linq;
using HsEngine.Ingestion;

namespace HsEngine.Simulation;

public sealed record DeckDefinition(
    string Name,
    string HeroClass,
    IReadOnlyDictionary<string, int> Cards,
    DateTimeOffset? LastEdited = null)
{
    public int TotalCards => Cards.Values.Sum();

    public List<string> Expand()
    {
        var list = new List<string>(TotalCards);
        foreach ((string id, int count) in Cards.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            for (int i = 0; i < count; i++)
                list.Add(id);
        return list;
    }

    /// <summary>Multiset difference <c>Cards \ spentCardIds</c>: the copies still unseen in the deck.</summary>
    public List<string> RemainingAfter(IEnumerable<string>? spentCardIds)
    {
        var remaining = new Dictionary<string, int>(Cards, StringComparer.Ordinal);
        foreach (string id in spentCardIds ?? [])
            if (remaining.TryGetValue(id, out int count) && count > 0)
                remaining[id] = count - 1;

        var list = new List<string>();
        foreach ((string id, int count) in remaining.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            for (int i = 0; i < count; i++)
                list.Add(id);
        return list;
    }
}

/// <summary>
/// Encodes and decodes official Hearthstone Base64 varint deckstrings (format version 1).
/// </summary>
public static class DeckStringCodec
{
    private static readonly Dictionary<string, int> DefaultHeroDbfByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WARRIOR"] = 7, ["SHAMAN"] = 1066, ["ROGUE"] = 930, ["PALADIN"] = 671, ["HUNTER"] = 31,
        ["DRUID"] = 274, ["WARLOCK"] = 893, ["MAGE"] = 637, ["PRIEST"] = 813, ["DEMONHUNTER"] = 56550,
        ["DEATHKNIGHT"] = 78065,
    };

    public static DeckDefinition? TryDecode(string text, string? name = null, IReadOnlyList<CardData>? snapshot = null)
    {
        try
        {
            return Decode(text, name, snapshot);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or EndOfStreamException)
        {
            return null;
        }
    }

    public static DeckDefinition Decode(string text, string? name = null, IReadOnlyList<CardData>? snapshot = null)
    {
        IReadOnlyList<CardData> pool = snapshot ?? CardSnapshot.Default;
        Dictionary<int, CardData> byDbf = pool
            .GroupBy(c => c.DbfId)
            .Where(g => g.Key > 0)
            .ToDictionary(g => g.Key, g => g.First());

        string? commentName = null;
        string? payload = null;
        foreach (string rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("### ", StringComparison.Ordinal))
                commentName = line[4..].Trim();
            else if (!line.StartsWith('#') && line.Length >= 8)
                payload = line;
        }
        payload ??= text.Trim();

        byte[] bytes = Convert.FromBase64String(payload);
        int offset = 0;
        if (bytes.Length < 4 || ReadVarInt(bytes, ref offset) != 0 || ReadVarInt(bytes, ref offset) != 1)
            throw new InvalidDataException("Unsupported deckstring header.");

        _ = ReadVarInt(bytes, ref offset); // format type (1 Wild, 2 Standard, ...)

        int heroCount = ReadVarInt(bytes, ref offset);
        string heroClass = "NEUTRAL";
        for (int i = 0; i < heroCount; i++)
        {
            int heroDbf = ReadVarInt(bytes, ref offset);
            if (byDbf.TryGetValue(heroDbf, out CardData? hero) && !string.IsNullOrEmpty(hero.CardClass))
                heroClass = NormalizeClass(hero.CardClass);
        }

        var cards = new Dictionary<string, int>(StringComparer.Ordinal);
        int singletons = ReadVarInt(bytes, ref offset);
        for (int i = 0; i < singletons; i++)
            AddDbf(cards, byDbf, ReadVarInt(bytes, ref offset), 1);

        int pairs = ReadVarInt(bytes, ref offset);
        for (int i = 0; i < pairs; i++)
            AddDbf(cards, byDbf, ReadVarInt(bytes, ref offset), 2);

        int multis = ReadVarInt(bytes, ref offset);
        for (int i = 0; i < multis; i++)
        {
            int dbf = ReadVarInt(bytes, ref offset);
            int count = ReadVarInt(bytes, ref offset);
            AddDbf(cards, byDbf, dbf, count);
        }

        return new DeckDefinition(name ?? commentName ?? $"{heroClass} Deck", heroClass, cards);
    }

    public static string Encode(DeckDefinition deck, int format = 2, IReadOnlyList<CardData>? snapshot = null)
    {
        IReadOnlyList<CardData> pool = snapshot ?? CardSnapshot.Default;
        Dictionary<string, int> dbfById = pool
            .Where(c => c.DbfId > 0)
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().DbfId, StringComparer.Ordinal);

        int heroDbf = DefaultHeroDbfByClass.GetValueOrDefault(NormalizeClass(deck.HeroClass), 637);
        var singletons = new List<int>();
        var pairs = new List<int>();
        var multis = new List<(int Dbf, int Count)>();

        foreach ((string id, int count) in deck.Cards)
        {
            if (count <= 0 || !dbfById.TryGetValue(id, out int dbf))
                continue;
            if (count == 1) singletons.Add(dbf);
            else if (count == 2) pairs.Add(dbf);
            else multis.Add((dbf, count));
        }

        singletons.Sort();
        pairs.Sort();
        multis.Sort((a, b) => a.Dbf.CompareTo(b.Dbf));

        var bytes = new List<byte> { 0 };
        WriteVarInt(bytes, 1);
        WriteVarInt(bytes, format);
        WriteVarInt(bytes, 1);
        WriteVarInt(bytes, heroDbf);

        WriteVarInt(bytes, singletons.Count);
        foreach (int dbf in singletons) WriteVarInt(bytes, dbf);

        WriteVarInt(bytes, pairs.Count);
        foreach (int dbf in pairs) WriteVarInt(bytes, dbf);

        WriteVarInt(bytes, multis.Count);
        foreach ((int dbf, int count) in multis)
        {
            WriteVarInt(bytes, dbf);
            WriteVarInt(bytes, count);
        }

        return Convert.ToBase64String(bytes.ToArray());
    }

    public static string NormalizeClass(string raw) =>
        raw.Replace(" ", "", StringComparison.Ordinal).Trim().ToUpperInvariant();

    private static void AddDbf(Dictionary<string, int> cards, Dictionary<int, CardData> byDbf, int dbf, int count)
    {
        if (count > 0 && byDbf.TryGetValue(dbf, out CardData? card))
            cards[card.Id] = cards.GetValueOrDefault(card.Id) + count;
    }

    private static int ReadVarInt(byte[] data, ref int offset)
    {
        int result = 0, shift = 0;
        while (offset < data.Length)
        {
            byte b = data[offset++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
            if (shift > 28)
                throw new InvalidDataException("VarInt overflow in deckstring.");
        }
        throw new EndOfStreamException("Unexpected end of deckstring.");
    }

    private static void WriteVarInt(List<byte> output, int value)
    {
        uint u = (uint)value;
        while (u >= 0x80)
        {
            output.Add((byte)(u | 0x80));
            u >>= 7;
        }
        output.Add((byte)u);
    }
}

/// <summary>Reads decks logged by Hearthstone (<c>Decks.log</c>) and saved by Hearthstone Deck Tracker (<c>PlayerDecks.xml</c>).</summary>
public static class LocalDeckSources
{
    public static string DefaultHdtXmlPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "PlayerDecks.xml");

    public static List<DeckDefinition> ReadDecksLog(string logsDirectory, IReadOnlyList<CardData>? snapshot = null)
    {
        if (!Directory.Exists(logsDirectory))
            return [];

        string? logPath = Directory.GetDirectories(logsDirectory, "Hearthstone_*")
            .Order(StringComparer.Ordinal)
            .Select(d => Path.Combine(d, "Decks.log"))
            .LastOrDefault(File.Exists);

        string direct = Path.Combine(logsDirectory, "Decks.log");
        if (logPath is null && File.Exists(direct))
            logPath = direct;

        return logPath is null ? [] : ParseDecksLogLines(File.ReadLines(logPath), snapshot);
    }

    public static List<DeckDefinition> ParseDecksLogLines(IEnumerable<string> lines, IReadOnlyList<CardData>? snapshot = null)
    {
        var decks = new List<DeckDefinition>();
        string? pendingName = null;
        int order = 0;

        foreach (string raw in lines)
        {
            int idx = raw.IndexOf(" - ", StringComparison.Ordinal);
            string text = (idx >= 0 ? raw[(idx + 3)..] : raw).Trim();
            if (text.StartsWith("# ", StringComparison.Ordinal) && !text.StartsWith("# Class:", StringComparison.Ordinal)
                && !text.StartsWith("# Format:", StringComparison.Ordinal) && !text.StartsWith("# Hero:", StringComparison.Ordinal)
                && !text.StartsWith("# 1x", StringComparison.Ordinal) && !text.StartsWith("# 2x", StringComparison.Ordinal))
            {
                pendingName = text.TrimStart('#', ' ');
            }
            else if (!text.StartsWith('#') && text.Length >= 16 && !text.Contains(' ')
                     && DeckStringCodec.TryDecode(text, pendingName, snapshot) is { TotalCards: >= 15 } decoded)
            {
                decks.Add(decoded with { LastEdited = DateTimeOffset.UnixEpoch.AddSeconds(++order) });
                pendingName = null;
            }
        }
        return decks;
    }

    public static List<DeckDefinition> ReadHdtPlayerDecks(string? xmlPath = null)
    {
        string path = xmlPath ?? DefaultHdtXmlPath;
        if (!File.Exists(path))
            return [];

        try
        {
            XDocument doc = XDocument.Load(path);
            var decks = new List<DeckDefinition>();
            foreach (XElement el in doc.Root?.Elements("Deck") ?? [])
            {
                if (BoolChild(el, "Archived") || BoolChild(el, "IsArenaDeck") || BoolChild(el, "IsDungeonDeck"))
                    continue;

                string heroClass = DeckStringCodec.NormalizeClass(el.Element("Class")?.Value ?? "");
                if (heroClass.Length == 0)
                    continue;

                var cards = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (XElement card in el.Element("Cards")?.Elements("Card") ?? [])
                {
                    string? id = card.Element("Id")?.Value;
                    if (!string.IsNullOrEmpty(id)
                        && int.TryParse(card.Element("Count")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                        && count > 0)
                    {
                        cards[id] = cards.GetValueOrDefault(id) + count;
                    }
                }
                if (cards.Count == 0)
                    continue;

                DateTimeOffset? edited = DateTimeOffset.TryParse(el.Element("LastEdited")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset dt)
                    ? dt
                    : null;
                string name = el.Element("Name")?.Value is { Length: > 0 } n ? n : $"{heroClass} HDT";
                decks.Add(new DeckDefinition(name, heroClass, cards, edited));
            }
            return decks;
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            return [];
        }
    }

    private static bool BoolChild(XElement parent, string name) =>
        bool.TryParse(parent.Element(name)?.Value, out bool flag) && flag;
}

/// <summary>Matches the local player's active deck from revealed opening/drawn cards against known local decks.</summary>
public static class DeckMatcher
{
    public static DeckDefinition? MatchLocalDeck(ObservedSide me, IEnumerable<DeckDefinition> candidates)
    {
        string heroClass = DeckStringCodec.NormalizeClass(me.Hero.Class);
        IReadOnlyList<string> known = me.KnownDeckCards
            ?? me.Hand.Where(h => h.CardId is not null && !h.IsCreated).Select(h => h.CardId!).ToList();
        if (known.Count == 0)
            return null;

        var seenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string id in known)
            seenCounts[id] = seenCounts.GetValueOrDefault(id) + 1;

        DeckDefinition? best = null;
        (int Missing, int Matched, DateTimeOffset Edited) bestScore = (int.MaxValue, -1, DateTimeOffset.MinValue);

        foreach (DeckDefinition deck in candidates)
        {
            if (!string.Equals(DeckStringCodec.NormalizeClass(deck.HeroClass), heroClass, StringComparison.OrdinalIgnoreCase))
                continue;

            int matched = 0, missing = 0;
            foreach ((string id, int seen) in seenCounts)
            {
                int inDeck = deck.Cards.GetValueOrDefault(id);
                matched += Math.Min(seen, inDeck);
                missing += Math.Max(0, seen - inDeck);
            }

            // Require at least half of the seen cards to match and tolerate at most 2 edited flex cards.
            if (matched == 0 || missing > 2 || matched * 2 < known.Count)
                continue;

            var score = (missing, matched, deck.LastEdited ?? DateTimeOffset.MinValue);
            if (score.missing < bestScore.Missing
                || (score.missing == bestScore.Missing && score.matched > bestScore.Matched)
                || (score.missing == bestScore.Missing && score.matched == bestScore.Matched && score.Item3 > bestScore.Edited))
            {
                best = deck;
                bestScore = score;
            }
        }
        return best;
    }
}
