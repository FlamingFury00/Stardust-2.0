using System.Reflection;
using Bot;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;
using BallPrediction = RedUtils.BallPrediction;

// Deliberately added before the fixes. These use production code, not a reimplementation.
// This executable must fail on the merged 3.0 candidate and pass after the repair.
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

Test("pad initialization must be idempotent across bot instances", () =>
{
    Field.Boosts.Clear();
    var field = new FieldInfoT { BoostPads = new List<BoostPadT>
        { new() { Location = new Vector3T { X = 100, Y = 200 }, IsFullBoost = true } } };
    Field.Initialize(field);
    Field.Initialize(field);
    Check(Field.Boosts.Count == 1, $"one pad became {Field.Boosts.Count} pads");
});
Test("inactive boost timers mean elapsed time, not remaining time", () =>
{
    var pad = new Boost(0, new BoostPadT { Location = new Vector3T(), IsFullBoost = true });
    pad.Update(new BoostPadStateT { IsActive = false, Timer = 4 });
    Check(MathF.Abs(pad.TimeUntilActive - 6) < 0.001f, $"expected 6 seconds remaining, got {pad.TimeUntilActive}");
});
Test("deep defense cannot collapse support and cover onto the same point", () =>
{
    Vec3 ball = new(1800, -4100, 100), goal = Field.BlueGoal.Location;
    Vec3 cover = Tactics.ShadowTarget(ball, goal, true), support = Tactics.ShadowTarget(ball, goal, false);
    Check(cover.FlatDist(support) >= 900, $"role separation is only {cover.FlatDist(support)} uu");
});
Test("ETA tie ordering must be transitive and independent of observer", () =>
{
    float[] eta = { 1.12f, 1.06f, 1.00f };
    int Winner(int observer)
    {
        int winner = observer;
        for (int i = 0; i < eta.Length; i++)
            if (i != observer && Tactics.WinsTie(eta[i], i, eta[winner], winner)) winner = i;
        return winner;
    }
    Check(Winner(0) == Winner(1) && Winner(1) == Winner(2),
        $"observers disagree: {Winner(0)}, {Winner(1)}, {Winner(2)}");
});
Test("ground Drive must not boost through a ninety-degree yaw error", () =>
{
    var bot = new ProbeBot();
    Set(typeof(RLBot.Manager.Bot), "Index", bot, 0);
    Set(typeof(RLBot.Manager.Bot), "Team", bot, 0);
    Set(typeof(RUBot), "Renderer", bot, new ExtendedRenderer(((RLBot.Manager.Bot)bot).Renderer));
    ((RLBot.Manager.Bot)bot).Renderer.Begin("regression");
    Cars.AllCars.Clear();
    var car = new Car { Location = new Vec3(0, 0, 17), Velocity = new Vec3(1500, 0, 0), IsGrounded = true, Boost = 50 };
    Cars.AllCars.Add(car);
    new Drive(car, new Vec3(0, 3000, 0), 2300, allowDodges: false, wasteBoost: true).Run(bot);
    Check(!bot.Controller.Boost, "boost was requested while the target was sideways");
});
Test("aerial carry must not add upward thrust into predicted height overshoot", () =>
{
    var bot = new ProbeBot();
    Set(typeof(RLBot.Manager.Bot), "Index", bot, 0);
    Set(typeof(RLBot.Manager.Bot), "Team", bot, 0);
    var car = new Car { Location = new Vec3(0, 0, 600), Velocity = new Vec3(0, 500, 110),
        Orientation = new Mat3x3(new Vec3(0.4f, MathF.PI / 2, 0)), Boost = 50 };
    Cars.AllCars.Clear(); Cars.AllCars.Add(car);
    Vec3 location = new(0, 195, 690), velocity = new(0, 500, 0);
    Set(typeof(Ball), "Location", null, location);
    Set(typeof(Ball), "Velocity", null, velocity);
    Set(typeof(Ball), "Prediction", null, new BallPrediction { Slices = new[] {
        new BallSlice(Game.Time, location, velocity),
        new BallSlice(Game.Time + 0.12f, location + velocity * 0.12f + Game.Gravity * 0.0072f, velocity + Game.Gravity * 0.12f) } });
    new AerialCarry().Run(bot);
    Check(!bot.Controller.Boost, "upward boost was requested with only 68 uu of predicted vertical clearance");
});
Console.WriteLine($"BUG REPRO RESULT: {passed} passed, {failed} failed. Synthetic packet/control tests, not live match results.");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class ProbeBot : RUBot
{
    public ProbeBot() : base("stardust-regression") { }
    public override void Run() { }
}
