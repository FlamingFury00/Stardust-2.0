using System.Runtime.CompilerServices;
using RedUtils;
using RedUtils.Math;

internal static class SurfaceChecks
{
    [ModuleInitializer]
    internal static void Run()
    {
        var random = new Random(371);
        Surface[] excluded = { Field.Surfaces["Ground"], Field.Surfaces["Ceiling"] };
        for (int i = 0; i < 1000; i++)
        {
            Vec3 point = new((float)random.NextDouble() * 10000 - 5000,
                (float)random.NextDouble() * 12000 - 6000, (float)random.NextDouble() * 2400);
            // Independent exhaustive ordered minimum, matching the old collection/filter implementation.
            Surface driving = Field.DrivableSurfaces.Values.OrderBy(s => point.Dist(s.Limit(point))).First();
            Surface filtered = Field.Surfaces.Values.Where(s => !excluded.Contains(s)).OrderBy(s => point.Dist(s.Limit(point))).First();
            if (!ReferenceEquals(driving, Field.NearestSurface(point))) throw new Exception("Driving surface search changed its result.");
            if (!ReferenceEquals(filtered, Field.NearestSurface(point, excluded))) throw new Exception("Excluded-surface search changed its result.");
        }
        for (int i = 0; i < 10; i++) { Field.NearestSurface(Vec3.Zero); Field.NearestSurface(Vec3.Zero, excluded); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { Field.NearestSurface(Vec3.Zero); Field.NearestSurface(Vec3.Zero, excluded); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated > 4096) throw new Exception($"Surface lookup allocated {allocated} bytes for 2000 warm queries.");
        Console.WriteLine($"SURFACE RESULT: 3 equivalence/allocation checks passed; {allocated} bytes for 2000 warm queries.");
    }
}
