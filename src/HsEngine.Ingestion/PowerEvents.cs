namespace HsEngine.Ingestion;

/// <summary>
/// Reference to an entity as Power.log writes it: a bare id, a bracket with id and metadata, or an unresolved
/// name (the game entity, or a player's BattleTag before it is mapped to a player entity).
/// </summary>
public readonly record struct EntityRef(int? Id, string? Name = null, string? Zone = null, string? CardId = null, int? Player = null);

/// <summary>One syntactic event of the GameState power stream. Tag names and values stay as raw text.</summary>
public abstract record PowerEvent;

public sealed record CreateGame : PowerEvent;

public sealed record GameEntityCreated(int Id, IReadOnlyDictionary<string, string> Tags) : PowerEvent;

public sealed record PlayerCreated(int EntityId, int PlayerId, IReadOnlyDictionary<string, string> Tags) : PowerEvent;

public sealed record FullEntity(int Id, string CardId, IReadOnlyDictionary<string, string> Tags) : PowerEvent;

/// <summary>A hidden entity is revealed (drawn by us, played by the opponent, ...).</summary>
public sealed record ShowEntity(EntityRef Entity, string CardId, IReadOnlyDictionary<string, string> Tags) : PowerEvent;

public sealed record HideEntity(EntityRef Entity, IReadOnlyDictionary<string, string> Tags) : PowerEvent;

/// <summary>An entity becomes another card (transform effects).</summary>
public sealed record ChangeEntity(EntityRef Entity, string CardId, IReadOnlyDictionary<string, string> Tags) : PowerEvent;

public sealed record TagChange(EntityRef Entity, string Tag, string Value) : PowerEvent;

public sealed record BlockStart(string BlockType, EntityRef Entity, string EffectCardId, EntityRef? Target) : PowerEvent;

public sealed record BlockEnd : PowerEvent;

/// <summary>A <c>DebugPrintGame</c> key/value line (GameType, FormatType, ...).</summary>
public sealed record GameInfo(string Key, string Value) : PowerEvent;

/// <summary>Maps a player id to the BattleTag that power lines use as an entity reference.</summary>
public sealed record PlayerName(int PlayerId, string Name) : PowerEvent;
