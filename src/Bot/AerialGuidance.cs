using System;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    /// <summary>Impulse accounting for the engine's minimum boost burst.</summary>
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
            bool safe = float.IsFinite(demand) && float.IsFinite(alignment) && float.IsFinite(fuel) && fuel > 0 && alignment > 0.94f && !unsafeContact;
            bool applied = commanded || now - began < MinimumBurst;
            debt = System.Math.Clamp(debt + (safe ? System.Math.Clamp(demand, 0, AirAcceleration) : 0) * dt -
                (applied ? AirAcceleration * dt : 0), -AirAcceleration * MinimumBurst, AirAcceleration * MinimumBurst);
            if (!safe) { commanded = false; debt = MathF.Min(0, debt); return false; }
            if (now - began < MinimumBurst) return commanded = true;
            if (debt > 8 && demand > 80) { began = now; return commanded = true; }
            return commanded = false;
        }
        public void Reset() { previous = float.NaN; began = float.NegativeInfinity; debt = 0; commanded = false; }
    }

    public static class AerialGuidance
    {
        public const float CarryHeight = 125;
        /// <summary>Predict under the reference feed-forward before applying tracking correction.</summary>
        public static Vec3 TrackingAcceleration(Car car, Ball prediction, Vec3 offset, Vec3 closingVelocity,
            Vec3 referenceAcceleration, float horizon)
        {
            // Predicting an unboosted falling car against a hovering target and THEN adding -gravity
            // biases the nominal hover. Use a common reference acceleration for the look-ahead instead.
            Vec3 position = car.Location + car.Velocity * horizon + referenceAcceleration * (0.5f * horizon * horizon);
            Vec3 velocity = car.Velocity + referenceAcceleration * horizon;
            return (prediction.location - offset - position) * 7 +
                (prediction.velocity + closingVelocity - velocity) * 4.5f + referenceAcceleration - Game.Gravity;
        }
        public static Vec3 CarryAcceleration(Car car, Ball ball, Ball prediction, Vec3 lane, float horizon)
        {
            lane = ControlMath.FlatUnit(lane, car.Forward);
            Vec3 delta = ball.location - car.Location;
            float distance = delta.Length();
            float support = distance < 180 && delta.z > 105 && delta.FlatLen() < 110 ? System.Math.Clamp((180 - distance) / 45, 0, 1) : 0;
            Vec3 correction = -Game.Gravity * support;
            // Framework prediction has no car contacts. This local, explicitly heuristic contact target
            // gradually asks for support, rather than pretending its ballistic prediction already includes it.
            Ball reference = new(prediction.location + correction * (0.5f * horizon * horizon), prediction.velocity + correction * horizon);
            return TrackingAcceleration(car, reference, lane * 45 + Vec3.Up * CarryHeight,
                lane * 35, Game.Gravity + correction, horizon);
        }
        public static bool UnsafeUpwardBurst(Car car, Ball ball, float burst = ImpulseBoost.MinimumBurst)
        {
            Vec3 delta = ball.location - car.Location;
            float closingZ = car.Velocity.z - ball.velocity.z;
            float predictedGap = delta.z - MathF.Max(0, closingZ) * (burst + 0.12f) -
                0.5f * ImpulseBoost.AirAcceleration * MathF.Max(0, car.Forward.z) * burst * burst;
            float closing = (car.Velocity - ball.velocity).Dot(ControlMath.Unit(delta, Vec3.Up));
            return (car.Forward.z > 0.05f && predictedGap < 100) || (delta.Length() < 200 && closing > 120) || delta.z < 45;
        }
        public static bool LostCarry(Car car, Ball ball) => ball.location.z < car.Location.z - 20 ||
            (ball.location - car.Location).Flatten().Length() > 700;
    }
}
