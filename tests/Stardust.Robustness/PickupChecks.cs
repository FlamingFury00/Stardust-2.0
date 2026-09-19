using System.Runtime.CompilerServices;
using Bot;
using RedUtils;
using RedUtils.Math;

internal static class PickupChecks
{
    [ModuleInitializer]
    internal static void Run()
    {
        var car = new Car { Location = new Vec3(0, 0, 17), Velocity = new Vec3(200, 0, 0), IsGrounded = true };
        Vec3 nearby = new(50, 0, 17);
        if (Navigation.Control(car, nearby, 1410, false, false).Throttle <= 0)
            throw new Exception("Through-pickup navigation must not apply the parking stop condition.");
        if (Navigation.Control(car, nearby, 1410, true, false).Throttle >= 0)
            throw new Exception("Parking navigation must still brake at its target.");
        Console.WriteLine("PICKUP RESULT: 2 through-target/parking checks passed.");
    }
}
