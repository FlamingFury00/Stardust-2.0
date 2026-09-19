using System.Runtime.CompilerServices;
using System.Reflection;
using RedUtils;
using RedUtils.Math;

internal static class GeometryChecks
{
    [ModuleInitializer]
    internal static void Run()
    {
        static void Near(float a, float b)
        {
            if (!float.IsFinite(a) || MathF.Abs(a - b) > 0.001f) throw new Exception($"Geometry regression: {a} != {b}");
        }
        // Previously the flat angle of (1,0,1) with itself was 60 degrees.
        Near(new Vec3(1, 0, 1).FlatAngle(new Vec3(1, 0, 1)), 0);
        Near(new Vec3(1, 0, 100).FlatAngle(new Vec3(1, 0, -100)), 0);
        Near(new Vec3(1, 100, 0).FlatAngle(new Vec3(1, -100, 0), Vec3.Y), 0);
        Near(new Vec3(1, 0, 100).FlatAngle(new Vec3(0, 1, -100)), MathF.PI / 2);
        Near(Field.Surfaces["Orange Right Backwall"].Location.x, -Field.Surfaces["Blue Right Backwall"].Location.x);
        Near(Field.Surfaces["Orange Left Backwall"].Location.x, -Field.Surfaces["Blue Left Backwall"].Location.x);
        Vec3 quarterTurn = AerialShot.TurnDisplacement(Vec3.X, Vec3.Y, 1000, MathF.PI / 2);
        Near(quarterTurn.x, 1000); Near(quarterTurn.y, 1000);
        Vec3 zeroTurn = AerialShot.TurnDisplacement(Vec3.X, Vec3.Y, 1000, 0);
        Near(zeroTurn.Length(), 0);
        var car = new Car { Location = new Vec3(0, 0, 500), Boost = 50, IsGrounded = false };
        var shot = new AerialShot(car, new BallSlice(Game.Time + 1, new Vec3(800, 0, 800), Vec3.Zero), new Vec3(0, 5120, 200));
        if (!(bool)typeof(AerialShot).GetField("_jumped", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(shot)!)
            throw new Exception("An airborne aerial must not execute another ground launch.");
        Console.WriteLine("GEOMETRY RESULT: 9 projection/surface/aerial checks passed.");
    }
}
