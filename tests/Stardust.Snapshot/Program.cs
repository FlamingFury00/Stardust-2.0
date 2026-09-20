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

Test("shared gate: three bot instances refresh one world snapshot per frame", () =>
{
    var gate = new SharedWorldFrameGate();
    int refreshes = 0;
    for (uint frame = 100; frame < 220; frame++)
    {
        float time = frame / 120f;
        for (int bot = 0; bot < 3; bot++)
        {
            SharedWorldDecision decision = gate.Step(frame, time, 6);
            if (decision.Refresh) refreshes++;
            if (bot == 0)
                Check(decision.Refresh, $"first bot did not refresh frame {frame}");
            else
                Check(!decision.Refresh, $"bot {bot} duplicated frame {frame}");
        }
    }
    Check(refreshes == 120, $"expected 120 shared refreshes, got {refreshes}");
});

Test("shared gate: discontinuities and roster changes reinitialize exactly once", () =>
{
    var gate = new SharedWorldFrameGate();
    SharedWorldDecision first = gate.Step(500, 10, 4);
    Check(first.Refresh && first.Reinitialize, "first frame did not initialize");
    Check(!gate.Step(500, 10, 4).Refresh, "duplicate first frame refreshed");

    SharedWorldDecision next = gate.Step(501, 10 + 1f / 120, 4);
    Check(next.Refresh && !next.Reinitialize, "normal next frame reinitialized");

    SharedWorldDecision roster = gate.Step(501, 10 + 1f / 120, 5);
    Check(roster.Refresh && roster.Reinitialize, "roster change did not reinitialize");
    Check(!gate.Step(501, 10 + 1f / 120, 5).Refresh, "roster duplicate refreshed twice");

    SharedWorldDecision rewind = gate.Step(12, 0.1f, 5);
    Check(rewind.Refresh && rewind.Reinitialize, "game-time rewind did not reinitialize");

    SharedWorldDecision gap = gate.Step(1000, 3f, 5);
    Check(gap.Refresh && gap.Reinitialize, "large packet discontinuity did not reinitialize");
});

Test("car snapshot: unchanged touch record keeps object identity", () =>
{
    PlayerInfoT player = new()
    {
        Physics = new PhysicsT
        {
            Location = new Vector3T(),
            Rotation = new RotatorT(),
            Velocity = new Vector3T(),
            AngularVelocity = new Vector3T()
        },
        ScoreInfo = new ScoreInfoT(),
        Hitbox = new BoxShapeT { Length = 118, Width = 84, Height = 36 },
        HitboxOffset = new Vector3T(),
        LatestTouch = new TouchT
        {
            GameSeconds = 4.25f,
            Location = new Vector3T { X = 10, Y = 20, Z = 30 },
            Normal = new Vector3T { X = 0, Y = 0, Z = 1 },
            BallIndex = 0
        },
        AirState = AirState.OnGround,
        Name = "fixture",
        Team = 0,
        Boost = 50
    };

    var car = new Car();
    car.Update(player);
    BallTouch first = car.LatestTouch;
    car.Update(player);
    Check(ReferenceEquals(first, car.LatestTouch), "identical packet touch allocated a replacement BallTouch");

    player.LatestTouch.Location.X = 11;
    car.Update(player);
    Check(!ReferenceEquals(first, car.LatestTouch), "changed same-timestamp touch was not refreshed");
});

Console.WriteLine($"SNAPSHOT RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;
