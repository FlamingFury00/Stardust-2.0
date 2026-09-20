using System.Reflection;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;

Cars.Initialize(new GamePacketT { Players = new() });

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void SetGameTime(float time) =>
    typeof(Game).GetProperty(nameof(Game.Time), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
        .SetValue(null, time);
void Set(Type type, string property, object instance, object value) =>
    type.GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!
        .SetValue(instance, value);
void FieldSet(Type type, string field, object instance, object value) =>
    type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, value);

ProbeBot MakeBot(Car car, float dt)
{
    var bot = new ProbeBot();
    Set(typeof(RLBot.Manager.Bot), "Index", bot, 0);
    Set(typeof(RLBot.Manager.Bot), "Team", bot, 0);
    Set(typeof(RUBot), nameof(RUBot.DeltaTime), bot, dt);
    Cars.AllCars.Clear();
    Cars.AllCars.Add(car);
    return bot;
}

Car GroundCar() => new()
{
    Location = new Vec3(0, 0, 17),
    Velocity = new Vec3(400, 0, 0),
    Orientation = new Mat3x3(Vec3.Zero),
    IsGrounded = true,
    Boost = 50
};

float AerialSecondPress(float dt)
{
    SetGameTime(0);
    Car car = GroundCar();
    var bot = MakeBot(car, dt);
    var slice = new BallSlice(0.8f, new Vec3(850, 0, 650), Vec3.Zero);
    var shot = new AerialShot(car, slice, new Vec3(0, 5120, 300));
    FieldSet(typeof(AerialShot), "_aerialing", shot, true);
    Set(typeof(AerialShot), nameof(AerialShot.DoubleJumping), shot, true);

    bool sawFirst = false, sawRelease = false;
    for (int i = 0; i < 30; i++)
    {
        SetGameTime(i * dt);
        Set(typeof(RUBot), nameof(RUBot.DeltaTime), bot, dt);
        bot.Controller = new ControllerStateT();
        shot.Run(bot);
        if (bot.Controller.Jump) 
        {
            if (sawRelease) return i * dt;
            sawFirst = true;
        }
        else if (sawFirst) sawRelease = true;
        if (i == 0) car.IsGrounded = false;
    }
    return float.PositiveInfinity;
}

(float Time, int Releases) DoubleJumpSecondPress(float dt)
{
    SetGameTime(0);
    Car car = GroundCar();
    var bot = MakeBot(car, dt);
    var slice = new BallSlice(0.8f, new Vec3(850, 0, 420), Vec3.Zero);
    var shot = new DoubleJumpShot(car, slice, new Vec3(0, 5120, 300));
    FieldSet(typeof(DoubleJumpShot), "_jumped", shot, true);

    bool sawFirst = false;
    int releases = 0;
    for (int i = 0; i < 30; i++)
    {
        SetGameTime(i * dt);
        Set(typeof(RUBot), nameof(RUBot.DeltaTime), bot, dt);
        bot.Controller = new ControllerStateT();
        shot.Run(bot);
        if (bot.Controller.Jump)
        {
            if (releases > 0) return (i * dt, releases);
            sawFirst = true;
        }
        else if (sawFirst) releases++;
        if (i == 0) car.IsGrounded = false;
    }
    return (float.PositiveInfinity, releases);
}

float JumpShotDodgeHandoff(float dt)
{
    SetGameTime(0);
    Car car = GroundCar();
    car.IsGrounded = false;
    var bot = MakeBot(car, dt);
    var slice = new BallSlice(0.05f, new Vec3(180, 0, 140), Vec3.Zero);
    var shot = new JumpShot(car, slice, new Vec3(0, 5120, 200));
    FieldSet(typeof(JumpShot), "_jumped", shot, true);
    FieldSet(typeof(JumpShot), "_jumpElapsed", shot, 0.06f);

    bool sawRelease = false;
    for (int i = 0; i < 10; i++)
    {
        SetGameTime(i * dt);
        Set(typeof(RUBot), nameof(RUBot.DeltaTime), bot, dt);
        bot.Controller = new ControllerStateT();
        shot.Run(bot);
        if (!bot.Controller.Jump) sawRelease = true;
        if (bot.Action is Dodge)
        {
            Check(sawRelease, "jump shot handed off to dodge without an observed release");
            return i * dt;
        }
    }
    return float.PositiveInfinity;
}

Test("aerial launch: double jump uses one release edge at every cadence", () =>
{
    foreach (float dt in new[] { 1f / 120, 1f / 60, 1f / 30, 1f / 15 })
    {
        float second = AerialSecondPress(dt);
        float max = Car.JumpMaxDuration + 2.1f * dt;
        Check(float.IsFinite(second) && second <= max,
            $"second aerial jump at {1 / dt:F0} Hz was {second:F4}s; expected <= {max:F4}s");
    }
});

Test("double-jump shot: second jump uses one release edge at every cadence", () =>
{
    foreach (float dt in new[] { 1f / 120, 1f / 60, 1f / 30, 1f / 15 })
    {
        var result = DoubleJumpSecondPress(dt);
        float max = Car.JumpMaxDuration + 2.1f * dt;
        Check(result.Releases == 1,
            $"double-jump shot emitted {result.Releases} release outputs at {1 / dt:F0} Hz; expected exactly 1");
        Check(float.IsFinite(result.Time) && result.Time <= max,
            $"second jump at {1 / dt:F0} Hz was {result.Time:F4}s; expected <= {max:F4}s");
    }
});

Test("jump shot: dodge handoff needs one release edge, not three frames", () =>
{
    foreach (float dt in new[] { 1f / 120, 1f / 60, 1f / 30, 1f / 15 })
    {
        float handoff = JumpShotDodgeHandoff(dt);
        Check(float.IsFinite(handoff) && handoff <= 1.1f * dt,
            $"dodge handoff at {1 / dt:F0} Hz was {handoff:F4}s; expected <= {1.1f * dt:F4}s");
    }
});

Console.WriteLine($"SHOT JUMP EDGE RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class ProbeBot : RUBot
{
    public ProbeBot() : base("stardust-shot-jump-edge-regression") { }
    public override void Run() { }
}
