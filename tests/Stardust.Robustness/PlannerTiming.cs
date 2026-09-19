using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Bot;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;
using BallPrediction = RedUtils.BallPrediction;

internal static class PlannerTiming
{
    [ModuleInitializer]
    internal static void Measure()
    {
        static void Set(Type t, string property, object instance, object value) =>
            t.GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!.SetValue(instance, value);
        Cars.Initialize(new GamePacketT { Players = new() });
        for (int i = 0; i < 6; i++)
        {
            int side = i < 3 ? -1 : 1;
            Cars.AllCars.Add(new Car { Index = i, Team = (uint)(i / 3), IsGrounded = true, Boost = 40,
                Location = new Vec3((i % 3 - 1) * 1800, side * (1200 + i % 3 * 900), 17),
                Velocity = new Vec3(0, -side * 800, 0), Orientation = new Mat3x3(new Vec3(0, -side * MathF.PI / 2, 0)) });
        }
        var bots = Enumerable.Range(0, 3).Select(i =>
        {
            var bot = new Stardust("stardust-planning-timing");
            Set(typeof(RLBot.Manager.Bot), "Index", bot, i);
            Set(typeof(RLBot.Manager.Bot), "Team", bot, 0);
            return bot;
        }).ToArray();
        const float now = 5;
        Set(typeof(Game), "Time", null, now);
        Set(typeof(Ball), "Location", null, new Vec3(0, 0, 100));
        Set(typeof(Ball), "Velocity", null, Vec3.Zero);
        Set(typeof(Ball), "Prediction", null, new BallPrediction { Slices = Enumerable.Range(0, 721)
            .Select(i => new BallSlice(now + i / 120f, new Vec3(0, 0, 100), Vec3.Zero)).ToArray() });
        static void Plan(Stardust[] bots)
        {
            foreach (var bot in bots)
            {
                TacticalFrame frame = Tactics.Evaluate(bot);
                _ = ShotPlanner.Select(bot, false, frame.OpponentEta, _ => false);
            }
        }
        for (int i = 0; i < 10; i++) Plan(bots);
        var samples = new double[100];
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < samples.Length; i++)
        {
            long start = Stopwatch.GetTimestamp();
            Plan(bots);
            samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
        Array.Sort(samples);
        Console.WriteLine(FormattableString.Invariant(
            $"SYNTHETIC PLANNING: 3 sequential bots, 6 cars, 721 slices; p50={samples[49]:F3}ms p95={samples[94]:F3}ms p99={samples[98]:F3}ms; allocated={allocation / samples.Length} bytes/batch. No live tick-latency claim; one stationary-ball scenario."));
    }
}
