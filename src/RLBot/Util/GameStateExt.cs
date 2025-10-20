using RLBot.Flat;

namespace RLBot.Util;

static class GameStateExt
{
    public static DesiredGameStateT FillDesiredGameState(
        Dictionary<int, DesiredBallStateT>? balls = null,
        Dictionary<int, DesiredCarStateT>? cars = null,
        DesiredMatchInfoT? matchInfo = null,
        List<ConsoleCommandT>? commands = null
    )
    {
        var gameState = new DesiredGameStateT
        {
            MatchInfo = matchInfo,
            ConsoleCommands = commands ?? new List<ConsoleCommandT>(),
        };

        if (balls != null)
        {
            var maxEntry = balls.Keys.Max();
            gameState.BallStates = Enumerable
                .Range(0, maxEntry + 1)
                .Select(i =>
                    balls.TryGetValue(i, out var ballState)
                        ? ballState
                        : new DesiredBallStateT()
                )
                .ToList();
        }
        else
        {
            gameState.BallStates = new List<DesiredBallStateT>();
        }

        if (cars != null)
        {
            var maxEntry = cars.Keys.Max();
            gameState.CarStates = Enumerable
                .Range(0, maxEntry + 1)
                .Select(i =>
                    cars.TryGetValue(i, out var carState) ? carState : new DesiredCarStateT()
                )
                .ToList();
        }
        else
        {
            gameState.CarStates = new List<DesiredCarStateT>();
        }

        return gameState;
    }
}
