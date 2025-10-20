using System.Runtime.InteropServices;

namespace RLBot.Util;

public static class OsConstants
{
    public static string MainExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "RLBotServer.exe"
            : "RLBotServer";
}
