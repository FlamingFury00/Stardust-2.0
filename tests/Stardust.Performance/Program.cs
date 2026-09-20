using System.Diagnostics;
using RedUtils;
using RedUtils.Math;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }

Surface ReferenceNearest(Vec3 point, IReadOnlyCollection<Surface>? excluded = null, bool drivableOnly = false)
{
    IEnumerable<Surface> source = drivableOnly ? Field.DrivableSurfaces.Values : Field.Surfaces.Values;
    Surface? best = null;
    float bestDistance = float.PositiveInfinity;
    foreach (Surface surface in source)
    {
        if (excluded != null && excluded.Contains(surface)) continue;
        float distance = point.Dist(surface.Limit(point));
        if (distance < bestDistance)
        {
            bestDistance = distance;
            best = surface;
        }
    }
    return best ?? throw new InvalidOperationException("reference has no eligible surface");
}

Test("surface lookup: optimized path must preserve exact nearest-surface identity", () =>
{
    var rng = new Random(371);
    Surface[] excluded = { Field.Surfaces["Ground"], Field.Surfaces["Ceiling"] };
    for (int i = 0; i < 2000; i++)
    {
        Vec3 point = new(
            (float)rng.NextDouble() * 10000 - 5000,
            (float)rng.NextDouble() * 12000 - 6000,
            (float)rng.NextDouble() * 2400);
        Check(ReferenceEquals(ReferenceNearest(point, drivableOnly: true), Field.NearestSurface(point)),
            $"drivable lookup changed for {point}");
        Check(ReferenceEquals(ReferenceNearest(point, excluded), Field.NearestSurface(point, excluded)),
            $"excluded lookup changed for {point}");
    }
});

Test("surface lookup: warm queries must not allocate per call", () =>
{
    Surface[] excluded = { Field.Surfaces["Ground"], Field.Surfaces["Ceiling"] };
    for (int i = 0; i < 50; i++)
    {
        Field.NearestSurface(Vec3.Zero);
        Field.NearestSurface(Vec3.Zero, excluded);
    }

    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 2000; i++)
    {
        Field.NearestSurface(new Vec3(i % 100 - 50, i % 200 - 100, i % 150));
        Field.NearestSurface(new Vec3(i % 100 - 50, i % 200 - 100, i % 150), excluded);
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated <= 4096, $"surface lookup allocated {allocated} bytes for 4000 warm queries");
});

Test("drive physics: ETA rollout is allocation-free after warmup", () =>
{
    for (int i = 0; i < 20; i++) DrivePhysics.TravelTime(4000, 300, 40);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 5000; i++) DrivePhysics.TravelTime(4000, 300, 40);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated <= 1024, $"drive rollout allocated {allocated} bytes");
});

var watch = Stopwatch.StartNew();
for (int i = 0; i < 20000; i++)
    Field.NearestSurface(new Vec3(i % 4096, (i * 7) % 5120, (i * 13) % 1950));
watch.Stop();
Console.WriteLine($"PERF DIAGNOSTIC: 20000 nearest-surface queries in {watch.Elapsed.TotalMilliseconds:F3} ms.");
Console.WriteLine($"PERF RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;
