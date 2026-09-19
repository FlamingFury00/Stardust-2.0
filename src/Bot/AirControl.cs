using System;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    /// <summary>Ball-relative tracking: a freely falling target shares gravity with the car.</summary>
    public static class AirControl
    {
        public const float BoostAcceleration = Car.BoostAccel + Car.AirThrottleAccel;
        public const float MinimumBoostBurst = 0.1f; // RocketSim RLConst::BOOST_MIN_TIME
        public static Vec3 CarryAcceleration(Car car, Ball ball, Ball predicted, float horizon, Vec3 lane)
        {
            horizon = System.Math.Clamp(horizon, 0.04f, 0.2f);
            lane = ControlMath.FlatUnit(lane, car.Forward);
            Vec3 normal = ControlMath.Unit(lane * 0.45f + Vec3.Up * 0.89f, Vec3.Up);
            Vec3 target = predicted.location - normal * 128;
            Vec3 error = target - car.PredictLocation(horizon);
            Vec3 relative = predicted.velocity - car.PredictVelocity(horizon);
            Vec3 targetAcceleration = (predicted.velocity - ball.velocity) / horizon;
            // Do not inject a permanent upward velocity or compensate gravity twice.
            targetAcceleration = targetAcceleration.Cap(0, 1600);
            Vec3 acceleration = error * 8 + relative * 5.5f + targetAcceleration - Game.Gravity;
            return ControlMath.Finite(acceleration) ? acceleration.Cap(0, 1800) : Vec3.Zero;
        }

        public static Vec3 ResetApproach(Car car, Ball ball)
        {
            Vec3 delta = ball.location - car.Location;
            Vec3 toward = ControlMath.Unit(delta, Vec3.Up);
            float gap = MathF.Max(0, delta.Length() - 112);
            float closing = System.Math.Clamp(gap * 2.5f, 0, 260);
            return ((ball.velocity + toward * closing - car.Velocity) * 6).Cap(0, 1400);
        }

        /// <summary>Gate a NEW burst using its unavoidable impulse, not just the current distance.</summary>
        public static bool SafeBurst(Car car, Ball ball, Vec3 desiredNose, float reserve = 6)
        {
            if (!ControlMath.Finite(desiredNose) || !float.IsFinite(car.Boost) || car.Boost <= reserve) return false;
            Vec3 delta = ball.location - car.Location;
            Vec3 relative = car.Velocity - ball.velocity;
            const float lookahead = MinimumBoostBurst + 0.04f;
            float forecastGap = delta.z - relative.z * lookahead -
                0.5f * BoostAcceleration * MathF.Max(car.Forward.z, desiredNose.z) * lookahead * lookahead;
            if (car.Forward.z > 0.05f && (delta.z < 90 || forecastGap < 90)) return false;
            float closing = relative.Dot(ControlMath.Unit(delta, Vec3.Up));
            if (delta.Length() < 210 && closing > 140) return false;
            return car.Forward.Dot(desiredNose) > 0.94f;
        }
    }

    /// <summary>Impulse-budget modulation with the simulator's 100 ms minimum burst duration.</summary>
    public sealed class ThrustBudget
    {
        private float last = float.NaN, until = float.NegativeInfinity, credit;
        public bool Step(float now, float demand, bool safeToStart, float fuel)
        {
            if (!float.IsFinite(now) || !float.IsFinite(demand) || !float.IsFinite(fuel) || fuel <= 0)
            { credit = 0; return false; }
            float dt = float.IsFinite(last) && now >= last ? MathF.Min(now - last, 0.05f) : 1f / 120;
            if (float.IsFinite(last) && now < last) { until = float.NegativeInfinity; credit = 0; }
            last = now;
            float duty = System.Math.Clamp(demand / AirControl.BoostAcceleration, 0, 1);
            bool ongoing = now < until;
            credit = System.Math.Clamp(credit + (duty - (ongoing ? 1 : 0)) * dt, -0.15f, 0.02f);
            // Issuing false cannot undo Rocket League's already-started minimum burst.
            if (ongoing) return safeToStart;
            if (!safeToStart || demand < 60) { credit = MathF.Min(credit, 0); return false; }
            if (credit < 0) return false;
            until = now + AirControl.MinimumBoostBurst;
            credit -= dt;
            return true;
        }
    }

    /// <summary>Event counters separate selection, acquisition, dodge and useful follow-through.</summary>
    public sealed class MechanicsTelemetry
    {
        public int ResetAttempts { get; private set; }
        public int ResetAcquisitions { get; private set; }
        public int ResetDodges { get; private set; }
        public int ResetFollowThroughs { get; private set; }
        public int ResetAborts { get; private set; }
        public void Record(string stage, bool trace, int index, string reason = "")
        {
            switch (stage)
            {
                case "attempt": ResetAttempts++; break;
                case "acquired": ResetAcquisitions++; break;
                case "dodge": ResetDodges++; break;
                case "follow-through": ResetFollowThroughs++; break;
                case "abort": ResetAborts++; break;
            }
            if (trace) Console.WriteLine(FormattableString.Invariant(
                $"stardust mechanic=reset stage={stage} car={index} t={Game.Time:F3} reason={reason} attempts={ResetAttempts} acquired={ResetAcquisitions} dodges={ResetDodges} follow={ResetFollowThroughs} aborts={ResetAborts}"));
        }
    }
}

namespace Bot
{
    public static class ResetPolicy
    {
        public static bool Allowed(Car car, Ball ball, TacticalFrame frame, int team,
            bool enabled, float now, float cooldown, bool solo)
        {
            return enabled && now >= cooldown && frame.FirstMan == car.Index && car.Boost >= 42 &&
                ball.location.y * Field.Side(team) < -300 && ball.location.z > 700 &&
                !Tactics.UnderPressure(ball, new Vec3(0, Field.Side(team) * 5120, 0), frame.OpponentEta) &&
                (solo ? frame.OpponentEta > 2 : frame.HasCover && frame.OpponentEta > 1.7f);
        }
    }
}
