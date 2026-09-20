using System;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public sealed class TacticalFrame
    {
        public float MyEta, OpponentEta = 6, TeammateEta = 6;
        public int FirstMan;
        public bool LastBack;
        public float FreeTime => OpponentEta - MyEta;
    }

    public static class Tactics
    {
        /// <summary>Conservative goal-mouth crossing detector, using prediction timestamps rather than indices.</summary>
        public static float GoalThreat(BallSlice[] slices, Vec3 goal, float now, float horizon = 2.5f)
        {
            if (slices == null) return float.PositiveInfinity;
            float side = goal.y < 0 ? -1 : 1;
            for (int i = 0; i < slices.Length; i++)
            {
                BallSlice b = slices[i];
                if (b == null || b.Time < now) continue;
                if (b.Time > now + horizon) break;
                if (b.Location.y * side >= MathF.Abs(goal.y) &&
                    MathF.Abs(b.Location.x - goal.x) < Goal.Width / 2 + Ball.Radius &&
                    b.Location.z < Goal.Height + Ball.Radius) return b.Time - now;
            }
            return float.PositiveInfinity;
        }

        public static bool WinsTie(float eta, int index, float otherEta, int otherIndex, float margin = 0.08f) =>
            eta < otherEta - margin || (MathF.Abs(eta - otherEta) <= margin && index < otherIndex);

        public static bool KickoffBefore(Car a, Car b, Vec3 ball, int team)
        {
            float da = a.Location.Dist(ball), db = b.Location.Dist(ball);
            if (MathF.Abs(da - db) > 20) return da < db;
            float leftA = -Field.Side(team) * a.Location.x, leftB = -Field.Side(team) * b.Location.x;
            if (MathF.Abs(leftA - leftB) > 1) return leftA > leftB;
            return a.Index < b.Index;
        }

        public static float GroundEta(Car car)
        {
            if (car.IsDemolished) return 6;
            if (car.Location.Dist(Ball.Location) < 180 && (car.Velocity - Ball.Velocity).Length() < 600) return 0.05f;
            float next = Game.Time + 0.1f;
            BallSlice[] slices = Ball.Prediction.Slices;
            if (slices != null)
                foreach (BallSlice slice in slices)
                {
                    if (slice == null || slice.Time < next) continue;
                    float t = slice.Time - Game.Time;
                    if (t > 3.5f) break;
                    next = slice.Time + 0.15f;
                    // This is a ground-race estimate, not an aerial feasibility proof.
                    if (slice.Location.z > 300) continue;
                    float eta = Drive.GetEta(car, slice.Location);
                    if (float.IsFinite(eta) && eta <= t) return t;
                }
            float fallback = Drive.GetEta(car, Ball.Location);
            return float.IsFinite(fallback) ? System.Math.Clamp(fallback, 0.05f, 6) : 6;
        }

        public static TacticalFrame Evaluate(RUBot bot)
        {
            var result = new TacticalFrame { MyEta = GroundEta(bot.Me), FirstMan = bot.Index, LastBack = true };
            float best = result.MyEta;
            int side = Field.Side(bot.Team);
            foreach (Car car in Cars.AllCars)
            {
                if (car.IsDemolished || car.Index == bot.Index) continue;
                float eta = GroundEta(car);
                if (car.Team != bot.Team) { result.OpponentEta = MathF.Min(result.OpponentEta, eta); continue; }
                result.TeammateEta = MathF.Min(result.TeammateEta, eta);
                if (car.Location.y * side > bot.Me.Location.y * side + 100) result.LastBack = false;
                if (WinsTie(eta, car.Index, best, result.FirstMan)) { best = eta; result.FirstMan = car.Index; }
            }
            return result;
        }

        public static Vec3 ShadowTarget(Vec3 ball, Vec3 ownGoal, bool lastBack)
        {
            Vec3 target = ball + ControlMath.FlatUnit(ownGoal - ball, new Vec3(0, ownGoal.y < 0 ? -1 : 1, 0)) *
                (lastBack ? 1400 : 1000);
            float side = ownGoal.y < 0 ? -1 : 1;
            if (ball.y * side > 3400)
                target = new Vec3(ball.x > 0 ? -750 : 750, side * 4700, 17);
            return new Vec3(System.Math.Clamp(target.x, -3300, 3300), System.Math.Clamp(target.y, -4800, 4800), 17);
        }

        /// <summary>Bounded search. Expensive shot solvers run at tactical cadence, never at 120 Hz.</summary>
        public static Shot SelectShot(RUBot bot, bool emergency, float opponentEta, Func<float, bool> claimed)
        {
            BallSlice[] slices = Ball.Prediction.Slices;
            if (slices == null || slices.Length == 0) return null;
            Target target = new Target(emergency ? bot.OurGoal : bot.TheirGoal, emergency);
            float next = Game.Time + 0.08f, bestScore = float.NegativeInfinity;
            int evaluated = 0;
            Shot best = null;
            foreach (BallSlice slice in slices)
            {
                if (slice == null || slice.Time < next) continue;
                float t = slice.Time - Game.Time;
                if (t > 3 || evaluated >= 48) break;
                next = slice.Time + 0.06f;
                evaluated++;
                if (!target.Fits(slice.Location) || (!emergency && claimed(slice.Time))) continue;
                if (!emergency && opponentEta < 1.5f && t > opponentEta + 0.35f) continue;
                Ball after = slice.ToBall();
                Vec3 approach = (slice.Location - bot.Me.Location) / t;
                after.velocity = approach.Cap(0, Car.MaxSpeed) + slice.Velocity * 0.25f;
                Vec3 destination = target.Clamp(after);
                if (!ControlMath.Finite(destination)) continue;
                Shot candidate = new GroundShot(bot.Me, slice, destination);
                float cost = 0;
                if (!candidate.IsValid(bot.Me)) { candidate = new JumpShot(bot.Me, slice, destination); cost = 0.15f; }
                if (!candidate.IsValid(bot.Me)) { candidate = new DoubleJumpShot(bot.Me, slice, destination); cost = 0.4f; }
                if (!candidate.IsValid(bot.Me)) { candidate = new AerialShot(bot.Me, slice, destination); cost = 0.8f; }
                if (!candidate.IsValid(bot.Me)) continue;
                float score = -t - cost - MathF.Max(0, t - opponentEta) * (emergency ? 0 : 2);
                if (score > bestScore) { best = candidate; bestScore = score; }
                if (best != null && t > best.Slice.Time - Game.Time + 0.3f) break;
            }
            return best;
        }
    }
}
