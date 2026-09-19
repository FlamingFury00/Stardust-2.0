using System.Runtime.CompilerServices;
using RedUtils;
using RLBot.Flat;

internal static class Fixture
{
    [ModuleInitializer]
    internal static void Initialize() => Cars.Initialize(new GamePacketT { Players = new() });
}
