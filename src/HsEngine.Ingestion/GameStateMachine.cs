using System.Globalization;

namespace HsEngine.Ingestion;

/// <summary>An entity as the log describes it: card id ("" while hidden) and raw tag values.</summary>
public sealed class TrackedEntity(int id)
{
    private readonly Dictionary<string, string> _tags = [];

    public int Id { get; } = id;
    public string CardId { get; internal set; } = "";
    public IReadOnlyDictionary<string, string> Tags => _tags;

    public string? this[string tag] => _tags.GetValueOrDefault(tag);

    /// <summary>Integer value of a tag; 0 when absent or not numeric (the game's default for every tag).</summary>
    public int Int(string tag) =>
        _tags.TryGetValue(tag, out string? value) && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) ? n : 0;

    internal void Set(string tag, string value) => _tags[tag] = value;

    internal void Merge(IReadOnlyDictionary<string, string> tags)
    {
        foreach ((string tag, string value) in tags)
            _tags[tag] = value;
    }
}

/// <summary>One player's view of the tracked state. Zones are ordered by ZONE_POSITION.</summary>
public sealed record PlayerState(
    int PlayerId,
    TrackedEntity Entity,
    TrackedEntity Hero,
    int Resources,
    int ResourcesUsed,
    IReadOnlyList<TrackedEntity> Board,
    IReadOnlyList<TrackedEntity> Hand,
    int DeckCount);

/// <summary>
/// Rebuilds the game state from <see cref="PowerEvent"/>s (GameState stream), one event at a time.
/// Name references are resolved through <see cref="PlayerName"/> lines; the opponent is first listed as
/// "UNKNOWN HUMAN PLAYER" and its BattleTag only shows up later in power lines, so the first unknown name is bound
/// to that player. References that still cannot be resolved are counted and skipped, never thrown.
/// </summary>
public sealed class GameStateMachine
{
    private const string UnknownPlayer = "UNKNOWN HUMAN PLAYER";
    private const string GameEntityName = "GameEntity";

    private readonly Dictionary<int, TrackedEntity> _entities = [];
    private readonly Dictionary<int, int> _playerEntities = [];
    private readonly Dictionary<int, string> _playerNames = [];
    private readonly Dictionary<string, string> _info = [];
    private int? _gameEntityId;

    public IReadOnlyDictionary<int, TrackedEntity> Entities => _entities;
    public IReadOnlyDictionary<string, string> Info => _info;
    public int UnresolvedReferences { get; private set; }

    /// <summary>The player whose name the client knows from the start; the opponent starts as "UNKNOWN HUMAN PLAYER".</summary>
    public int? LocalPlayerId { get; private set; }

    public int? OpponentPlayerId => LocalPlayerId is int local ? _playerEntities.Keys.FirstOrDefault(p => p != local) : null;

    public int Turn => _gameEntityId is int id ? _entities[id].Int("TURN") : 0;

    public void Apply(PowerEvent e)
    {
        switch (e)
        {
            case CreateGame:
                Reset();
                break;
            case GameEntityCreated g:
                _gameEntityId = g.Id;
                Create(g.Id).Merge(g.Tags);
                break;
            case PlayerCreated p:
                _playerEntities[p.PlayerId] = p.EntityId;
                Create(p.EntityId).Merge(p.Tags);
                break;
            case FullEntity f:
                TrackedEntity created = Create(f.Id);
                created.CardId = f.CardId;
                created.Merge(f.Tags);
                break;
            case ShowEntity s when Resolve(s.Entity) is { } shown:
                shown.CardId = s.CardId;
                shown.Merge(s.Tags);
                break;
            case ChangeEntity c when Resolve(c.Entity) is { } changed:
                changed.CardId = c.CardId;
                changed.Merge(c.Tags);
                break;
            case HideEntity h when Resolve(h.Entity) is { } hidden:
                hidden.Merge(h.Tags);
                break;
            case TagChange t when Resolve(t.Entity) is { } target:
                target.Set(t.Tag, t.Value);
                break;
            case PlayerName n:
                OnPlayerName(n);
                break;
            case GameInfo info:
                _info[info.Key] = info.Value;
                break;
        }
    }

    public PlayerState Player(int playerId)
    {
        TrackedEntity player = _entities[_playerEntities[playerId]];
        string controller = playerId.ToString(CultureInfo.InvariantCulture);
        List<TrackedEntity> owned = _entities.Values.Where(x => x["CONTROLLER"] == controller).ToList();

        return new PlayerState(
            playerId,
            player,
            _entities[player.Int("HERO_ENTITY")],
            player.Int("RESOURCES"),
            player.Int("RESOURCES_USED"),
            InZone(owned, "PLAY").Where(x => x["CARDTYPE"] == "MINION").ToList(),
            InZone(owned, "HAND").ToList(),
            owned.Count(x => x["ZONE"] == "DECK"));
    }

    private static IEnumerable<TrackedEntity> InZone(IEnumerable<TrackedEntity> owned, string zone) =>
        owned.Where(x => x["ZONE"] == zone).OrderBy(x => x.Int("ZONE_POSITION")).ThenBy(x => x.Id);

    private void OnPlayerName(PlayerName n)
    {
        _playerNames[n.PlayerId] = n.Name;
        if (_playerNames.Count == 2 && _playerNames.Count(p => p.Value == UnknownPlayer) == 1)
            LocalPlayerId = _playerNames.Single(p => p.Value != UnknownPlayer).Key;
    }

    private TrackedEntity? Resolve(EntityRef reference)
    {
        if (reference.Id is int id)
            return _entities.TryGetValue(id, out TrackedEntity? known) ? known : Create(id);

        if (reference.Name == GameEntityName && _gameEntityId is int game)
            return _entities[game];

        int? playerId = PlayerIdByName(reference.Name);
        if (playerId is int p && _playerEntities.TryGetValue(p, out int entityId))
            return _entities[entityId];

        UnresolvedReferences++;
        return null;
    }

    private int? PlayerIdByName(string? name)
    {
        if (name is null)
            return null;
        foreach ((int playerId, string playerName) in _playerNames)
            if (playerName == name)
                return playerId;

        // First sighting of the opponent's BattleTag: bind it to the only player still listed as unknown.
        List<int> unknown = _playerNames.Where(p => p.Value == UnknownPlayer).Select(p => p.Key).ToList();
        if (unknown.Count != 1)
            return null;
        _playerNames[unknown[0]] = name;
        return unknown[0];
    }

    private TrackedEntity Create(int id)
    {
        var entity = new TrackedEntity(id);
        _entities[id] = entity;
        return entity;
    }

    private void Reset()
    {
        _entities.Clear();
        _playerEntities.Clear();
        _playerNames.Clear();
        _info.Clear();
        _gameEntityId = null;
        LocalPlayerId = null;
        UnresolvedReferences = 0;
    }
}
