using Microsoft.Extensions.Logging;
using RLBot.Flat;
using RLBot.GameState;
using RLBot.Util;

namespace RLBot.Manager;

public abstract class Bot
{
    public Logging Logger = new("rlbot", LogLevel.Information);

    public int Team { get; private set; } = -1;
    public int Index { get; private set; } = -1;
    public string Name { get; private set; } = "";
    public int PlayerId { get; private set; } = -1;

    public MatchConfigurationT MatchConfig { get; private set; } = new();
    public FieldInfoT FieldInfo { get; private set; } = new();
    public BallPredictionT BallPrediction { get; private set; } = new();

    public readonly Renderer Renderer;

    private bool _initializedBot = false;
    private bool _hasMatchConfig = false;
    private bool _hasFieldInfo = false;
    private bool _hasPlayerMapping = false;

    private readonly Interface _gameInterface;
    private GamePacketT? _latestPacket;
    private BallPredictionT _latestPrediction = new();

    public Bot(string? defaultAgentId = null)
    {
        string? agentId =
            Environment.GetEnvironmentVariable("RLBOT_AGENT_ID") ?? defaultAgentId;

        if (agentId is null)
        {
            Logger.LogCritical(
                "Environment variable RLBOT_AGENT_ID is not set and no default agent id is passed to "
                    + "the constructor of the bot. If you are starting your bot manually, please set it "
                    + "manually, e.g. `RLBOT_AGENT_ID=<agent_id> dotnet run`"
            );

            throw new Exception(
                "Environment variable RLBOT_AGENT_ID is not set and no default agent id is passed to the constructor of the bot."
            );
        }

        Logger = new Logging("Bot", LogLevel.Information);
        _gameInterface = new Interface(agentId, logger: Logger);
        _gameInterface.OnMatchConfigCallback += HandleMatchConfig;
        _gameInterface.OnFieldInfoCallback += HandleFieldInfo;
        _gameInterface.OnMatchCommunicationCallback += HandleMatchCommunication;
        _gameInterface.OnBallPredictionCallback += HandleBallPrediction;
        _gameInterface.OnControllableTeamInfoCallback += HandleControllableTeamInfo;
        _gameInterface.OnGamePacketCallback += HandleGamePacket;

        Renderer = new Renderer(_gameInterface);
    }

    private void TryInitialize()
    {
        if (_initializedBot || !_hasMatchConfig || !_hasFieldInfo || !_hasPlayerMapping)
            return;

        foreach (PlayerConfigurationT player in MatchConfig.PlayerConfigurations)
        {
            if (player.PlayerId == PlayerId)
            {
                Name = player.Variety.AsCustomBot().Name;
                Logger = new Logging(Name, LogLevel.Information);
                break;
            }
        }

        try
        {
            Initialize();
        }
        catch (Exception e)
        {
            Logger.LogCritical(
                "Bot {0} failed to initialize due the following error: {1}",
                Name,
                e
            );
            throw new Exception("Failed to initialize bot.", e);
        }

        _initializedBot = true;
        _gameInterface.SendInitComplete();
    }

    public virtual void Initialize() { }

    private void HandleMatchConfig(MatchConfigurationT matchConfig)
    {
        MatchConfig = matchConfig;
        _hasMatchConfig = true;
        TryInitialize();
    }

    private void HandleFieldInfo(FieldInfoT fieldInfo)
    {
        FieldInfo = fieldInfo;
        _hasFieldInfo = true;
        TryInitialize();
    }

    private void HandleMatchCommunication(MatchCommT matchComm) =>
        HandleMatchComm(
            (int)matchComm.Index,
            (int)matchComm.Team,
            matchComm.Content,
            matchComm.Display,
            matchComm.TeamOnly
        );

    public virtual void HandleMatchComm(
        int Index,
        int Team,
        List<byte> Content,
        string? Display,
        bool teamOnly
    ) { }

    public void SendMatchComm(
        int Index,
        int Team,
        List<byte> Content,
        string? Display = null,
        bool teamOnly = false
    )
    {
        _gameInterface.SendMatchComm(
            new MatchCommT
            {
                Index = (uint)Index,
                Team = (uint)Team,
                Content = Content,
                Display = Display,
                TeamOnly = teamOnly,
            }
        );
    }

    private void HandleBallPrediction(BallPredictionT ballPrediction) =>
        _latestPrediction = ballPrediction;

    private void HandleControllableTeamInfo(ControllableTeamInfoT controllableTeamInfo)
    {
        Team = (int)controllableTeamInfo.Team;
        var controllable = controllableTeamInfo.Controllables[0];
        Index = (int)controllable.Index;
        PlayerId = controllable.Identifier;
        _hasPlayerMapping = true;

        TryInitialize();
    }

    private void HandleGamePacket(GamePacketT gamePacket) => _latestPacket = gamePacket;

    private void ProcessPacket(GamePacketT packet)
    {
        if (packet.Players.Count <= Index)
        {
            return;
        }

        BallPrediction = _latestPrediction;
        ControllerStateT controller;

        try
        {
            controller = GetOutput(packet);
        }
        catch (Exception e)
        {
            Logger.LogError(
                "Bot {0} encountered an error while processing game packet: {1}",
                Name,
                e
            );
            return;
        }

        var playerInput = new PlayerInputT
        {
            PlayerIndex = (uint)Index,
            ControllerState = controller,
        };
        _gameInterface.SendPlayerInput(playerInput);
    }

    public void Run(bool wantsMatchCommunications = true, bool wantsBallPredictions = true)
    {
        int rlbotServerPort = int.Parse(
            Environment.GetEnvironmentVariable("RLBOT_SERVER_PORT") ?? "23234"
        );

        try
        {
            _gameInterface.Connect(
                wantsMatchCommunications,
                wantsBallPredictions,
                rlbotServerPort: rlbotServerPort
            );

            while (true)
            {
                var res = _gameInterface.HandleIncomingMessages(
                    blocking: _latestPacket is null
                );

                switch (res)
                {
                    case Interface.MsgHandlingResult.Terminated:
                        return;
                    case Interface.MsgHandlingResult.MoreMsgsQueued:
                        continue;
                    case Interface.MsgHandlingResult.NoIncomingMsgs:
                        if (_latestPacket is not null)
                        {
                            ProcessPacket(_latestPacket);
                            _latestPacket = null;
                        }
                        continue;
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogCritical("An error occured while running the bot:\n{0}", e);
            return;
        }
        finally
        {
            Retire();
        }
    }

    public void SetLoadout(SetLoadoutT setLoadout) =>
        _gameInterface.SendSetLoadout(setLoadout);

    public void SetGameState(
        Dictionary<int, DesiredBallStateT>? balls = null,
        Dictionary<int, DesiredCarStateT>? cars = null,
        DesiredMatchInfoT? matchInfo = null,
        List<ConsoleCommandT>? commands = null
    )
    {
        var gameState = GameStateExt.FillDesiredGameState(balls, cars, matchInfo, commands);
        _gameInterface.SendGameState(gameState);
    }

    /// <summary>
    /// Modify the current game state using a builder pattern.
    /// </summary>
    public DesiredGameStateBuilder GameStateBuilder()
    {
        return new DesiredGameStateBuilder(_gameInterface);
    }

    public virtual void Retire() { }

    public abstract ControllerStateT GetOutput(GamePacketT packet);
}
