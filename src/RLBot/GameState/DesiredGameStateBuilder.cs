using RLBot.Flat;

namespace RLBot.GameState;

public struct DesiredGameStateBuilder(Interface @interface)
{
    private readonly DesiredGameStateT _state = new()
    {
        BallStates = new(),
        CarStates = new(),
        ConsoleCommands = new(),
    };

    /// <summary>
    /// Modify the desired match info.
    /// </summary>
    public DesiredGameStateBuilder MatchInfo(Func<DesiredMatchInfoBuilder, DesiredMatchInfoBuilder> build)
    {
        var builder = new DesiredMatchInfoBuilder(_state.MatchInfo ?? new DesiredMatchInfoT());
        build(builder);
        return this;
    }

    /// <summary>
    /// Modify the desired car state at the given index.
    /// </summary>
    public DesiredGameStateBuilder Car(int index, Func<DesiredCarStateBuilder, DesiredCarStateBuilder> build)
    {
        while (_state.CarStates.Count <= index)
        {
            _state.CarStates.Add(new());
        }

        var builder = new DesiredCarStateBuilder(_state.CarStates[index]);
        build(builder);
        return this;
    }

    /// <summary>
    /// Modify the desired cars at the given indices.
    /// </summary>
    public DesiredGameStateBuilder Cars(IEnumerable<int> indices,
        Func<int, DesiredCarStateBuilder, DesiredCarStateBuilder> build)
    {
        foreach (var index in indices)
        {
            while (_state.CarStates.Count <= index)
            {
                _state.CarStates.Add(new());
            }

            var builder = new DesiredCarStateBuilder(_state.CarStates[index]);
            build(index, builder);
        }
        return this;
    }
    
    /// <summary>
    /// Modify the desired ball state at the given index.
    /// </summary>
    public DesiredGameStateBuilder Ball(int index, Func<DesiredBallStateBuilder, DesiredBallStateBuilder> build)
    {
        while (_state.BallStates.Count <= index)
        {
            _state.BallStates.Add(new());
        }

        var builder = new DesiredBallStateBuilder(_state.BallStates[index]);
        build(builder);
        return this;
    }

    /// <summary>
    /// Modify the desired balls at the given indices. 
    /// </summary>
    public DesiredGameStateBuilder Balls(IEnumerable<int> indices,
        Func<int, DesiredBallStateBuilder, DesiredBallStateBuilder> build)
    {
        foreach (var index in indices)
        {
            while (_state.BallStates.Count <= index)
            {
                _state.BallStates.Add(new());
            }

            var builder = new DesiredBallStateBuilder(_state.BallStates[index]);
            build(index, builder);
        }
        return this;
    }

    /// <summary>
    /// Add a console command.
    /// </summary>
    public DesiredGameStateBuilder Command(string cmd)
    {
        _state.ConsoleCommands.Add(new() { Command = cmd });
        return this;
    }

    public void BuildAndSend()
    {
        @interface.SendGameState(_state);
    }
}
