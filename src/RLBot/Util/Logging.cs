using System.Text;
using Microsoft.Extensions.Logging;

namespace RLBot.Util;

public class Logging : ILogger
{
    private const string Grey = "\x1b[38;20m";
    private const string LightBlue = "\x1b[94;20m";
    private const string Yellow = "\x1b[33;20m";
    private const string Green = "\x1b[32;20m";
    private const string Red = "\x1b[31;20m";
    private const string BoldRed = "\x1b[31;1m";
    private const string Reset = "\x1b[0m";

    private readonly string TraceText = "TRACE".PadLeft(8);
    private readonly string DebugText = "DEBUG".PadLeft(8);
    private readonly string InfoText = "INFO".PadLeft(8);
    private readonly string WarningText = "WARNING".PadLeft(8);
    private readonly string ErrorText = "ERROR".PadLeft(8);
    private readonly string CriticalText = "CRITICAL".PadLeft(8);
    private readonly string UnknownText = "UNKNOWN".PadLeft(8);

    private static readonly string[] TraceColors = [Grey, Grey, Grey, Grey];
    private static readonly string[] DebugColors = [Grey, LightBlue, Grey, LightBlue];
    private static readonly string[] InformationColors = [Grey, LightBlue, Grey, LightBlue];
    private static readonly string[] WarningColors = [Yellow, Yellow, Yellow, Yellow];
    private static readonly string[] ErrorColors = [Red, Red, Red, Red];
    private static readonly string[] CriticalColors = [Red, BoldRed, Red, BoldRed];

    private static readonly LogLevel LoggingLevel = LogLevel.Information;

    private readonly string _name;
    private readonly LogLevel _minLevel;
    private static readonly object _lock = new();

    public Logging(string name, LogLevel minLevel)
    {
        _name = name.PadLeft(16);
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (!IsEnabled(logLevel))
            return;

        var logLevelColors = GetLogLevelColors(logLevel);
        var logLevelString = GetLogLevelString(logLevel);
        var message = formatter(state, exception);

        lock (_lock)
        {
            var logBuilder = new StringBuilder();
            logBuilder.Append($"{logLevelColors[0]}{DateTime.Now:HH:mm:ss}{Reset} ");
            logBuilder.Append(
                $"{logLevelColors[1]}{logLevelString}:{Reset}{Green}{_name}{Reset}"
            );
            logBuilder.Append($"{logLevelColors[2]} | {Reset} ");
            logBuilder.Append($"{logLevelColors[3]}{message}{Reset}");

            Console.WriteLine(logBuilder.ToString());
        }
    }

    private string[] GetLogLevelColors(LogLevel logLevel) =>
        logLevel switch
        {
            LogLevel.Trace => TraceColors,
            LogLevel.Debug => DebugColors,
            LogLevel.Information => InformationColors,
            LogLevel.Warning => WarningColors,
            LogLevel.Error => ErrorColors,
            LogLevel.Critical => CriticalColors,
            _ => TraceColors,
        };

    private string GetLogLevelString(LogLevel logLevel) =>
        logLevel switch
        {
            LogLevel.Trace => TraceText,
            LogLevel.Debug => DebugText,
            LogLevel.Information => InfoText,
            LogLevel.Warning => WarningText,
            LogLevel.Error => ErrorText,
            LogLevel.Critical => CriticalText,
            _ => UnknownText,
        };

    public class CustomConsoleLoggerProvider : ILoggerProvider
    {
        private readonly LogLevel _minLevel;

        public CustomConsoleLoggerProvider(LogLevel minLevel)
        {
            _minLevel = minLevel;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new Logging(categoryName, _minLevel);
        }

        public void Dispose() { }
    }

    public static ILogger GetLogger(string loggerName)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(LoggingLevel)
                .AddProvider(new CustomConsoleLoggerProvider(LoggingLevel));
        });

        var logger = loggerFactory.CreateLogger(loggerName);
        return logger;
    }
}
