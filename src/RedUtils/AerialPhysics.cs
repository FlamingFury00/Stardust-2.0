using System;
using RedUtils.Math;

namespace RedUtils
{
    /// <summary>Short-horizon aerial translation primitives based on Rocket League's measured force model.</summary>
    public static class AerialPhysics
    {
        public const float MinimumBoostTime = 0.1f;

        /// <summary>
        /// Constant world-space acceleration required to reach a position after <paramref name="time"/>,
        /// after removing the supplied gravity term. This is a local guidance primitive, not an open-loop simulator.
        /// </summary>
        public static Vec3 RequiredAcceleration(Vec3 position, Vec3 velocity, Vec3 targetPosition, float time, Vec3 gravity)
        {
            if (!Finite(position) || !Finite(velocity) || !Finite(targetPosition) || !Finite(gravity) ||
                !float.IsFinite(time) || time <= 0.0001f)
                return Vec3.Zero;

            return 2 * (targetPosition - position - velocity * time - gravity * (0.5f * time * time)) /
                (time * time);
        }

        private static bool Finite(Vec3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    /// <summary>
    /// Converts a continuous forward-acceleration request into boost-start pulses while accounting for the
    /// engine's unavoidable minimum boost burst. The returned boolean is the controller command; the internal
    /// model separately tracks boost that remains physically active after that command is released.
    /// </summary>
    public sealed class ImpulseBoostGate
    {
        private float previous = float.NaN;
        private float lockoutUntil = float.NegativeInfinity;
        private float impulseDebt;
        private bool hasStarted;

        public bool Step(float now, float demand, float alignment, float fuel, bool gentleContact)
        {
            if (!float.IsFinite(now))
            {
                Reset();
                return false;
            }

            float dt;
            if (!float.IsFinite(previous))
            {
                dt = 1f / 120f;
            }
            else
            {
                dt = now - previous;
                if (!float.IsFinite(dt) || dt < 0 || dt > 0.25f)
                {
                    Reset();
                    dt = 1f / 120f;
                }
                else
                {
                    dt = System.Math.Clamp(dt, 0, 1f / 15f);
                }
            }
            previous = now;

            bool safe = float.IsFinite(demand) && float.IsFinite(alignment) && float.IsFinite(fuel) &&
                fuel > 0 && alignment >= 0.88f && !gentleContact;
            if (!safe)
                return false;

            // Positive air throttle supplies 66.667 uu/s^2 independently. Boost is responsible
            // only for the residual forward acceleration.
            float desiredBoostAcceleration =
                System.Math.Clamp(demand - Car.AirThrottleAccel, 0, Car.BoostAccel);

            if (dt > 0)
            {
                impulseDebt += desiredBoostAcceleration * dt;
                float debtLimit = Car.BoostAccel * AerialPhysics.MinimumBoostTime * 2;
                impulseDebt = System.Math.Clamp(impulseDebt, -debtLimit, debtLimit);
            }

            if (desiredBoostAcceleration <= 0 || dt == 0)
                return false;

            // At near-full demand, pulse-width modulation only creates needless one-tick gaps.
            // Hold the command continuously and clear debt because actual thrust ~= requested thrust.
            if (desiredBoostAcceleration >= Car.BoostAccel * 0.9f)
            {
                impulseDebt = 0;
                hasStarted = true;
                lockoutUntil = now;
                return true;
            }

            // One true controller tick starts a physical burst that Rocket League will continue even
            // after the command is released. Never retrigger inside that guaranteed burst.
            if (now < lockoutUntil)
                return false;

            if (!hasStarted || impulseDebt > 0)
            {
                float burstDuration = MathF.Min(AerialPhysics.MinimumBoostTime,
                    MathF.Max(dt, fuel / Car.BoostConsumption));
                float burstImpulse = Car.BoostAccel * burstDuration;
                impulseDebt -= burstImpulse;
                lockoutUntil = now + burstDuration;
                hasStarted = true;
                return true;
            }

            return false;
        }

        public void Reset()
        {
            previous = float.NaN;
            lockoutUntil = float.NegativeInfinity;
            impulseDebt = 0;
            hasStarted = false;
        }
    }
}
