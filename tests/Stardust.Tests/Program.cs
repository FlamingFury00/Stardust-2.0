using System.Globalization;
using Bot;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;

// No RLBot server, game, GPU, or test-framework NuGet package is required.
// These are software/control regressions, NOT evidence of match strength or ball-contact success.
int passed = 0, failed = 0;
void Test(string name, Action run)
{
    try { run(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error}"); }
}
void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
void Near(float actual, float expected, float tolerance = 0.0001f)
{ Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance, $"expected {expected}, got {actual}"); }
BallSlice Slice(float time, float x = 0, float y = 0, float z = 100) => new(time, new Vec3(x, y, z), Vec3.Zero);
BallPrediction Prediction(params BallSlice[] slices) => new() { Slices = slices };
Car GroundCar() => new() { Location = new Vec3(0, 0, 17), IsGrounded = true, Boost = 50 };
Car AirCar() => new() { Location = new Vec3(0, 0, 500), Velocity = new Vec3(600, 0, 100), Boost = 40 };
JumpState Jump(bool jumped = false, bool doubled = false, bool dodged = false,
    float timeout = -1, AirState state = AirState.InAir) => new(new PlayerInfoT
    { HasJumped = jumped, HasDoubleJumped = doubled, HasDodged = dodged, DodgeTimeout = timeout, AirState = state });

