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
void Near(float actual, float expected, float tolerance)
{
    Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance,
        $"expected {expected}, got {actual}");
}
void Set(Type type, string property, object instance, object value) =>
    type.GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!
        .SetValue(instance, value);

Test("drive: ground boost alignment uses yaw rather than pitch", () =>
{
    Near(Drive.GroundHeadingError(0, MathF.PI / 2), MathF.PI / 2, 0.0001f);
    Near(Drive.GroundHeadingError(0.4f, 0.05f), 0.05f, 0.0001f);
});

Test("drive: close straight path stays finite and approximately straight", () =>
{
    var car = new Car
    {
        Location = new Vec3(0, 0, 17),
        Velocity = Vec3.Zero,
        Orientation = new Mat3x3(Vec3.Zero),
        IsGrounded = true,
        Boost = 0
    };
    float distance = Drive.GetDistance(car, new Vec3(100, 0, 17), false);
    Check(float.IsFinite(distance), $"straight distance was {distance}");
    Check(MathF.Abs(distance - 100) < 5, $"100 uu straight path estimated as {distance:F3} uu");
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
