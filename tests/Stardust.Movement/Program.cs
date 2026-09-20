using System.Reflection;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Set(Type type, string property, object instance, object value) =>
    type.GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!
        .SetValue(instance, value);

Test("drive: sideways target must gate boost by yaw, not pitch", () =>
{
    var bot = new ProbeBot();
    Set(typeof(RLBot.Manager.Bot), "Index", bot, 0);
    Set(typeof(RLBot.Manager.Bot), "Team", bot, 0);
    Set(typeof(RUBot), "Renderer", bot, new ExtendedRenderer(((RLBot.Manager.Bot)bot).Renderer));
    ((RLBot.Manager.Bot)bot).Renderer.Begin("movement-regression");
    Cars.AllCars.Clear();
    var car = new Car
    {
        Location = new Vec3(0, 0, 17),
        Velocity = new Vec3(1500, 0, 0),
        Orientation = new Mat3x3(Vec3.Zero),
        IsGrounded = true,
        Boost = 50
    };
    Cars.AllCars.Add(car);
    new Drive(car, new Vec3(0, 3000, 17), 2300, allowDodges: false, wasteBoost: true).Run(bot);
    Check(!bot.Controller.Boost, "boost requested while target is approximately 90 degrees sideways");
    ((RLBot.Manager.Bot)bot).Renderer.End();
});

Test("eta: stationary car must be slower than a car already at throttle cap", () =>
{
    var stationary = new Car
    {
        Location = new Vec3(0, 0, 17),
        Velocity = Vec3.Zero,
        Orientation = new Mat3x3(Vec3.Zero),
        IsGrounded = true,
        Boost = 0
    };
    var fast = new Car(stationary) { Velocity = new Vec3(1400, 0, 0) };
    Vec3 target = new(3000, 0, 17);
    float fromRest = Drive.GetEta(stationary, target, false, false);
    float atSpeed = Drive.GetEta(fast, target, false, false);
    Check(float.IsFinite(fromRest) && float.IsFinite(atSpeed), $"non-finite ETA: rest={fromRest}, fast={atSpeed}");
    Check(fromRest > atSpeed + 0.35f, $"rest ETA {fromRest:F3}s should materially exceed 1400-speed ETA {atSpeed:F3}s");
});

Test("eta: short no-boost launch cannot assume instant 1400 speed", () =>
{
    var car = new Car
    {
        Location = new Vec3(0, 0, 17),
        Velocity = Vec3.Zero,
        Orientation = new Mat3x3(Vec3.Zero),
        IsGrounded = true,
        Boost = 0
    };
    float eta = Drive.GetEta(car, new Vec3(1000, 0, 17), false, false);
    Check(eta > 0.9f, $"1000 uu from rest reported implausible ETA {eta:F3}s");
});

Test("landing: parallel motion near a wall keeps a finite ground landing time", () =>
{
    var car = new Car
    {
        Location = new Vec3(Field.Width / 2 - 46, 0, 500),
        Velocity = new Vec3(0, 900, 0),
        Orientation = new Mat3x3(Vec3.Zero),
        IsGrounded = false
    };
    float time = car.PredictLandingTime();
    Check(float.IsFinite(time) && time > 0 && time < 2, $"parallel-wall landing time was {time}");
});

Console.WriteLine($"MOVEMENT RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class ProbeBot : RUBot
{
    public ProbeBot() : base("stardust-movement-regression") { }
    public override void Run() { }
}
