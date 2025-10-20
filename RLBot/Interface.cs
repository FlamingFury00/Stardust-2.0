using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Google.FlatBuffers;
using Microsoft.Extensions.Logging;
using RLBot.Flat;
using RLBot.Util;

public class Interface
{
    public const int RLBOT_SERVER_PORT = 23234;

    public bool IsConnected { get; private set; } = false;
    private bool _running = false;

    private readonly int _connectionTimeout;
    private readonly Logging _logger;
    private readonly TcpClient _client = new();
    private SpecStreamReader? _socketSpecReader;
    private SpecStreamWriter? _socketSpecWriter;

    public readonly string AgentId;
    public event Action OnConnectCallback = delegate { };
    public event Action<GamePacketT> OnGamePacketCallback = delegate { };
    public event Action<FieldInfoT> OnFieldInfoCallback = delegate { };
    public event Action<MatchConfigurationT> OnMatchConfigCallback = delegate { };
    public event Action<MatchCommT> OnMatchCommunicationCallback = delegate { };
    public event Action<BallPredictionT> OnBallPredictionCallback = delegate { };
    public event Action<ControllableTeamInfoT> OnControllableTeamInfoCallback = delegate { };
    public event Action<RenderingStatusT> OnRenderingStatusCallback = delegate { };
    public event Action<CorePacketT> OnAnyMessageCallback = delegate { };

    public Interface(string agentId, int connectionTimeout = 120, Logging? logger = null)
    {
        AgentId = agentId;
        _connectionTimeout = connectionTimeout;

        if (logger is null)
        {
            _logger = new Logging("Interface", LogLevel.Information);
        }
        else
        {
            _logger = logger;
        }

        _client.NoDelay = true;
    }

    public void SendFlatBuffer(InterfaceMessageUnion message)
    {
        if (!IsConnected)
        {
            throw new Exception("Connection has not been established");
        }
        _socketSpecWriter!.Write(message);
        _socketSpecWriter.Send();
    }

    public void SendInitComplete()
    {
        SendFlatBuffer(InterfaceMessageUnion.FromInitComplete(new InitCompleteT()));
    }

    public void SendSetLoadout(SetLoadoutT setLoadout)
    {
        SendFlatBuffer(InterfaceMessageUnion.FromSetLoadout(setLoadout));
    }

    public void SendMatchComm(MatchCommT matchComm)
    {
        SendFlatBuffer(InterfaceMessageUnion.FromMatchComm(matchComm));
    }

    public void SendPlayerInput(PlayerInputT playerInput)
    {
        SendFlatBuffer(InterfaceMessageUnion.FromPlayerInput(playerInput));
    }

    public void SendGameState(DesiredGameStateT gameState)
    {
        SendFlatBuffer(InterfaceMessageUnion.FromDesiredGameState(gameState));
    }

    public void SendRenderGroup(RenderGroupT renderGroup)
    {
        SendFlatBuffer(InterfaceMessageUnion.FromRenderGroup(renderGroup));
    }

    public void SendRemoveRenderGroup(RemoveRenderGroupT removeRenderGroup)
    {
        SendFlatBuffer(InterfaceMessageUnion.FromRemoveRenderGroup(removeRenderGroup));
    }

    public void StopMatch(bool shutdownServer = false)
    {
        var stopCommand = new StopCommandT { ShutdownServer = shutdownServer };
        SendFlatBuffer(InterfaceMessageUnion.FromStopCommand(stopCommand));
    }

    public void StartMatch(MatchConfigurationT matchConfig)
    {
        SendFlatBuffer(InterfaceMessageUnion.FromMatchConfiguration(matchConfig));
    }

    public void StartMatch(string matchConfigPath)
    {
        matchConfigPath = Path.GetFullPath(matchConfigPath);
        if (!Path.Exists(matchConfigPath))
            throw new FileNotFoundException($"File does not exist: {matchConfigPath}");
        var attr = File.GetAttributes(matchConfigPath);
        if (attr.HasFlag(FileAttributes.Directory))
            throw new ArgumentException(
                $"Expected path to file, but it is a directory: {matchConfigPath}"
            );

        var startCommand = new StartCommandT { ConfigPath = matchConfigPath };
        SendFlatBuffer(InterfaceMessageUnion.FromStartCommand(startCommand));
    }

    public void Connect(
        bool wantsMatchCommunications,
        bool wantsBallPredictions,
        bool closeBetweenMatches = true,
        int rlbotServerPort = RLBOT_SERVER_PORT
    )
    {
        if (IsConnected)
        {
            throw new Exception("Connection has already been established");
        }

        _client.ReceiveTimeout = _connectionTimeout * 1000;

        try
        {
            var beginTime = DateTime.Now;
            var nextWarning = 10;
            while (DateTime.Now < beginTime.AddSeconds(_connectionTimeout))
            {
                try
                {
                    _client.Connect(new IPAddress([127, 0, 0, 1]), rlbotServerPort);
                    IsConnected = true;
                    break;
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.ConnectionRefused)
                    {
                        Thread.Sleep(100);
                    }
                    else if (e.SocketErrorCode == SocketError.ConnectionAborted)
                    {
                        Thread.Sleep(100);
                    }

                    if (DateTime.Now > beginTime.AddSeconds(nextWarning))
                    {
                        nextWarning *= 2;
                        _logger.LogWarning(
                            "Connection is being refused/aborted. Trying again ..."
                        );
                    }
                }
            }

            if (!IsConnected)
            {
                throw new SocketException(
                    (int)SocketError.ConnectionRefused,
                    "Connection was refused/aborted repeatedly! "
                        + "Ensure that Rocket League and the RLBotServer is running. "
                        + "Try calling `ensure_server_started()` before connecting."
                );
            }
        }
        catch (TimeoutException e)
        {
            throw new TimeoutException(
                "Took too long to connect to the RLBot! "
                    + "Ensure that Rocket League and the RLBotServer is running."
                    + "Try calling `ensure_server_started()` before connecting.",
                e
            );
        }
        finally
        {
            _client.ReceiveTimeout = 0;
        }

