using System;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    /// <summary>Accounting for the minimum boost burst, rather than treating boost as an analog throttle.</summary>
    public sealed class ImpulseBoost
    {
        public const float MinimumBurst = 0.1f, AirAcceleration = 1058.333f;
        private float previous = float.NaN, began = float.NegativeInfinity, debt;
        private bool commanded;
        public bool Step(float now, float demand, float alignment, float fuel, bool unsafeContact)
        {
            if (!float.IsFinite(now)) { Reset(); return false; }
            float dt = float.IsFinite(previous) ? now - previous : 1f / 120;
            if (dt < 0 || dt > 0.25f) { Reset(); dt = 1f / 120; }
            previous = now;
            dt = System.Math.Clamp(dt, 0, 1f / 15);
            bool safe = float.IsFinite(demand) && float.IsFinite(alignment) && float.IsFinite(fuel) &&
                fuel > 0 && alignment > 0.94f && !unsafeContact;
            // Count unavoidable residual thrust even after the output was cancelled for safety.
            bool applied = commanded || now - began < MinimumBurst;
            debt = System.Math.Clamp(debt + (safe ? System.Math.Clamp(demand, 0, AirAcceleration) : 0) * dt -
                (applied ? AirAcceleration * dt : 0), -AirAcceleration * MinimumBurst, AirAcceleration * MinimumBurst);
            if (!safe) { commanded = false; debt = MathF.Min(0, debt); return false; }
            if (now - began < MinimumBurst) return commanded = true;
            if (debt > 8 && demand > 80)
            { began = now; return commanded = true; }
            return commanded = false;
        }
        public void Reset() { previous = float.NaN; began = float.NegativeInfinity; debt = 0; commanded = false; }
    }

    public static class AerialGuidance
    {
        public const float CarryHeight = 125;
        /// <summary>Both car and reference experience gravity. Do not compensate gravity twice for a falling ball.</summary>
        public static Vec3 TrackingAcceleration(Car car, Ball prediction, Vec3 offset, Vec3 closingVelocity,
            Vec3 referenceAcceleration, float horizon)
        {
            Vec3 error = prediction.location - offset - car.PredictLocation(horizon);
            Vec3 velocityError = prediction.velocity + closingVelocity - car.PredictVelocity(horizon);
            return error * 7f + velocityError * 4.5f + referenceAcceleration - Game.Gravity;
        }

        public static Vec3 CarryAcceleration(Car car, Ball ball, Ball prediction, Vec3 lane, float horizon)
        {
            lane = ControlMath.FlatUnit(lane, car.Forward);
            float distance = ball.location.Dist(car.Location);
            // Only a close, centered contact may ask for support against gravity. A separated ball is ballistic.
            Vec3 delta = ball.location - car.Location;
            float support = distance < 180 && delta.z > 105 && delta.FlatLen() < 110 ?
                System.Math.Clamp((180 - distance) / 45, 0, 1) : 0;
            Vec3 referenceAcceleration = Game.Gravity * (1 - support);
            Vec3 offset = lane * 45 + Vec3.Up * CarryHeight;
            return TrackingAcceleration(car, prediction, offset, lane * 35, referenceAcceleration, horizon);
        }

        public static bool UnsafeUpwardBurst(Car car, Ball ball, float burst = ImpulseBoost.MinimumBurst)
        {
            Vec3 delta = ball.location - car.Location;
            float closingZ = car.Velocity.z - ball.velocity.z;
            float predictedGap = delta.z - MathF.Max(0, closingZ) * (burst + 0.12f) -
                0.5f * ImpulseBoost.AirAcceleration * MathF.Max(0, car.Forward.z) * burst * burst;
            float closing = (car.Velocity - ball.velocity).Dot(ControlMath.Unit(delta, Vec3.Up));
            bool high = car.Forward.z > 0.05f && predictedGap < 100;
            bool impact = delta.Length() < 200 && closing > 120;
            return high || impact || delta.z < 45;
        }

        public static bool LostCarry(Car car, Ball ball) => ball.location.z < car.Location.z - 20 ||
            (ball.location - car.Location).Flatten().Length() > 700;
    }
}
