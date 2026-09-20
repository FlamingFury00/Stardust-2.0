using RedUtils;
using RLBot.Flat;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }

GamePacketT Packet(float time, uint frame)
{
    return new GamePacketT
    {
        MatchInfo = new MatchInfoT
        {
            SecondsElapsed = time,
            FrameNum = frame,
            GameTimeRemaining = 300,
            GameSpeed = 1,
            WorldGravityZ = -650,
            MatchPhase = MatchPhase.Active
        },
        Teams = new List<TeamInfoT>
        {
            new() { Score = 1 },
            new() { Score = 2 }
        }
    };
}

Test("game snapshot: score storage must remain stable across frame updates", () =>
{
    Game.Initialize();
    GamePacketT first = Packet(1, 120);
    Game.Update(first);
    uint[] scores = Game.Scores;
    Game.Update(Packet(1 + 1f / 120, 121));
    Check(ReferenceEquals(scores, Game.Scores), "Game.Update replaced the score array");
    Check(Game.Scores[0] == 1 && Game.Scores[1] == 2, "score values changed");
});

Test("game snapshot: repeated frame updates should not allocate score arrays", () =>
{
    GamePacketT packet = Packet(2, 240);
    for (int i = 0; i < 50; i++) Game.Update(packet);

    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10000; i++)
    {
        packet.MatchInfo.SecondsElapsed += 1f / 120;
        packet.MatchInfo.FrameNum++;
        Game.Update(packet);
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated <= 1024, $"Game.Update allocated {allocated} bytes across 10000 updates");
});

Console.WriteLine($"SNAPSHOT RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;
