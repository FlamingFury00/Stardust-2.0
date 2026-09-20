using Bot;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;

// Analytic attitude plant, not RocketSim or Rocket League. No ball-contact model is claimed.
// Empirical torque/damping equations: https://www.smish.dev/rocket_league/aerial_control/
int passed = 0;
float worstError = 0;
void Check(bool success, string message)
{
    if (!success) throw new InvalidOperationException(message);
    passed++;
}
Vec3 Rotate(Vec3 value, Vec3 axis, float angle)
{
    float cosine = MathF.Cos(angle), sine = MathF.Sin(angle);
    return value * cosine + axis.Cross(value) * sine + axis * (axis.Dot(value) * (1 - cosine));
}
void Scenario(Vec3 rotation, float dt)
{
    var car = new Car { Orientation = new Mat3x3(rotation) };
    int steps = (int)MathF.Ceiling(3f / dt);
    for (int step = 0; step < steps; step++)
    {
        car.LocalAngularVelocity = car.Local(car.AngularVelocity);
        var controls = new ControllerStateT();
        ControlMath.Aim(car, controls, new Vec3(1, 0, 0), Vec3.Up);
        Vec3 local = car.LocalAngularVelocity;
        Vec3 acceleration = new(
            -36.0796f * controls.Roll - 4.4717f * local.x,
            -12.146f * controls.Pitch - 2.7982f * (1 - MathF.Abs(controls.Pitch)) * local.y,
            8.9196f * controls.Yaw - 1.8865f * (1 - MathF.Abs(controls.Yaw)) * local.z);
        Vec3 worldAcceleration = car.Forward * acceleration.x + car.Right * acceleration.y + car.Up * acceleration.z;
        Vec3 nextRate = (car.AngularVelocity + worldAcceleration * dt).Cap(0, 5.5f);
        Vec3 averageRate = (car.AngularVelocity + nextRate) * 0.5f;
        float angle = averageRate.Length() * dt;
        if (angle > 0.0000001f)
        {
            Vec3 axis = averageRate.Normalize();
            car.Forward = Rotate(car.Forward, axis, angle);
            car.Right = Rotate(car.Right, axis, angle);
            car.Up = Rotate(car.Up, axis, angle);
        }
        car.AngularVelocity = nextRate;
    }
    float error = ControlMath.RotationError(car, new Vec3(1, 0, 0), Vec3.Up).Length();
    worstError = MathF.Max(worstError, error);
    Check(float.IsFinite(error) && error < 0.04f && car.AngularVelocity.Length() < 0.15f,
        $"attitude did not converge: rotation={rotation}, dt={dt}, error={error}, rate={car.AngularVelocity.Length()}");
}

Vec3[] rotations = { Vec3.Zero, new(0, 0, MathF.PI), new(0, MathF.PI, 0), new(MathF.PI, 0, 0),
    new(0.5f, 2, -1.4f), new(-1.2f, -2.8f, 2.5f), new(1.4f, 0, 0), new(0, -2, 2) };
foreach (float dt in new[] { 1f / 120, 1f / 60, 1f / 30 })
    foreach (Vec3 rotation in rotations) Scenario(rotation, dt);
var rng = new Random(4096);
for (int i = 0; i < 16; i++) Scenario(new Vec3((float)rng.NextDouble() * 3 - 1.5f,
    (float)rng.NextDouble() * 6 - 3, (float)rng.NextDouble() * 6 - 3), 1f / 120);
Console.WriteLine(FormattableString.Invariant($"ATTITUDE: {passed} analytic scenarios passed; worst final error={worstError:F6} rad."));

var me = new Car { Location = new Vec3(0, -1000, 17), Boost = 10, IsGrounded = true };
var smallPad = new Boost(0); // active default small pad at the origin
var pads = new[] { smallPad };
Vec3 ball = new(0, 1000, 100), destination = new(0, 0, 17);
Check(ReferenceEquals(RoutePlanner.SelectBoost(me, pads, ball, destination, 0, 2, (_, _) => 0.5f), smallPad),
    "small on-route boost must be considered");
Check(RoutePlanner.SelectBoost(me, pads, ball, destination, 0, 0.4f, (_, _) => 0.5f) == null,
    "opponent pressure must reject the detour");
Check(RoutePlanner.SelectBoost(me, pads, ball, destination, 0, 2, (_, _) => float.NaN) == null,
    "nonfinite travel time must be rejected");
Check(RoutePlanner.SelectBoost(me, pads, new Vec3(0, -2000, 100), destination, 0, 2, (_, _) => 0.5f) == null,
    "boost route must remain goal-side");
Check(RoutePlanner.Detour(new Vec3(0, 0, 0), new Vec3(2000, 0, 0), new Vec3(0, 1000, 0)) > 400,
    "off-route pad should exceed the detour budget");
Console.WriteLine($"MODEL RESULT: {passed} checks passed. These checks do not establish in-game match strength.");
