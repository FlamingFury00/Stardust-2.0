using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RLBot.Flat;
using RLBot.GameState;
using RLBot.Util;

namespace RLBot.Manager;

public class Match
{
    private readonly Logging _logger = new Logging("Match", LogLevel.Information);

    public GamePacketT? Packet { get; private set; }
    private Process? _rlbotServerProcess = null;
    private int _rlbotServerPort = Interface.RLBOT_SERVER_PORT;
    private bool _initialized = false;

    private string? _mainExecutablePath;
    private string _mainExecutableName = OsConstants.MainExecutableName;

    private Interface _gameInterface;

    public Match(
        string? mainExecutablePath = null,
        string? mainExecutableName = null
    )
    {
        _mainExecutablePath = mainExecutablePath;
        if (mainExecutableName != null)
            _mainExecutableName = mainExecutableName;

        _gameInterface = new Interface("", logger: _logger);
        _gameInterface.OnGamePacketCallback += PacketReporter;
    }

    public void EnsureServerStarted()
    {
        // self.rlbot_server_process, self.rlbot_server_port = gateway.find_server_process(
        //     self.main_executable_name
        // )

        if (_rlbotServerProcess != null)
        {
            _logger.LogInformation("Already have {0} running!", _mainExecutableName);
            return;
        }

        if (_mainExecutablePath == null)
            _mainExecutablePath = Directory.GetCurrentDirectory();

        // rlbot_server_process, self.rlbot_server_port = gateway.launch(
        //     self.main_executable_path,
        //     self.main_executable_name,
        // )
        // self.rlbot_server_process = psutil.Process(rlbot_server_process.pid)

        // self.logger.info(
        //     "Started %s with process id %s",
        //     self.main_executable_name,
        //     self.rlbot_server_process.pid,
        // )
    }

    private void PacketReporter(GamePacketT packet) => Packet = packet;

    public void Connect(
        bool wantsMatchCommunications,
        bool wantsBallPredictions,
        bool closeAfterMatch = true,
        int rlbotServerPort = Interface.RLBOT_SERVER_PORT
    )
    {
        _gameInterface.Connect(
            wantsMatchCommunications,
            wantsBallPredictions,
            closeAfterMatch,
            rlbotServerPort
        );

        _rlbotServerPort = rlbotServerPort;
    }

    public void WaitForFirstPacket()
    {
        while (
            Packet == null
            || Packet.MatchInfo.MatchPhase == MatchPhase.Inactive
            || Packet.MatchInfo.MatchPhase == MatchPhase.Ended
        )
            Thread.Sleep(100);
    }

    public void StartMatch(MatchConfigurationT config, bool waitForStart = true)
    {
        EnsureGameConnection();

        _gameInterface.StartMatch(config);

        if (!_initialized)
        {
            _gameInterface.SendInitComplete();
            _initialized = true;
        }

        if (waitForStart)
        {
            WaitForFirstPacket();
            _logger.LogInformation("Match has started.");
        }
    }

    public void StartMatch(string configPath, bool waitForStart = true)
    {
        EnsureGameConnection();

        _gameInterface.StartMatch(configPath);

        if (!_initialized)
        {
            _gameInterface.SendInitComplete();
            _initialized = true;
        }

        if (waitForStart)
        {
            WaitForFirstPacket();
            _logger.LogInformation("Match has started.");
        }
    }

    private void EnsureGameConnection()
    {
        if (!_gameInterface.IsConnected)
        {
            _gameInterface.Connect(
                wantsMatchCommunications: false,
                wantsBallPredictions: false,
                closeBetweenMatches: false,
                rlbotServerPort: _rlbotServerPort
            );
            _gameInterface.Run(backgroundThread: true);
        }
    }

    /// <summary>
    /// Modify the current game state using a builder pattern.
    /// </summary>
    public DesiredGameStateBuilder GameStateBuilder()
    {
        return new DesiredGameStateBuilder(_gameInterface);
    }
    
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

    public void Disconnect() => _gameInterface.Disconnect();

    public void StopMatch() => _gameInterface.StopMatch();
}
