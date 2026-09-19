using System.Diagnostics;
using System.Reflection;
using Bot;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;
using BallPrediction = RedUtils.BallPrediction;

// Synthetic production-code scenarios and analytic plants. NO contact physics or live Rocket League.
int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e}"); }
}
void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
void Near(float a, float b, float tolerance = 0.001f) => Check(float.IsFinite(a) && MathF.Abs(a - b) <= tolerance, $"{a} != {b}");
void Set(Type type, string property, object owner, object value) =>
    type.GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!.SetValue(owner, value);
Car CarAt(int index, int team, Vec3 position, Vec3 velocity = default, float boost = 40, float yaw = MathF.PI / 2) =>
    new() { Index = index, Team = (uint)team, Location = position, Velocity = velocity, Boost = boost,
        IsGrounded = position.z < 50, Orientation = new Mat3x3(new Vec3(0, yaw, 0)) };
void World(float time, Ball ball, params Car[] cars)
{
    Set(typeof(Game), "Time", null, time);
    Set(typeof(Game), "MatchPhase", null, MatchPhase.Active);
    Set(typeof(Game), "Gravity", null, new Vec3(0, 0, -650));
    Cars.Initialize(new GamePacketT { Players = new() });
    Cars.AllCars.AddRange(cars);
    Field.Initialize(new FieldInfoT { BoostPads = new() });
    Set(typeof(Ball), "Location", null, ball.location);
    Set(typeof(Ball), "Velocity", null, ball.velocity);
    Set(typeof(Ball), "Prediction", null, new BallPrediction { Slices = Enumerable.Range(0, 721)
        .Select(i => new BallSlice(time + i / 120f, ball.location + ball.velocity * (i / 120f), ball.velocity)).ToArray() });
}
Stardust BotAt(int index, int team)
{
    var bot = new Stardust("stardust-robustness");
    Set(typeof(RLBot.Manager.Bot), "Index", bot, index); Set(typeof(RLBot.Manager.Bot), "Team", bot, team);
    Set(typeof(RUBot), "Renderer", bot, new ExtendedRenderer(((RLBot.Manager.Bot)bot).Renderer));
    Set(typeof(RUBot), "DeltaTime", bot, 1f / 120);
    Set(typeof(RUBot), "Jump", bot, new JumpState(new PlayerInfoT { AirState = AirState.OnGround }));
    ((RLBot.Manager.Bot)bot).Renderer.Begin("test");
    return bot;
}
float SegmentDistance(Vec3 a, Vec3 b, Vec3 point)
{
    Vec3 d = (b - a).Flatten(); float squared = d.Dot(d);
    float u = squared > 0 ? System.Math.Clamp((point - a).Dot(d) / squared, 0, 1) : 0;
    return point.FlatDist(a + d * u);
}

