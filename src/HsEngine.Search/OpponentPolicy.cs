using HsEngine.Evaluation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search;

/// <summary>Plays the side to move until its turn ends or the game is over (ADR-0004: light opponent response).</summary>
public interface IOpponentPolicy
{
    void PlayTurn(Game game, Random rng);
}

/// <summary>
/// One-ply greedy: repeatedly takes the action whose resulting position the evaluator scores best for the mover,
/// ending the turn when nothing beats passing. Ties are broken at random.
/// </summary>
public sealed class GreedyOpponentPolicy(BoardEvaluator evaluator, int maxActions = 30) : IOpponentPolicy
{
    public void PlayTurn(Game game, Random rng)
    {
        Controller mover = game.CurrentPlayer;
        for (int step = 0; step < maxActions && game.State != State.COMPLETE && game.CurrentPlayer == mover; step++)
        {
            List<PlayerTask> options = mover.Options();
            int best = -1;
            double bestScore = double.NegativeInfinity;
            int ties = 0;

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].PlayerTaskType == PlayerTaskType.CONCEDE || !BoardPlacement.IsCanonicalOption(options[i]))
                    continue;

                Game child = game.Clone(logging: false, resetRandomSeed: false, history: false);
                if (!child.Process(child.CurrentPlayer.Options()[i]))
                    continue;

                double score = evaluator.Evaluate(child, child.Player1.PlayerId == mover.PlayerId ? child.Player1 : child.Player2);
                if (score > bestScore)
                {
                    (best, bestScore, ties) = (i, score, 1);
                }
                else if (score == bestScore && rng.Next(++ties) == 0)
                {
                    best = i;
                }
            }

            if (best < 0 || options[best].PlayerTaskType == PlayerTaskType.END_TURN || !game.Process(options[best]))
                break;
        }

        if (game.State != State.COMPLETE && game.CurrentPlayer == mover)
            game.Process(EndTurnTask.Any(mover));
    }
}
