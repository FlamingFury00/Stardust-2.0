using System;

namespace RedUtils
{
    /// <summary>
    /// Bounded one-dimensional ground travel model for ETA estimation.
    /// It intentionally models longitudinal acceleration only; turn geometry remains in Drive.
    /// </summary>
    public static class DrivePhysics
    {
        /// <summary>
        /// Approximate full-throttle acceleration as a function of forward speed.
        /// Matches the measured Rocket League throttle curve used by the existing drive model.
        /// </summary>
        public static float ThrottleAcceleration(float speed)
        {
            if (!float.IsFinite(speed)) return 0;
            if (speed < 0) return Car.BrakeAccel;
            if (speed < 1400) return 1600 - speed * (1440f / 1400f);
            if (speed < 1410) return 160f * (1410 - speed) / 10f;
            return 0;
        }

        /// <summary>
        /// Integrate full-throttle travel over a short fixed step, consuming boost only while it
        /// can add forward speed. Reverse travel never assumes boost.
        /// </summary>
        public static float TravelTime(float distance, float speed, float fuel, bool reverse = false)
        {
            if (!float.IsFinite(distance) || !float.IsFinite(speed) || !float.IsFinite(fuel))
                return float.PositiveInfinity;
            if (distance <= 0) return 0;

            speed = System.Math.Clamp(speed, 0, Car.MaxSpeed);
            fuel = reverse ? 0 : System.Math.Clamp(fuel, 0, 100);

            float covered = 0;
            float time = 0;
            const float dt = 0.05f;

            for (int step = 0; step < 600; step++)
            {
                float boostFraction = !reverse && speed < Car.MaxSpeed
                    ? MathF.Min(1, fuel / (Car.BoostConsumption * dt))
                    : 0;
                float acceleration = ThrottleAcceleration(speed) + Car.BoostAccel * boostFraction;
                float nextSpeed = MathF.Min(Car.MaxSpeed, speed + acceleration * dt);
                float advance = (speed + nextSpeed) * 0.5f * dt;

                if (advance > 0 && covered + advance >= distance)
                    return time + dt * (distance - covered) / advance;

                covered += advance;
                speed = nextSpeed;
                fuel = MathF.Max(0, fuel - boostFraction * Car.BoostConsumption * dt);
                time += dt;
            }

            return speed > 1
                ? time + MathF.Max(0, distance - covered) / speed
                : float.PositiveInfinity;
        }
    }
}