Test("clock: current dt, duplicate packet, rewind, and pause", () =>
{
    var clock = new TickClock();
    Near(clock.Step(10), 1f / 120);
    Near(clock.Step(10), 0);
    Near(clock.Step(10 + 1f / 60), 1f / 60, 0.00001f);
    Near(clock.Step(1), 1f / 120); Check(clock.Discontinuity);
    Near(clock.Step(20), 1f / 120); Check(clock.Discontinuity);
});
Test("clock: nonfinite input cannot poison the next frame", () =>
{
    var clock = new TickClock();
    Near(clock.Step(float.NaN), 0); Check(clock.Discontinuity);
    Near(clock.Step(0), 1f / 120); Check(!clock.Discontinuity);
});
Test("clock: bounded deltas across seeded packet jitter", () =>
{
    var clock = new TickClock(); var rng = new Random(8128); float time = 0;
    for (int i = 0; i < 10000; i++)
    {
        time += (float)rng.NextDouble() * 0.3f - 0.02f;
        float dt = clock.Step(time);
        Check(float.IsFinite(dt) && dt >= 0 && dt <= 1f / 15f);
    }
});
Test("jump: persistent spent flip remains spent in InAir", () =>
{
    Check(!Jump(true, false, true, 1).CanDodge);
    Check(!Jump(true, true, false, 1).CanDodge);
    Check(!Jump(true, true).HasReset);
});
Test("jump: finite timeout and indefinite reset are distinct", () =>
{
    Check(Jump(true, timeout: 0.5f).CanDodge);
    Check(!Jump(true, timeout: 0).CanDodge);
    Check(Jump().HasReset && Jump().CanDodge);
    Check(!Jump(state: AirState.OnGround).CanDodge);
});
Test("jump: packet adapter repairs the legacy car flags", () =>
{
    var car = new Car();
    JumpState.Apply(car, new PlayerInfoT { AirState = AirState.InAir, HasJumped = true, HasDodged = true });
    Check(car.HasJumped && car.HasDoubleJumped);
});
Test("reset: spent state plus wheel touch plus restored flags confirms", () =>
{
    var evidence = new ResetEvidence();
    Check(!evidence.Observe(Jump(true, true), false, false, 650, 1));
    Check(evidence.Observe(Jump(), true, true, 650, 1.1f));
});
Test("reset: free fall alone is not evidence of acquisition", () =>
{
    var evidence = new ResetEvidence();
    Check(!evidence.Observe(Jump(), true, true, 650, 1));
});
Test("reset: other-player touch and incorrect wheel orientation fail", () =>
{
    var evidence = new ResetEvidence();
    evidence.Observe(Jump(true, true), false, false, 650, 1);
    Check(!evidence.Observe(Jump(), false, true, 650, 1.1f));
    Check(!evidence.Observe(Jump(), true, false, 650, 1.2f));
});
Test("reset: stale contact and floor contact fail", () =>
{
    var evidence = new ResetEvidence();
    evidence.Observe(Jump(true, true), true, true, 650, 1);
    Check(!evidence.Observe(Jump(), false, true, 650, 1.4f));
    Check(!evidence.Observe(Jump(), true, true, 17, 1.5f));
});
Test("lifecycle: own carry touch survives, opponent touch cancels", () =>
{
    var carry = new FakePossession();
    Check(!ControlRuntime.CancelBeforeRun(carry, true, true, false, false));
    Check(ControlRuntime.CancelBeforeRun(carry, true, false, false, false));
});
Test("lifecycle: ordinary actions cancel on changed own touch", () =>
{
    Check(ControlRuntime.CancelBeforeRun(new FakeAction(), true, true, false, false));
});
Test("lifecycle: physical commit survives touch, but not demolition or reset", () =>
{
    var action = new FakeAction { Interruptible = false };
    Check(!ControlRuntime.CancelBeforeRun(action, true, false, false, false));
    Check(ControlRuntime.CancelBeforeRun(action, false, false, true, false));
    Check(ControlRuntime.CancelBeforeRun(action, false, false, false, true));
});
Test("lifecycle: idle and finished actions are handled", () =>
{
    Check(!ControlRuntime.CancelBeforeRun(null!, true, true, false, false));
    Check(ControlRuntime.CancelBeforeRun(new FakeAction { Finished = true }, false, false, false, false));
});
Test("outputs: nonfinite axes, limits, fuel and demolished neutrality", () =>
{
    var controls = ControlRuntime.Sanitize(new ControllerStateT
        { Throttle = float.NaN, Steer = float.PositiveInfinity, Pitch = 2, Yaw = -2, Roll = 0.4f, Boost = true }, false, 0);
    Near(controls.Throttle, 0); Near(controls.Steer, 0); Near(controls.Pitch, 1); Near(controls.Yaw, -1);
    Check(!controls.Boost);
    controls = ControlRuntime.Sanitize(new ControllerStateT { Jump = true, Boost = true, Throttle = 1 }, true, 100);
    Check(!controls.Jump && !controls.Boost && controls.Throttle == 0);
});
Test("outputs: NaN fuel never boosts", () =>
{
    Check(!ControlRuntime.Sanitize(new ControllerStateT { Boost = true }, false, float.NaN).Boost);
});
Test("claims: wire format is independent of machine locale", () =>
{
    var previous = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
        Check(ShotClaimLedger.Encode(11.25f) == "CLAIM_SHOT;11.25");
        var ledger = new ShotClaimLedger();
        Check(ledger.Receive(0, "CLAIM_SHOT;11.25", 10));
        Check(ledger.EarlierThan(1, 11.25f, 10));
    }
    finally { CultureInfo.CurrentCulture = previous; }
});
Test("claims: exactly one car owns equal-time claims", () =>
{
    var a = new ShotClaimLedger(); var b = new ShotClaimLedger();
    a.Receive(1, "CLAIM_SHOT;11", 10); b.Receive(0, "CLAIM_SHOT;11", 10);
    Check(!a.EarlierThan(0, 11, 10)); Check(b.EarlierThan(1, 11, 10));
});
Test("claims: genuinely earlier higher-index player wins", () =>
{
    var ledger = new ShotClaimLedger(); ledger.Receive(3, "CLAIM_SHOT;10.5", 10);
    Check(ledger.EarlierThan(0, 11, 10));
});
Test("claims: stale claims expire and release is exact", () =>
{
    var ledger = new ShotClaimLedger(); ledger.Receive(0, "CLAIM_SHOT;12", 10);
    Check(!ledger.EarlierThan(1, 12, 11.01f));
    Check(!ledger.Receive(0, "RELEASE_SHOT;garbage", 10));
    ledger.Receive(0, "RELEASE_SHOT", 10);
    Check(!ledger.EarlierThan(1, 12, 10));
});
Test("claims: malformed, nonfinite, oversized, and out-of-horizon messages fail", () =>
{
    var ledger = new ShotClaimLedger();
    foreach (string message in new[] { "CLAIM_SHOT;NaN", "CLAIM_SHOT;Infinity", "CLAIM_SHOT;17", "CLAIM_SHOT;1", "CLAIM_SHOT;10,5", new string('x', 1000) })
        Check(!ledger.Receive(0, message, 10), message);
});
Test("claims: identity count is bounded", () =>
{
    var ledger = new ShotClaimLedger();
    for (int i = 0; i < 64; i++) Check(ledger.Receive(i, "CLAIM_SHOT;11", 10));
    Check(!ledger.Receive(64, "CLAIM_SHOT;11", 10));
    Check(ledger.Receive(0, "CLAIM_SHOT;11.5", 10));
});
Test("prediction: null and empty inputs do not crash", () =>
{
    var empty = new BallPrediction((BallPredictionT)null!);
    Check(empty.Length == 0 && empty.Find(_ => true) == null && !empty.TrySample(1, out _));
    Check(default(BallPrediction).FindGoal(0) == null);
});
Test("prediction: successful old coarse index is no longer skipped", () =>
{
    var slices = Enumerable.Range(0, 7).Select(i => Slice(i)).ToArray();
    Check(ReferenceEquals(Prediction(slices).Find(s => s.Time == 6), slices[6]));
});
Test("prediction: short arrays and tail samples are searchable", () =>
{
    Check(Prediction(Slice(1), Slice(2)).Find(s => s.Time == 2)?.Time == 2);
    var prediction = Prediction(Enumerable.Range(0, 14).Select(i => Slice(i)).ToArray());
    Check(prediction.Find(s => s.Time == 13)?.Time == 13);
});
Test("prediction: interpolation uses timestamps rather than tick index", () =>
{
    var prediction = Prediction(Slice(1, 0), Slice(1.2f, 100), Slice(1.7f, 600));
    Check(prediction.TrySample(1.45f, out Ball sample)); Near(sample.location.x, 350, 0.001f);
});
Test("prediction: endpoints and single sample work without division by zero", () =>
{
    var prediction = Prediction(Slice(1, 42));
    Check(prediction.TrySample(1, out Ball sample)); Near(sample.location.x, 42);
    Check(!prediction.TrySample(0.99f, out _) && !prediction.TrySample(1.01f, out _));
});
Test("prediction: duplicate timestamps and invalid query are bounded", () =>
{
    var prediction = Prediction(Slice(1), Slice(1), Slice(2, 100));
    Check(prediction.TrySample(1, out _)); Check(prediction.TrySample(1.5f, out Ball sample)); Near(sample.location.x, 50);
    Check(!prediction.TrySample(float.NaN, out _));
});
Test("prediction: do not shoot at samples after a goal", () =>
{
    var prediction = Prediction(Slice(1, y: 5300), Slice(2, y: 0));
    Check(prediction.Find(s => s.Time == 2) == null);
    Check(prediction.FindGoal(0)?.Time == 1);
});
Test("attitude: zero error produces zero controls", () =>
{
    var car = new Car(); var controls = new ControllerStateT();
    ControlMath.Aim(car, controls, car.Forward, car.Up);
    Near(controls.Pitch, 0); Near(controls.Yaw, 0); Near(controls.Roll, 0);
});
Test("attitude: nose-up and yaw signs match Rocket League torque axes", () =>
{
    var car = new Car(); var controls = new ControllerStateT();
    ControlMath.Aim(car, controls, new Vec3(1, 0, 1), Vec3.Up); Check(controls.Pitch > 0);
    ControlMath.Aim(car, controls, new Vec3(1, 1, 0), Vec3.Up); Check(controls.Yaw > 0);
});
Test("attitude: exactly inverted and backwards targets do not stall", () =>
{
    var car = new Car(); var controls = new ControllerStateT();
    ControlMath.Aim(car, controls, car.Forward, -Vec3.Up); Check(MathF.Abs(controls.Roll) > 0.5f);
    Near(ControlMath.RotationError(car, -car.Forward, Vec3.Up).Length(), MathF.PI, 0.001f);
});
Test("attitude: seeded random rotations produce finite shortest arcs", () =>
{
    var rng = new Random(2048);
    for (int i = 0; i < 2000; i++)
    {
        Vec3 rotation = new((float)rng.NextDouble() * 3 - 1.5f, (float)rng.NextDouble() * 6 - 3, (float)rng.NextDouble() * 6 - 3);
        var car = new Car { Orientation = new Mat3x3(rotation) };
        Vec3 target = new((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
        Vec3 error = ControlMath.RotationError(car, target, Vec3.Up);
        Check(ControlMath.Finite(error) && error.Length() <= MathF.PI + 0.001f);
        var controls = new ControllerStateT(); ControlMath.Aim(car, controls, target, Vec3.Up);
        Check(float.IsFinite(controls.Pitch) && MathF.Abs(controls.Pitch) <= 1);
    }
});
Test("attitude: rate damping opposes angular velocity at zero error", () =>
{
    var car = new Car { LocalAngularVelocity = new Vec3(1, 1, 1) }; var controls = new ControllerStateT();
    ControlMath.Aim(car, controls, car.Forward, car.Up);
    Check(controls.Roll > 0 && controls.Pitch > 0 && controls.Yaw < 0);
});
Test("flight: equilibrium includes gravity compensation", () =>
{
    Vec3 acceleration = ControlMath.FlightAcceleration(Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, new Vec3(0, 0, -650));
    Near(acceleration.z, 650); Near(acceleration.x, 0);
});
Test("flight: relative velocity brakes an overshooting car", () =>
{
    Vec3 acceleration = ControlMath.FlightAcceleration(Vec3.Zero, new Vec3(500, 0, 0), new Vec3(10, 0, 0), Vec3.Zero, Vec3.Zero);
    Check(acceleration.x < 0);
});
Test("flight: common velocity must not create artificial look-ahead error", () =>
{
    var a = AirCar(); a.Velocity = Vec3.Zero;
    Vec3 position = new(100, 0, 650); Vec3 velocity = Vec3.Zero;
    Vec3 baseAcceleration = PossessionControl.FlightAtHorizon(a, position, velocity, 0.12f);
    Vec3 shift = new(800, 200, 0); a.Velocity += shift;
    Vec3 shiftedAcceleration = PossessionControl.FlightAtHorizon(a, position + shift * 0.12f, velocity + shift, 0.12f);
    Near((baseAcceleration - shiftedAcceleration).Length(), 0, 0.001f);
});
Test("boost: impulse pulse and immediate safety gates", () =>
{
    var gate = new BoostGate();
    Check(gate.Step(0, 300, 1, 50, false));
    // The controller command may release immediately; Rocket League continues the physical
    // boost burst to its minimum duration, which the gate accounts for internally.
    Check(!gate.Step(0.01f, 180, 1, 50, false));
    Check(!gate.Step(0.02f, 300, 0.5f, 50, false));
    Check(!gate.Step(1, 1000, 1, 50, true));
    Check(!gate.Step(2, 1000, 1, 0, false));
});
Test("jump sequence: release is observed before second jump after dropped frame", () =>
{
    var sequence = new JumpSequence();
    Check(sequence.Step(0, false).Jump);
    Check(!sequence.Step(0.3f, true).Jump);
    Check(!sequence.Step(0.3f, true).Jump);
    Check(sequence.Step(0.31f, true).Dodge);
    Check(!sequence.Step(0.5f, true).Jump);
    Check(!sequence.Step(1, true).Dodge);
});
Test("jump sequence: unavailable or expired second jump is never manufactured", () =>
{
    var sequence = new JumpSequence(); sequence.Step(0, false); sequence.Step(0.2f, false);
    Check(!sequence.Step(0.3f, false).Jump);
    Check(!sequence.Step(0.8f, true).Jump);
});
Test("ground carry: possession gate requires space and a controllable ball", () =>
{
    var car = GroundCar(); var ball = new Ball(new Vec3(38, 0, 150), Vec3.Zero);
    Check(GroundDribble.CanStart(car, ball, 1));
    Check(!GroundDribble.CanStart(car, ball, 0.2f));
    ball.location.y = 300; Check(!GroundDribble.CanStart(car, ball, 1));
});
Test("ground carry: equal forward speed does not cause spurious acceleration", () =>
{
    var car = GroundCar(); var ball = new Ball(new Vec3(38, 0, 150), Vec3.Zero);
    var stationary = PossessionControl.GroundCarry(car, ball, car.Forward);
    car.Velocity = new Vec3(900, 0, 0); ball.velocity = car.Velocity;
    var moving = PossessionControl.GroundCarry(car, ball, car.Forward);
    Near(stationary.Throttle, moving.Throttle); Near(moving.Throttle, 0);
    Check(!moving.Boost && !moving.Handbrake);
});
Test("ground carry: throttle corrects forward error in both directions", () =>
{
    var car = GroundCar();
    Check(PossessionControl.GroundCarry(car, new Ball(new Vec3(80, 0, 150), Vec3.Zero), car.Forward).Throttle > 0);
    Check(PossessionControl.GroundCarry(car, new Ball(new Vec3(0, 0, 150), Vec3.Zero), car.Forward).Throttle < 0);
});
Test("aerial carry: availability respects boost and relative speed", () =>
{
    var car = AirCar(); var ball = new Ball(new Vec3(100, 0, 650), car.Velocity);
    Check(AerialCarry.CanStart(car, ball, 2));
    car.Boost = 0; Check(!AerialCarry.CanStart(car, ball, 2));
    car.Boost = 40; ball.velocity = new Vec3(-2000, 0, 0); Check(!AerialCarry.CanStart(car, ball, 2));
});
Test("reset: entry requires an already spent flip", () =>
{
    var car = AirCar(); var ball = new Ball(new Vec3(80, 0, 650), car.Velocity);
    Check(FlipReset.CanStart(car, ball, Jump(true, true)));
    Check(!FlipReset.CanStart(car, ball, Jump()));
    car.Boost = 5; Check(!FlipReset.CanStart(car, ball, Jump(true, true)));
});
Test("defense: predicted goal threats are team symmetric", () =>
{
    Near(Tactics.GoalThreat(new[] { Slice(10.5f, y: -5200) }, new Vec3(0, -5120, 0), 10), 0.5f);
    Near(Tactics.GoalThreat(new[] { Slice(10.5f, y: 5200) }, new Vec3(0, 5120, 0), 10), 0.5f);
});
Test("defense: wide, high, historical and distant future balls are not imminent goals", () =>
{
    Vec3 goal = new(0, -5120, 0);
    foreach (BallSlice slice in new[] { Slice(10.5f, x: 1600, y: -5200), Slice(10.5f, y: -5200, z: 950), Slice(9, y: -5200), Slice(15, y: -5200) })
        Check(float.IsPositiveInfinity(Tactics.GoalThreat(new[] { slice }, goal, 10)));
});
Test("roles: equal ETA ownership is antisymmetric", () =>
{
    Check(Tactics.WinsTie(1, 0, 1, 1)); Check(!Tactics.WinsTie(1, 1, 1, 0));
    Check(Tactics.WinsTie(0.5f, 3, 1, 0));
});
Test("kickoff: left-goes tie break is mirrored for orange", () =>
{
    var left = new Car { Index = 1, Location = new Vec3(2048, -2560, 17) };
    var right = new Car { Index = 0, Location = new Vec3(-2048, -2560, 17) };
    Check(Tactics.KickoffBefore(left, right, new Vec3(0, 0, 92), 0));
    left.Location = new Vec3(-2048, 2560, 17); right.Location = new Vec3(2048, 2560, 17);
    Check(Tactics.KickoffBefore(left, right, new Vec3(0, 0, 92), 1));
});
Test("support: deep defense chooses the opposite post on both teams", () =>
{
    Vec3 blue = Tactics.ShadowTarget(new Vec3(2000, -4000, 100), new Vec3(0, -5120, 0), true);
    Vec3 orange = Tactics.ShadowTarget(new Vec3(-2000, 4000, 100), new Vec3(0, 5120, 0), true);
    Check(blue.x < 0 && blue.y < -4000 && orange.x > 0 && orange.y > 4000);
});

Test("geometry: planar angle ignores vertical components", () =>
{
    Near(new Vec3(1, 0, 1).FlatAngle(new Vec3(1, 0, -5)), 0);
    Near(new Vec3(1, 0, 8).FlatAngle(new Vec3(0, 1, -3)), MathF.PI / 2, 0.0002f);
});
Test("field: repeated initialization does not duplicate boost pads", () =>
{
    var field = new FieldInfoT
    {
        BoostPads = new List<BoostPadT>
        {
            new() { Location = new Vector3T { X = 100, Y = 200, Z = 0 }, IsFullBoost = true }
        }
    };
    Field.Initialize(field);
    Field.Initialize(field);
    Check(Field.Boosts.Count == 1, $"expected 1 boost pad, got {Field.Boosts.Count}");
});
Test("boost: inactive timer is converted from elapsed to remaining", () =>
{
    var full = new Boost(0, new BoostPadT { Location = new Vector3T(), IsFullBoost = true });
    full.Update(new BoostPadStateT { IsActive = false, Timer = 4 });
    Near(full.TimeUntilActive, 6);

    var small = new Boost(1, new BoostPadT { Location = new Vector3T(), IsFullBoost = false });
    small.Update(new BoostPadStateT { IsActive = false, Timer = 1.5f });
    Near(small.TimeUntilActive, 2.5f);
    small.Update(new BoostPadStateT { IsActive = true, Timer = 0 });
    Near(small.TimeUntilActive, 0);
});
Test("aerial: circular turn displacement scales both components by radius", () =>
{
    Vec3 displacement = AerialShot.TurnDisplacement(Vec3.X, Vec3.Y, 500, MathF.PI / 2);
    Near(displacement.x, 500, 0.001f);
    Near(displacement.y, 500, 0.001f);
});

Console.WriteLine($"RESULT {passed} passed, {failed} failed; no in-game performance claim.");
Environment.ExitCode = failed == 0 ? 0 : 1;

class FakeAction : IAction
{
    public bool Finished { get; set; }
    public bool Interruptible { get; set; } = true;
    public void Run(RUBot bot) { }
}
sealed class FakePossession : FakeAction, IPossessionAction { public float ClaimTime => 1; }
