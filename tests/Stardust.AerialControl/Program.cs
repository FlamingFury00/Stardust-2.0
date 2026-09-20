using Bot;
using RedUtils;
using RedUtils.Math;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }

float EffectiveDuty(float demand, float alignment = 1, float fuel = 100, float seconds = 5, float dt = 1f / 120)
{
    var gate = new BoostGate();
    bool applied = false;
    float boostingTime = 0;
    int appliedTicks = 0, ticks = (int)MathF.Ceiling(seconds / dt);
    for (int i = 0; i < ticks; i++)
    {
        float now = i * dt;
        bool command = gate.Step(now, demand, alignment, fuel, false);

        // RocketSim-style minimum boost burst model: once boost starts it keeps applying
        // until at least 0.1 s has elapsed, even if command becomes false.
        if (applied)
        {
            if (!command && boostingTime >= 0.1f)
            {
                applied = false;
                boostingTime = 0;
            }
        }
        else if (command)
        {
            applied = true;
            boostingTime = 0;
        }

        if (applied)
        {
            appliedTicks++;
            boostingTime += dt;
        }
    }
    return (float)appliedTicks / ticks;
}

Test("boost pulse: moderate 300 uu/s^2 demand is not continuous boost", () =>
{
    float duty = EffectiveDuty(300);
    Check(duty > 0.12f && duty < 0.45f, $"effective duty was {duty:P1}");
});

Test("boost pulse: low 150 uu/s^2 demand is not discarded", () =>
{
    float duty = EffectiveDuty(150);
    Check(duty > 0.025f && duty < 0.20f, $"effective duty was {duty:P1}");
});

Test("boost pulse: unsafe alignment never starts a burst", () =>
{
    float duty = EffectiveDuty(500, alignment: 0.5f);
    Check(duty == 0, $"misaligned duty was {duty:P1}");
});

Test("boost pulse: zero fuel never starts a burst", () =>
{
    float duty = EffectiveDuty(500, fuel: 0);
    Check(duty == 0, $"zero-fuel duty was {duty:P1}");
});

Test("boost pulse: duty remains bounded across control cadences", () =>
{
    foreach (float dt in new[] { 1f / 120, 1f / 60, 1f / 30, 1f / 15 })
    {
        float moderate = EffectiveDuty(300, dt: dt);
        float low = EffectiveDuty(150, dt: dt);
        Check(moderate > 0.12f && moderate < 0.45f,
            $"300 uu/s^2 duty at {1 / dt:F0} Hz was {moderate:P1}");
        Check(low > 0.025f && low < 0.20f,
            $"150 uu/s^2 duty at {1 / dt:F0} Hz was {low:P1}");
    }
});

Test("boost pulse: near-full demand remains continuous", () =>
{
    float duty = EffectiveDuty(1000);
    Check(duty > 0.95f, $"near-full demand duty was {duty:P1}");
});

Test("guidance: ballistic endpoint requires no control acceleration", () =>
{
    Vec3 position = new(100, -200, 600), velocity = new(700, 50, 250), gravity = new(0, 0, -650);
    const float time = 0.8f;
    Vec3 target = position + velocity * time + gravity * (0.5f * time * time);
    Vec3 acceleration = AerialPhysics.RequiredAcceleration(position, velocity, target, time, gravity);
    Check(acceleration.Length() < 0.001f, $"ballistic correction was {acceleration}");
});

Test("guidance: hover compensates gravity", () =>
{
    Vec3 acceleration = AerialPhysics.RequiredAcceleration(Vec3.Zero, Vec3.Zero, Vec3.Zero, 1,
        new Vec3(0, 0, -650));
    Check(MathF.Abs(acceleration.z - 650) < 0.001f && MathF.Abs(acceleration.x) < 0.001f,
        $"hover acceleration was {acceleration}");
});

Test("guidance: finite-time acceleration reconstructs target", () =>
{
    Vec3 gravity = new(0, 0, -650), desired = new(200, -120, 350);
    Vec3 position = new(10, 20, 500), velocity = new(300, -50, 100);
    const float time = 0.65f;
    Vec3 target = position + velocity * time + (gravity + desired) * (0.5f * time * time);
    Vec3 recovered = AerialPhysics.RequiredAcceleration(position, velocity, target, time, gravity);
    Check((recovered - desired).Length() < 0.01f, $"expected {desired}, recovered {recovered}");
});

Test("guidance: invalid horizon returns neutral acceleration", () =>
{
    Check(AerialPhysics.RequiredAcceleration(Vec3.Zero, Vec3.Zero, Vec3.X, 0, new Vec3(0, 0, -650)).Length() == 0,
        "zero-time guidance was not neutral");
});

Console.WriteLine($"AERIAL CONTROL RESULT: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;
