using System;

namespace RedUtils
{
    /// <summary>Bounded longitudinal rollout. Turning/wall geometry remains an estimate, not a reachability proof.</summary>
    public static class DrivePhysics
    {
        public static float ThrottleAcceleration(float speed)
        {
            if (speed < 0) return Car.BrakeAccel;
            if (speed < 1400) return 1600 - speed * (1440f / 1400);
            if (speed < 1410) return 160 * (1410 - speed) / 10;
            return 0;
        }

        public static float TravelTime(float distance, float speed, float fuel, bool reverse = false)
        {
            if (!float.IsFinite(distance) || !float.IsFinite(speed) || !float.IsFinite(fuel)) return float.PositiveInfinity;
            if (distance <= 0) return 0;
            speed = System.Math.Clamp(speed, -Car.MaxSpeed, Car.MaxSpeed);
            fuel = reverse ? 0 : System.Math.Clamp(fuel, 0, 100);
            float covered = 0, time = 0;
            const float dt = 0.05f;
            for (int step = 0; step < 600; step++)
            {
                float boostFraction = speed < Car.MaxSpeed ? MathF.Min(1, fuel / (Car.BoostConsumption * dt)) : 0;
                float acceleration = ThrottleAcceleration(speed) + Car.BoostAccel * boostFraction;
                float nextSpeed = MathF.Min(Car.MaxSpeed, speed + acceleration * dt);
                float advance = (speed + nextSpeed) * 0.5f * dt;
                if (advance > 0 && covered + advance >= distance)
                    return time + dt * (distance - covered) / advance;
                covered += advance;
                speed = nextSpeed;
                fuel -= boostFraction * Car.BoostConsumption * dt;
                time += dt;
            }
            return time + MathF.Max(0, distance - covered) / MathF.Max(1, speed);
        }
    }
}
