using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Simulation;

/// <summary>Creates reproducible full games, used by clone invariants and benchmarks.</summary>
public static class GameFactory
{
    public static Game CreateStarted(long seed) =>
        Start(new GameConfig
        {
            StartPlayer = 1,
            Player1HeroClass = CardClass.MAGE,
            Player2HeroClass = CardClass.WARRIOR,
            FillDecks = true,
            FillDecksPredictably = true,
            Shuffle = true,
            SkipMulligan = true,
            Logging = false,
            History = false,
            RandomSeed = seed,
        });

    /// <summary>Plays <paramref name="actions"/> uniformly random legal actions from a seeded start.</summary>
    public static Game CreateMidgame(long seed, int actions)
    {
        Game game = CreateStarted(seed);
        var rng = new Random(unchecked((int)seed));
        for (int i = 0; i < actions && game.State != State.COMPLETE; i++)
        {
            List<PlayerTask> options = game.CurrentPlayer.Options();
            game.Process(options[rng.Next(options.Count)]);
        }
        return game;
    }

    private static Game Start(GameConfig config)
    {
        var game = new Game(config);
        game.StartGame();
        return game;
    }
}
