using System.Reflection;
using RedUtils;
using RedUtils.Math;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }
bool Finite(Vec3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

Test("target: stationary ball produces finite goal aim", () =>
{
    var target = new Target(new Goal(1));
    Vec3 aim = target.Clamp(new Ball(new Vec3(0, 0, 100), Vec3.Zero));
    Check(Finite(aim), $"stationary clamp returned {aim}");
    Check(MathF.Abs(aim.x) < Goal.Width / 2 && aim.y > 5000 && aim.z >= 0 && aim.z <= Goal.Height,
        $"stationary clamp left goal aperture: {aim}");
});

Test("target: velocity away from goal does not pull aim to a post", () =>
{
    var target = new Target(new Goal(1));
    var ball = new Ball(new Vec3(0, 0, 120), new Vec3(1500, -500, 0));
    Vec3 aim = target.Clamp(ball);
    Check(Finite(aim), $"away-moving clamp returned {aim}");
    Check(MathF.Abs(aim.x) < 250, $"irrelevant backward extrapolation pulled aim sideways: {aim}");
});

Test("target: near-post corrected limits remain finite", () =>
{
    var goal = new Goal(1);
    var target = new Target(goal);
    Vec3 ball = goal.TopLeftCorner + new Vec3(5, -50, -5);
    target.GetCorrectedLimits(ball, out Vec3 left, out Vec3 right, out Vec3 top, out Vec3 bottom);
    Check(Finite(left) && Finite(right) && Finite(top) && Finite(bottom),
        $"near-post geometry became non-finite: {left} {right} {top} {bottom}");
});

Test("aerial: zero-correction high-speed coast is feasible below max speed", () =>
{
    var car = new Car
    {
        Location = new Vec3(0, 0, 500),
        Velocity = new Vec3(2100, 0, 0),
        Orientation = new Mat3x3(Vec3.Zero),
        IsGrounded = false,
        Boost = 20
    };
    const float horizon = 0.25f;
    var slice = new BallSlice(Game.Time + horizon, new Vec3(1000, 0, 600), Vec3.Zero);
    var shot = new AerialShot(car, slice, new Vec3(4000, 0, 600));
    typeof(AerialShot).GetProperty(nameof(AerialShot.TargetLocation))!.SetValue(shot, car.PredictLocation(horizon));
    typeof(AerialShot).GetProperty(nameof(AerialShot.Slice))!.SetValue(shot,
        new BallSlice(Game.Time + horizon, car.PredictLocation(horizon) + new Vec3(155, 0, 0), Vec3.Zero));
    var estimate = typeof(AerialShot).GetMethod("GetBoostEstimate", BindingFlags.NonPublic | BindingFlags.Instance)!;
    float boost = (float)estimate.Invoke(shot, new object[] { car, false })!;
    Check(boost >= 0, $"coasting at {car.Velocity.Length():F0} uu/s below max was rejected");
});

Test("target: attacking goal aperture keeps post clearance beyond ball radius", () =>
{
    var target = new Target(new Goal(1));
    float halfWidth = target.TargetSurface.Size.x / 2;
    float allowed = Goal.Width / 2 - Ball.Radius - 60;
    Check(halfWidth <= allowed + 0.01f,
        $"attacking half-aperture {halfWidth:F1} leaves less than 60 uu post safety margin");
});

Test("target: attacking goal aperture keeps crossbar clearance without lifting floor", () =>
{
    var target = new Target(new Goal(1));
    float upper = target.TargetSurface.Location.z + target.TargetSurface.Size.y / 2;
    float lower = target.TargetSurface.Location.z - target.TargetSurface.Size.y / 2;
    Check(upper <= Goal.Height - Ball.Radius - 60 + 0.01f,
        $"upper target {upper:F1} leaves less than 60 uu crossbar safety margin");
    Check(lower <= Ball.Radius + 1,
        $"lower target {lower:F1} would unnecessarily remove rolling shots");
});

Console.WriteLine($"SHOOTING RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;