Test("ETA: rest cannot beat the maximum throttle acceleration bound", () =>
{
    var car = CarAt(0, 0, new Vec3(0, 0, 17), boost: 0, yaw: 0);
    float eta = Drive.GetEta(car, new Vec3(1000, 0, 0), false, false);
    Check(eta >= MathF.Sqrt(2 * 1000 / 1600f), $"unphysical ETA {eta}");
    Check(eta < 2, $"excessively conservative straight ETA {eta}");
});
Test("ETA: straight paths do not lose a phantom turn radius", () =>
{
    var car = CarAt(0, 0, new Vec3(0, 0, 17), yaw: 0);
    Near(Drive.GetDistance(car, new Vec3(1000, 0, 0), false), 1000, 0.1f);
});
Test("ETA: fuel and initial velocity change arrival in the correct direction", () =>
{
    Check(DrivePhysics.TravelTime(2500, 0, 50) < DrivePhysics.TravelTime(2500, 0, 0));
    Check(DrivePhysics.TravelTime(2500, 1200, 0) < DrivePhysics.TravelTime(2500, 0, 0));
    Check(DrivePhysics.TravelTime(2500, -800, 0) > DrivePhysics.TravelTime(2500, 0, 0));
    Near(DrivePhysics.TravelTime(2300, 2300, 100), 1, 0.001f);
});
Test("ETA: invalid values do not win a race", () =>
{
    Check(float.IsPositiveInfinity(DrivePhysics.TravelTime(100, float.NaN, 100)));
    Check(float.IsPositiveInfinity(DrivePhysics.TravelTime(float.PositiveInfinity, 0, 0)));
    Near(DrivePhysics.TravelTime(0, 0, 0), 0);
});
Test("roles: all input permutations elect the same first man and cover", () =>
{
    var cars = new[] { CarAt(0, 0, new Vec3(0, -300, 17)), CarAt(1, 0, new Vec3(1000, -2000, 17)), CarAt(2, 0, new Vec3(-500, -4000, 17)) };
    float[] eta = { 1.12f, 1.06f, 1.00f };
    int[] reference = null;
    foreach (int a in Enumerable.Range(0, 3)) foreach (int b in Enumerable.Range(0, 3).Where(i => i != a))
    {
        int c = 3 - a - b;
        TeamAssignment assignment = TeamShape.Assign(new[] { cars[a], cars[b], cars[c] }, 0, new Vec3(0, 100, 100), car => eta[car.Index]);
        int[] result = { assignment.FirstMan, assignment.Cover, assignment.LastBack };
        reference ??= result;
        Check(reference.SequenceEqual(result));
        Check(assignment.Roles.Count(r => r.Value == TeamRole.Challenger) == 1);
        Check(assignment.Roles.Count(r => r.Value == TeamRole.Cover) == 1);
    }
});
Test("roles: empty, solo, demolished and two-player teams are valid", () =>
{
    var me = CarAt(0, 0, new Vec3(0, -2000, 17));
    Check(TeamShape.Assign(Array.Empty<Car>(), 0, Vec3.Zero, _ => 1).FirstMan == -1);
    Check(TeamShape.Assign(new[] { me }, 0, Vec3.Zero, _ => 1).FirstMan == 0);
    var mate = CarAt(1, 0, new Vec3(0, -4000, 17)); mate.IsDemolished = true;
    Check(TeamShape.Assign(new[] { me, mate }, 0, Vec3.Zero, _ => 1).Count == 1);
    mate.IsDemolished = false;
    Check(TeamShape.Assign(new[] { me, mate }, 0, Vec3.Zero, _ => 1).Cover == 1);
});
Test("roles: seeded formation targets remain separated and mirror by team", () =>
{
    var rng = new Random(1743);
    for (int i = 0; i < 2000; i++)
    {
        Vec3 ball = new((float)rng.NextDouble() * 7000 - 3500, (float)rng.NextDouble() * 9600 - 4800, 100);
        Vec3 cover = TeamShape.Target(ball, Field.BlueGoal.Location, TeamRole.Cover);
        Vec3 support = TeamShape.Target(ball, Field.BlueGoal.Location, TeamRole.Support);
        Check(cover.FlatDist(support) > 1000);
        Check(MathF.Abs(cover.y) <= 4400 && Field.InField(cover, 150));
        Vec3 mirroredBall = new(-ball.x, -ball.y, ball.z);
        Vec3 mirrored = TeamShape.Target(mirroredBall, Field.OrangeGoal.Location, TeamRole.Cover);
        Near(mirrored.x, -cover.x); Near(mirrored.y, -cover.y);
    }
});
Test("navigation: routine targets never retreat behind the goal line", () =>
{
    foreach (float y in new[] { -6000f, -5120, 5120, 6000 })
    {
        Vec3 target = Navigation.FieldTarget(new Vec3(4000, y, 0));
        Check(MathF.Abs(target.y) <= 4600 && Field.InField(target, 200));
    }
});
Test("navigation: goal exits and backwall returns avoid the posts", () =>
{
    foreach (int side in new[] { -1, 1 }) foreach (float x in new[] { -1500f, -650, 0, 650, 1500 })
    {
        Vec3 car = new(x, side * 5450, 17), target = new(-1800, side * 4200, 17);
        for (int step = 0; step < 4; step++)
        {
            Vec3 waypoint = Navigation.Waypoint(car, target);
            foreach (float post in new[] { -Goal.Width / 2, Goal.Width / 2 })
                Check(SegmentDistance(car, waypoint, new Vec3(post, side * 5120, 0)) > 240,
                    $"post crossing from {car} to {waypoint}");
            car = waypoint;
        }
        Check(MathF.Abs(car.y) < 4800);
    }
});
Test("navigation: fast goal returns brake, do not boost or powerslide", () =>
{
    foreach (int side in new[] { -1, 1 })
    {
        var car = CarAt(0, side < 0 ? 0 : 1, new Vec3(0, side * 4300, 17), new Vec3(0, side * 1800, 0), 50, side * MathF.PI / 2);
        var output = Navigation.Control(car, new Vec3(0, side * 4500, 17), 2300, true, true);
        Check(output.Throttle < 0 && !output.Boost && !output.Handbrake);
    }
});
Test("navigation: sideways targets and routine routes conserve fuel", () =>
{
    var car = CarAt(0, 0, new Vec3(0, 0, 17), new Vec3(1200, 0, 0), 80, 0);
    Check(!Navigation.Control(car, new Vec3(0, 3000, 17), 2300, true, true).Boost);
    Check(!Navigation.Control(car, new Vec3(3000, 0, 17), 1410, true, false).Boost);
    car.Boost = 20;
    Check(!Navigation.Control(car, new Vec3(3000, 0, 17), 2300, true, false).Boost);
    Check(Navigation.Control(car, new Vec3(3000, 0, 17), 2300, true, true).Boost);
});
Test("navigation: actual production control stops before the net in a longitudinal plant", () =>
{
    foreach (float dt in new[] { 1f / 120, 1f / 60, 1f / 30 })
    {
        var car = CarAt(0, 0, new Vec3(0, -3000, 17), new Vec3(0, -2100, 0), 30, -MathF.PI / 2);
        float furthest = 0;
        for (int i = 0; i < (int)(4 / dt); i++)
        {
            Vec3 waypoint = Navigation.Waypoint(car.Location, new Vec3(0, -4400, 17));
            var control = Navigation.Control(car, waypoint, 1410, true, false);
            float speed = car.Velocity.Dot(car.Forward);
            float acceleration = MathF.Abs(control.Throttle) < 0.01f ? -MathF.Sign(speed) * 525 :
                control.Throttle * speed < 0 ? MathF.Sign(control.Throttle) * Car.BrakeAccel :
                control.Throttle * DrivePhysics.ThrottleAcceleration(MathF.Abs(speed));
            float next = speed + acceleration * dt;
            if (speed != 0 && MathF.Sign(next) != MathF.Sign(speed) && MathF.Abs(control.Throttle) < 0.01f) next = 0;
            car.Location += car.Forward * ((speed + next) * 0.5f * dt);
            car.Velocity = car.Forward * next;
            furthest = MathF.Max(furthest, -car.Location.y);
        }
        Check(furthest < 4800, $"overshot into goal area at {furthest}");
        Check(car.Velocity.FlatLen() < 150);
    }
});
Test("boost: teammate and opponent arrivals are checked at the PAD", () =>
{
    var me = CarAt(0, 0, new Vec3(0, -1000, 17), boost: 5);
    var mate = CarAt(1, 0, new Vec3(0, -200, 17), boost: 5);
    var foe = CarAt(2, 1, new Vec3(0, 500, 17));
    var pad = new Boost(0);
    Vec3 ball = new(0, 2000, 100), destination = Vec3.Zero;
    float Eta(Car c, Vec3 _) => c.Index == 0 ? 1 : 0.2f;
    Check(RoutePlanner.SelectBoost(me, new[] { pad }, ball, destination, 0, 6, Eta, new[] { me, mate }) == null);
    Check(RoutePlanner.SelectBoost(me, new[] { pad }, ball, destination, 0, 6, Eta, new[] { me, foe }) == null);
});
Test("boost: a nearby on-route small pad is available during defensive pressure", () =>
{
    var me = CarAt(0, 0, new Vec3(0, -200, 17), boost: 5);
    var pad = new Boost(0);
    Check(RoutePlanner.SelectBoost(me, new[] { pad }, new Vec3(0, 1000, 100), Vec3.Zero, 0, 0.8f,
        (_, _) => 0.1f, new[] { me }, true) == pad);
});
Test("boost: inactive pads and unsafe large detours are rejected", () =>
{
    var me = CarAt(0, 0, new Vec3(0, -200, 17), boost: 5);
    var pad = new Boost(0); pad.Update(new BoostPadStateT { IsActive = false, Timer = 2 });
    Near(pad.TimeUntilActive, 2);
    Check(RoutePlanner.SelectBoost(me, new[] { pad }, new Vec3(0, 1000, 100), Vec3.Zero, 0, 6) == null);
});
Test("aerial: ballistic tracking does not invent hover acceleration", () =>
{
    var car = CarAt(0, 0, new Vec3(0, 0, 700), new Vec3(100, 300, 200));
    Vec3 offset = new(0, 45, 125);
    var ball = new Ball(car.Location + offset, car.Velocity);
    foreach (float horizon in new[] { 0f, 0.08f, 0.12f })
        Near(AerialGuidance.TrackingAcceleration(car, ball.Predict(horizon), offset, Vec3.Zero, Game.Gravity, horizon).Length(), 0, 0.01f);
});
Test("aerial: supported reference has no look-ahead-induced hover bias", () =>
{
    var car = CarAt(0, 0, new Vec3(0, 0, 700));
    Vec3 offset = new(0, 45, 125);
    var reference = new Ball(car.Location + offset, Vec3.Zero);
    foreach (float horizon in new[] { 0f, 0.08f, 0.12f })
        Near(AerialGuidance.TrackingAcceleration(car, reference, offset, Vec3.Zero, Vec3.Zero, horizon).z, 650);
});
Test("aerial: feed-forward tracking is invariant to a common world velocity", () =>
{
    var car = CarAt(0, 0, new Vec3(0, 0, 700));
    var reference = new Ball(new Vec3(40, 20, 825), Vec3.Zero);
    Vec3 offset = new(0, 45, 125), shift = new(700, -300, 100);
    Vec3 initial = AerialGuidance.TrackingAcceleration(car, reference, offset, Vec3.Zero, Vec3.Zero, 0.1f);
    car.Velocity += shift; reference.location += shift * 0.1f; reference.velocity += shift;
    Near((initial - AerialGuidance.TrackingAcceleration(car, reference, offset, Vec3.Zero, Vec3.Zero, 0.1f)).Length(), 0, 0.01f);
});
Test("aerial: upward minimum bursts are blocked before the car climbs over the ball", () =>
{
    var car = CarAt(0, 0, new Vec3(0, 0, 600), new Vec3(0, 500, 110));
    car.Orientation = new Mat3x3(new Vec3(0.4f, MathF.PI / 2, 0));
    var ball = new Ball(new Vec3(0, 195, 690), new Vec3(0, 500, 0));
    Check(AerialGuidance.UnsafeUpwardBurst(car, ball));
    ball.location.z = 560; Check(AerialGuidance.LostCarry(car, ball));
});
Test("aerial: pulse duty tracks requested impulse at 120, 60 and 30 Hz", () =>
{
    foreach (float dt in new[] { 1f / 120, 1f / 60, 1f / 30 }) foreach (float demand in new[] { 250f, 650, 1000 })
    {
        var gate = new ImpulseBoost(); float actual = 0, start = -10; bool boosting = false;
        for (int i = 0; i < (int)(12 / dt); i++)
        {
            float time = i * dt;
            bool requested = gate.Step(time, demand, 1, 100, false);
            if (requested && !boosting) { boosting = true; start = time; }
            if (!requested && time - start >= ImpulseBoost.MinimumBurst) boosting = false;
            if (boosting) actual += ImpulseBoost.AirAcceleration * dt;
        }
        Check(MathF.Abs(actual / 12 - demand) < 55, $"pulse mean {actual / 12} versus {demand}, dt={dt}");
    }
});
Test("aerial: pulse gate rejects NaN, no fuel and unsafe alignment", () =>
{
    var gate = new ImpulseBoost();
    Check(!gate.Step(0, 1000, 1, float.NaN, false));
    Check(!gate.Step(1, 1000, 0.6f, 100, false));
    Check(!gate.Step(2, 1000, 1, 0, false));
    Check(!gate.Step(3, 1000, 1, 100, true));
});
Test("aerial: vertical analytic tracking remains bounded with minimum-duration boost", () =>
{
    foreach (float dt in new[] { 1f / 120, 1f / 60, 1f / 30 })
    {
        var car = CarAt(0, 0, new Vec3(0, 0, 800));
        var gate = new ImpulseBoost(); float start = -10, maxError = 0; bool boosting = false;
        for (int i = 0; i < (int)(8 / dt); i++)
        {
            float time = i * dt;
            var reference = new Ball(new Vec3(0, 0, 925), Vec3.Zero);
            Vec3 acceleration = AerialGuidance.TrackingAcceleration(car, reference, Vec3.Up * 125, Vec3.Zero, Vec3.Zero, 0.1f);
            bool requested = gate.Step(time, acceleration.z, 1, 100, false);
            if (requested && !boosting) { boosting = true; start = time; }
            if (!requested && time - start >= ImpulseBoost.MinimumBurst) boosting = false;
            float a = (boosting ? ImpulseBoost.AirAcceleration : 0) - 650;
            car.Location.z += car.Velocity.z * dt + 0.5f * a * dt * dt;
            car.Velocity.z += a * dt;
            maxError = MathF.Max(maxError, MathF.Abs(car.Location.z - 800));
        }
        Check(maxError < 35, $"vertical tracking error {maxError} at dt {dt}");
    }
});
Test("reset: matched velocity is NOT enough to coast into wheel contact", () =>
{
    var car = CarAt(0, 0, new Vec3(0, 0, 600), new Vec3(0, 500, 0));
    var ball = new Ball(new Vec3(0, 0, 770), car.Velocity);
    Check(!FlipReset.CanCoastToWheels(car, ball));
    car.Velocity.z = 120; Check(FlipReset.CanCoastToWheels(car, ball));
    car.Velocity.z = 400; Check(!FlipReset.CanCoastToWheels(car, ball));
});
Test("shots: close samples are dense and total candidates remain bounded", () =>
{
    var slices = Enumerable.Range(0, 721).Select(i => new BallSlice(10 + i / 120f, new Vec3(0, 0, 100), Vec3.Zero)).ToArray();
    var candidates = ShotPlanner.Candidates(slices, 10, 13).ToArray();
    Check(candidates.Length <= ShotPlanner.CandidateBudget);
    Check(candidates.Count(s => s.Time < 10.4f) >= 40);
    Check(candidates.All(s => s.Time > 10 && s.Time <= 13));
});
Test("shots: no candidate can be selected after the save deadline", () =>
{
    var slices = Enumerable.Range(0, 721).Select(i => new BallSlice(10 + i / 120f, new Vec3(0, 0, 100), Vec3.Zero)).ToArray();
    Check(ShotPlanner.Candidates(slices, 10, 10.16f).All(s => s.Time <= 10.16f));
    Check(!ShotPlanner.DeadlineAllows(10.5f, 10, 0.4f));
    Check(ShotPlanner.DeadlineAllows(10.2f, 10, 0.4f));
});
Test("shots: interior aim has ball-width margins from all three edges", () =>
{
    foreach (int team in new[] { 0, 1 })
    {
        Vec3 aim = ShotPlanner.InteriorTarget(new Vec3(900, 0, 700), Field.Goals[team].Location);
        Check(MathF.Abs(aim.x) + Ball.Radius < Goal.Width / 2);
        Check(aim.z - Ball.Radius > 0 && aim.z + Ball.Radius < Goal.Height);
    }
});
Test("defense: fast crossings use the goal-plane time, not the later sample", () =>
{
    var samples = new[] { new BallSlice(10, new Vec3(0, -5000, 100), Vec3.Zero), new BallSlice(10.2f, new Vec3(0, -5400, 100), Vec3.Zero) };
    Near(Tactics.GoalThreat(samples, Field.BlueGoal.Location, 10), 0.06f, 0.001f);
});
Test("defense: approaching box danger is recognized before a predicted goal", () =>
{
    var ball = new Ball(new Vec3(1000, -2000, 100), new Vec3(0, -1000, 0));
    var state = Defense.Read(ball, Array.Empty<BallSlice>(), Field.BlueGoal.Location, 0, Array.Empty<Car>(), 0);
    Check(state.Pressure && !state.Emergency);
});
Test("defense: actual dodge forces cannot be interrupted", () =>
{
    var locked = new LockedAction();
    var jumping = new JumpState(new PlayerInfoT { AirState = AirState.Dodging, HasDodged = true });
    Check(!Defense.MayInterrupt(locked, jumping));
    Check(Defense.MayInterrupt(new Recover(), jumping));
});
Test("integration: three bots retain distinct emergency backup lanes BETWEEN replans", () =>
{
    var cars = new[] { CarAt(0, 0, new Vec3(0, -3500, 17)), CarAt(1, 0, new Vec3(-1500, -1000, 17)), CarAt(2, 0, new Vec3(1500, -500, 17)) };
    World(10, new Ball(new Vec3(0, -4900, 100), new Vec3(0, -2500, 0)), cars);
    var bots = new[] { BotAt(0, 0), BotAt(1, 0), BotAt(2, 0) };
    foreach (var bot in bots) bot.Run();
    Check(bots.All(b => b.CurrentDefender == bots[0].CurrentDefender));
    var backups = bots.Where(b => b.Index != b.CurrentDefender).ToArray();
    Check(backups.All(b => b.Action is Navigate));
    Vec3[] targets = backups.Select(b => ((Navigate)b.Action).Target).ToArray();
    Check(targets[0].FlatDist(targets[1]) > 1000);
    for (int frame = 1; frame <= 4; frame++)
    {
        Set(typeof(Game), "Time", null, 10 + frame / 120f);
        foreach (var bot in bots) bot.Run();
        for (int i = 0; i < backups.Length; i++) Near((((Navigate)backups[i].Action).Target - targets[i]).Length(), 0);
    }
});
Test("integration: emergency interrupts an existing safe-navigation action immediately", () =>
{
    var car = CarAt(0, 0, new Vec3(0, -3000, 17));
    World(20, new Ball(new Vec3(0, -4900, 100), new Vec3(0, -2500, 0)), car);
    var bot = BotAt(0, 0);
    var old = new Navigate(new Vec3(3000, 0, 17)); bot.Action = old;
    bot.Run();
    Check(bot.CurrentDefender == 0 && bot.Decision.StartsWith("defend /"));
    Check(!ReferenceEquals(old, bot.Action));
    Check(bot.Action is not Shot shot || shot.Slice.Time < 20.088f);
});
Test("integration: planner samples actual ground-shot feasibility without crashing", () =>
{
    var car = CarAt(0, 0, new Vec3(0, -1000, 17), new Vec3(0, 1000, 0));
    World(30, new Ball(new Vec3(0, 0, 100), Vec3.Zero), car);
    var bot = BotAt(0, 0);
    Shot shot = ShotPlanner.Select(bot, false, 6, _ => false);
    Check(shot != null, "no shot for a simple uncontested approach");
    Check(ControlMath.Finite(shot.TargetLocation) && shot.ShotTarget.y > 0);
    Check(ShotPlanner.ContactFeasible(shot, car, shot.Slice.Time - Game.Time));
});
Console.WriteLine($"ROBUSTNESS RESULT: {passed} passed, {failed} failed; analytic/synthetic checks only, no in-game success rate.");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class LockedAction : IAction
{
    public bool Finished => false;
    public bool Interruptible => false;
    public void Run(RUBot _) { }
}
