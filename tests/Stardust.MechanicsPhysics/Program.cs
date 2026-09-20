using System.Reflection;
using Bot;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;
using BallPrediction = RedUtils.BallPrediction;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Set(Type type, string property, object? instance, object value) =>
    type.GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!
        .SetValue(instance, value);

void SetBall(Vec3 location, Vec3 velocity, BallPrediction prediction)
{
    Set(typeof(Ball), nameof(Ball.Location), null, location);
    Set(typeof(Ball), nameof(Ball.Velocity), null, velocity);
    Set(typeof(Ball), nameof(Ball.Prediction), null, prediction);
}

ProbeBot Probe(Car car, int team = 0)
{
    var bot = new ProbeBot();
    Set(typeof(RLBot.Manager.Bot), "Index", bot, 0);
    Set(typeof(RLBot.Manager.Bot), "Team", bot, team);
    Cars.AllCars.Clear();
    Cars.AllCars.Add(car);
    return bot;
}

Test("aerial carry: predicted vertical overshoot must not request upward boost", () =>
{
    Set(typeof(Game), nameof(Game.Time), null, 0f);
    var car = new Car
    {
        Location = new Vec3(0, 0, 600),
        Velocity = new Vec3(0, 500, 110),
        Orientation = new Mat3x3(new Vec3(0.4f, MathF.PI / 2, 0)),
        Boost = 50,
        IsGrounded = false
    };
    Vec3 location = new(0, 195, 690), velocity = new(0, 500, 0);
    var prediction = new BallPrediction
    {
        Slices = new[]
        {
            new BallSlice(0, location, velocity),
            new BallSlice(0.12f, location + velocity * 0.12f + Game.Gravity * 0.0072f,
                velocity + Game.Gravity * 0.12f)
        }
    };
    SetBall(location, velocity, prediction);
    var bot = Probe(car);
    new AerialCarry().Run(bot);
    Check(!bot.Controller.Boost,
        "air carry requested boost even though its short-horizon contact point is below the car's ballistic path");
});

Test("possession flight: identical ballistic motion needs no control acceleration", () =>
{
    var car = new Car
    {
        Location = new Vec3(100, -200, 650),
        Velocity = new Vec3(700, 120, 180),
        Orientation = new Mat3x3(Vec3.Zero),
        IsGrounded = false,
        Boost = 50
    };
    const float horizon = 0.18f;
    Vec3 targetPosition = car.PredictLocation(horizon);
    Vec3 targetVelocity = car.PredictVelocity(horizon);
    Vec3 acceleration = PossessionControl.FlightAtHorizon(car, targetPosition, targetVelocity, horizon);
    Check(acceleration.Length() < 1f,
        $"identical ballistic motion requested {acceleration} ({acceleration.Length():F1} uu/s^2) of control");
});

Test("ground dribble: pre-contact pressure triggers the flick window", () =>
{
    Set(typeof(Game), nameof(Game.Time), null, 0f);
    var car = new Car
    {
        Index = 0,
        Team = 0,
        Location = new Vec3(0, 0, 17),
        Velocity = new Vec3(0, 800, 0),
        Orientation = new Mat3x3(new Vec3(0, MathF.PI / 2, 0)),
        Boost = 40,
        IsGrounded = true
    };
    Vec3 ballLocation = car.Location + car.Forward * 20 + car.Up * 150;
    SetBall(ballLocation, car.Velocity, new BallPrediction
    {
        Slices = new[] { new BallSlice(0.3f, ballLocation, car.Velocity) }
    });

    var bot = new Stardust();
    Set(typeof(RLBot.Manager.Bot), "Index", bot, 0);
    Set(typeof(RLBot.Manager.Bot), "Team", bot, 0);
    Cars.AllCars.Clear();
    Cars.AllCars.Add(car);
    Set(typeof(Stardust), nameof(Stardust.Situation), bot, new TacticalFrame
    {
        MyEta = 0.05f,
        OpponentEta = 2f,
        PressureTime = 0.4f,
        FirstMan = 0,
        TeamRank = 0,
        TeamCount = 1
    });

    var dribble = new GroundDribble();
    bot.Action = dribble;
    dribble.Run(bot);
    Set(typeof(Game), nameof(Game.Time), null, 0.30f);
    dribble.Run(bot);
    Check(bot.Action is ControlledFlick,
        $"imminent pre-contact pressure did not trigger flick; action is {bot.Action?.GetType().Name ?? "null"}");
});

Console.WriteLine($"MECHANICS PHYSICS RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class ProbeBot : RUBot
{
    public ProbeBot() : base("stardust-mechanics-regression") { }
    public override void Run() { }
}
