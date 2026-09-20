using System.Reflection;
using Bot;
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

Test("boost: critically low support may choose a safe full pad beyond 400 uu detour", () =>
{
    var car = new Car
    {
        Location = new Vec3(2200, -2200, 17),
        Velocity = new Vec3(0, -800, 0),
        Orientation = new Mat3x3(new Vec3(0, -MathF.PI / 2, 0)),
        IsGrounded = true,
        Boost = 5
    };
    var full = new Boost(0, new BoostPadT
    {
        Location = new Vector3T { X = 3072, Y = -4096, Z = 73 },
        IsFullBoost = true
    });
    Vec3 ball = new(0, -500, 100);
    Vec3 destination = new(500, -4000, 17);
    float detour = RoutePlanner.Detour(car.Location, full.Location, destination);
    Check(detour > 400, $"fixture detour {detour:F1} did not exceed legacy cap");
    Boost? selected = RoutePlanner.SelectBoost(car, new[] { full }, ball, destination, 0, 4f, (_, _) => 1f);
    Check(ReferenceEquals(selected, full), $"safe full pad was rejected at detour {detour:F1}");
});

Test("boost: pressure still rejects a full-pad excursion", () =>
{
    var car = new Car
    {
        Location = new Vec3(2200, -2200, 17),
        Velocity = new Vec3(0, -800, 0),
        Orientation = new Mat3x3(new Vec3(0, -MathF.PI / 2, 0)),
        IsGrounded = true,
        Boost = 5
    };
    var full = new Boost(0, new BoostPadT
    {
        Location = new Vector3T { X = 3072, Y = -4096, Z = 73 },
        IsFullBoost = true
    });
    Boost? selected = RoutePlanner.SelectBoost(car, new[] { full }, new Vec3(0, -500, 100),
        new Vec3(500, -4000, 17), 0, 1.5f, (_, _) => 1f);
    Check(selected == null, "pressure allowed a risky full-pad excursion");
});

Test("goal return: drive exposes a handbrake safety switch", () =>
{
    System.Reflection.FieldInfo? allow = typeof(Drive).GetField("AllowHandbrake", BindingFlags.Public | BindingFlags.Instance);
    Check(allow != null && allow.FieldType == typeof(bool),
        "generic Drive cannot disable powerslide while parking in the goal mouth");
});

Test("goal return: guard speed brakes as distance collapses", () =>
{
    MethodInfo? method = typeof(Tactics).GetMethod("GuardSpeed", BindingFlags.Public | BindingFlags.Static);
    Check(method != null, "no braking-aware defensive guard speed exists");
    var car = new Car
    {
        Location = new Vec3(0, -4200, 17),
        Velocity = new Vec3(0, -1500, 0),
        Orientation = new Mat3x3(new Vec3(0, -MathF.PI / 2, 0)),
        IsGrounded = true
    };
    float far = (float)method!.Invoke(null, new object[] { car, new Vec3(0, -4700, 17), 1800f })!;
    float near = (float)method.Invoke(null, new object[] { car, new Vec3(0, -4300, 17), 1800f })!;
    Check(near < far && near <= 900, $"near guard speed {near:F1} did not brake below far speed {far:F1}");
});

Test("goal return: deep-net car is routed out through the mouth before parking", () =>
{
    MethodInfo? method = typeof(Tactics).GetMethod("GoalReturnTarget", BindingFlags.Public | BindingFlags.Static);
    Check(method != null, "no explicit deep-net exit waypoint exists");
    var car = new Car
    {
        Location = new Vec3(500, -5550, 17),
        Velocity = new Vec3(0, 300, 0),
        Orientation = new Mat3x3(new Vec3(0, MathF.PI / 2, 0)),
        IsGrounded = true
    };
    Vec3 target = (Vec3)method!.Invoke(null, new object[] { car, new Vec3(-600, -4400, 17), new Vec3(0, -5120, 0) })!;
    Check(target.y > -5120 && target.y < -4500, $"deep-net waypoint did not exit goal: {target}");
    Check(MathF.Abs(target.x) < Goal.Width / 2 - 100, $"exit waypoint is too close to a post: {target}");
});

Console.WriteLine($"BOOST/GOAL RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;
