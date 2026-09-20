using System.Reflection;
using Bot;
using RedUtils;
using RedUtils.Math;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }
MethodInfo Method(Type type, string name) =>
    type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
    ?? throw new Exception($"missing production contract {type.Name}.{name}");

Test("defense: imminent opponent contact near own goal is critical before predicted shot", () =>
{
    MethodInfo method = Method(typeof(Tactics), "CriticalDefense");
    var ball = new Ball(new Vec3(0, -4050, 110), new Vec3(0, -100, 0));
    bool critical = (bool)method.Invoke(null, new object[] { ball, new Vec3(0, -5120, 0), 0.55f })!;
    Check(critical, "near-goal opponent contact was not promoted to critical defense");
});

Test("defense: identical pressure at midfield is not a goal emergency", () =>
{
    MethodInfo method = Method(typeof(Tactics), "CriticalDefense");
    var ball = new Ball(new Vec3(0, 0, 110), Vec3.Zero);
    bool critical = (bool)method.Invoke(null, new object[] { ball, new Vec3(0, -5120, 0), 0.55f })!;
    Check(!critical, "midfield pressure incorrectly became goal emergency");
});

Test("defense: emergency support target stays in the goal-saving corridor", () =>
{
    MethodInfo method = Method(typeof(Tactics), "EmergencyGuardTarget");
    Vec3 guard = (Vec3)method.Invoke(null, new object[] {
        new Vec3(700, -4100, 100), new Vec3(0, -5120, 0), 1, 3 })!;
    Check(guard.y < -4550 && guard.y > -5050, $"guard remained too far from goal: {guard}");
    Check(MathF.Abs(guard.x) < Goal.Width / 2 - 120, $"guard is too close to a post: {guard}");
});

Test("clear: emergency clear target is strongly away from own goal", () =>
{
    MethodInfo method = Method(typeof(Tactics), "DefensiveClearPoint");
    Vec3 ball = new(250, -4450, 120), goal = new(0, -5120, 0);
    Vec3 target = (Vec3)method.Invoke(null, new object[] { ball, goal })!;
    Vec3 away = ControlMath.FlatUnit(ball - goal, Vec3.Y);
    Vec3 direction = ControlMath.FlatUnit(target - ball, away);
    Check(direction.Dot(away) >= 0.92f, $"clear direction is not safely away from goal: dot={direction.Dot(away):F3}, target={target}");
});

Test("clear: own-goal-pointing contact direction is rejected", () =>
{
    MethodInfo method = Method(typeof(Tactics), "ClearDirectionSafe");
    Vec3 ball = new(0, -4400, 100), goal = new(0, -5120, 0);
    bool safe = (bool)method.Invoke(null, new object[] { new Vec3(0, -1, 0), ball, goal })!;
    Check(!safe, "a direction toward our goal was accepted as a defensive clear");
});

Test("clear: shallow diagonal escape is rejected inside danger area", () =>
{
    MethodInfo method = Method(typeof(Tactics), "ClearDirectionSafe");
    Vec3 ball = new(0, -4400, 100), goal = new(0, -5120, 0);
    Vec3 away = ControlMath.FlatUnit(ball - goal, Vec3.Y);
    Vec3 diagonal = ControlMath.FlatUnit(away + Vec3.X * 0.58f, away);
    float dot = diagonal.Dot(away);
    Check(dot > 0.85f && dot < 0.90f, $"fixture angle is not in intended shallow-clear band: {dot:F3}");
    bool safe = (bool)method.Invoke(null, new object[] { diagonal, ball, goal })!;
    Check(!safe, $"shallow diagonal clear with away-dot {dot:F3} was accepted");
});

Test("possession setup: dangerous-side car routes around ball instead of through it", () =>
{
    MethodInfo method = Method(typeof(Tactics), "SafeApproachTarget");
    var car = new Car { Location = new Vec3(0, -3600, 17), Orientation = new Mat3x3(new Vec3(0, -MathF.PI / 2, 0)), IsGrounded = true };
    Vec3 ball = new(0, -4300, 100), ownGoal = new(0, -5120, 0), theirGoal = new(0, 5120, 0);
    Vec3 target = (Vec3)method.Invoke(null, new object[] { car, ball, ownGoal, theirGoal })!;
    Vec3 goalward = ControlMath.FlatUnit(ownGoal - ball, new Vec3(0, -1, 0));
    Check(MathF.Abs(target.x - ball.x) >= 450, $"unsafe-side setup did not route laterally around ball: {target}");
    Check(ControlMath.FlatUnit(target - ball, goalward).Dot(goalward) > 0.15f,
        $"setup waypoint is not goal-side of ball: {target}");
});

Test("aerial carry: defensive-third touch under pressure is not selected", () =>
{
    MethodInfo method = Method(typeof(AerialCarry), "SafeToStart");
    var car = new Car
    {
        Location = new Vec3(0, -4500, 500),
        Velocity = new Vec3(0, 800, 0),
        Orientation = new Mat3x3(new Vec3(0, MathF.PI / 2, 0)),
        IsGrounded = false,
        Boost = 50
    };
    var ball = new Ball(new Vec3(0, -4250, 660), new Vec3(0, 820, 0));
    bool safe = (bool)method.Invoke(null, new object[] { car, ball, new Vec3(0, -5120, 0), new Vec3(0, 5120, 0), 1.0f })!;
    Check(!safe, "risky own-third aerial carry was selected");
});

Test("aerial carry: velocity-matched offensive setup remains eligible", () =>
{
    MethodInfo method = Method(typeof(AerialCarry), "SafeToStart");
    var car = new Car
    {
        Location = new Vec3(0, 900, 500),
        Velocity = new Vec3(0, 900, 20),
        Orientation = new Mat3x3(new Vec3(0, MathF.PI / 2, 0)),
        IsGrounded = false,
        Boost = 50
    };
    var ball = new Ball(new Vec3(0, 1150, 660), new Vec3(0, 920, 10));
    bool safe = (bool)method.Invoke(null, new object[] { car, ball, new Vec3(0, -5120, 0), new Vec3(0, 5120, 0), 1.5f })!;
    Check(safe, "controlled offensive aerial setup was rejected");
});

Test("aerial carry: large separation and relative speed is not controlled possession", () =>
{
    MethodInfo method = Method(typeof(AerialCarry), "HasControl");
    var car = new Car
    {
        Location = new Vec3(0, 1000, 550),
        Velocity = new Vec3(0, 700, 0),
        Orientation = new Mat3x3(new Vec3(0, MathF.PI / 2, 0)),
        IsGrounded = false
    };
    var ball = new Ball(new Vec3(500, 1450, 800), new Vec3(0, 1600, 200));
    bool controlled = (bool)method.Invoke(null, new object[] { car, ball, new Vec3(0, 5120, 0) })!;
    Check(!controlled, "separated high-relative-speed ball was treated as an air dribble");
});

Console.WriteLine($"DEFENSE SAFETY RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;
