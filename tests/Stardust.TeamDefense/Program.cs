using System.Reflection;
using Bot;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }

Test("roles: first-man comparator is a total order for all observers", () =>
{
    float[] eta = { 1.12f, 1.06f, 1.00f };
    int Winner(int observer)
    {
        int winner = observer;
        for (int i = 0; i < eta.Length; i++)
            if (i != observer && Tactics.WinsTie(eta[i], i, eta[winner], winner))
                winner = i;
        return winner;
    }
    int a = Winner(0), b = Winner(1), c = Winner(2);
    Check(a == b && b == c, $"same snapshot elected different first men: {a}, {b}, {c}");
});

Test("roles: support and anchor cannot collapse onto the same lane", () =>
{
    Vec3 ball = new(0, 0, 100), goal = new(0, -5120, 0);
    Vec3 support = Tactics.ShadowTarget(ball, goal, false);
    Vec3 anchor = Tactics.ShadowTarget(ball, goal, true);
    float separation = support.FlatDist(anchor);
    Check(separation >= 900, $"support/anchor separation is only {separation:F1} uu");
});

Test("opponent model: car adapter exposes RLBot v5 last input", () =>
{
    FieldInfo? field = typeof(Car).GetField("LastInput", BindingFlags.Public | BindingFlags.Instance);
    Check(field != null && field.FieldType == typeof(ControllerStateT),
        "Car discards PlayerInfo.last_input, so opponent intent cannot be modeled");
});

Test("opponent model: pre-contact pressure detector exists", () =>
{
    MethodInfo? method = typeof(Tactics).GetMethod("OpponentPressure", BindingFlags.Public | BindingFlags.Static);
    Check(method != null, "no pre-contact opponent pressure detector exists; defense waits for ball-only threat");
});

Console.WriteLine($"TEAM DEFENSE RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;