        _socketSpecReader = new SpecStreamReader(_client.GetStream());
        _socketSpecWriter = new SpecStreamWriter(_client.GetStream());

        IPEndPoint? localIpEndPoint = _client.Client.LocalEndPoint as IPEndPoint;
        _logger.LogInformation(
            "Connected to port {0} from port {1}!",
            rlbotServerPort,
            localIpEndPoint!.Port
        );

        OnConnectCallback();

        var connectionSettings = new ConnectionSettingsT
        {
            AgentId = AgentId,
            WantsBallPredictions = wantsBallPredictions,
            WantsComms = wantsMatchCommunications,
            CloseBetweenMatches = closeBetweenMatches,
        };
        
        SendFlatBuffer(InterfaceMessageUnion.FromConnectionSettings(connectionSettings));
    }

    public void Run(bool backgroundThread = false)
    {
        if (!IsConnected)
        {
            throw new Exception("Connection has not been established");
        }

        if (_running)
        {
            throw new Exception("Message handling is already running");
        }

        if (backgroundThread)
        {
            new Thread(() => Run()).Start();
        }
        else
        {
            _running = true;
            while (_running && IsConnected)
                _running =
                    HandleIncomingMessages(blocking: true) != MsgHandlingResult.Terminated;

            _running = false;
        }
    }

    public enum MsgHandlingResult
    {
        Terminated,
        NoIncomingMsgs,
        MoreMsgsQueued,
    }

    public MsgHandlingResult HandleIncomingMessages(bool blocking = false)
    {
        if (!IsConnected)
        {
            throw new Exception("Connection has not been established");
        }

        try
        {
            _client.Client.Blocking = blocking;

            CorePacketT packet = _socketSpecReader!.ReadOne().UnPack();

            try
            {
                return HandleIncomingMessage(packet)
                    ? MsgHandlingResult.MoreMsgsQueued
                    : MsgHandlingResult.Terminated;
            }
            catch (Exception e)
            {
                _logger.LogError(
                    "Unexpected error while handling message of type {0}: {1}",
                    packet.Message.Type,
                    e
                );
                return MsgHandlingResult.Terminated;
            }
        }
        catch (Exception e)
        {
            if (e.InnerException is SocketException)
            {
                return MsgHandlingResult.NoIncomingMsgs;
            }
            _logger.LogError("SocketRelay disconnected unexpectedly! {}", e);
            return MsgHandlingResult.Terminated;
        }
    }

    private bool HandleIncomingMessage(CorePacketT packet)
    {
        OnAnyMessageCallback(packet);

        switch (packet.Message.Type)
        {
            case CoreMessage.NONE:
            case CoreMessage.DisconnectSignal:
                return false;
            case CoreMessage.GamePacket:
                GamePacketT gamePacket = packet.Message.AsGamePacket();
                OnGamePacketCallback(gamePacket);
                break;
            case CoreMessage.FieldInfo:
                FieldInfoT fieldInfo = packet.Message.AsFieldInfo();
                OnFieldInfoCallback(fieldInfo);
                break;
            case CoreMessage.MatchConfiguration:
                MatchConfigurationT matchSettings = packet.Message.AsMatchConfiguration();
                OnMatchConfigCallback(matchSettings);
                break;
            case CoreMessage.MatchComm:
                MatchCommT matchComm = packet.Message.AsMatchComm();
                OnMatchCommunicationCallback(matchComm);
                break;
            case CoreMessage.BallPrediction:
                BallPredictionT ballPrediction = packet.Message.AsBallPrediction();
                OnBallPredictionCallback(ballPrediction);
                break;
            case CoreMessage.ControllableTeamInfo:
                ControllableTeamInfoT controllableTeamInfo = packet.Message.AsControllableTeamInfo();
                OnControllableTeamInfoCallback(controllableTeamInfo);
                break;
            case CoreMessage.RenderingStatus:
                RenderingStatusT renderingStatus = packet.Message.AsRenderingStatus();
                OnRenderingStatusCallback(renderingStatus);
                break;
            default:
                _logger.LogWarning("Received message of unknown type: {0}", packet.Message.Type);
                break;
        }

        return true;
    }

    public void Disconnect()
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Asked to disconnect but was already disconnected.");
            return;
        }

        _socketSpecWriter!.Write(InterfaceMessageUnion.FromDisconnectSignal(new DisconnectSignalT()));
        _socketSpecWriter.Send();

        var timeout = 5.0;
        while (_running && timeout > 0)
        {
            Thread.Sleep(100);
            timeout -= 0.1;
        }

        if (timeout <= 0)
        {
            _logger.LogCritical("RLBot is not responding to our disconnect request!?");
            _running = false;
        }

        Debug.Assert(
            !_running,
            "Disconnect request or timeout should have set _running to False"
        );

        IsConnected = false;
    }
}
