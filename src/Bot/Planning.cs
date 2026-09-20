using System;
using System.Collections.Generic;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public sealed class TacticalFrame
    {
        public float MyEta, OpponentEta = 6, TeammateEta = 6;
        public float PressureTime = float.PositiveInfinity;
        public int FirstMan, TeamRank, TeamCount = 1;
        public bool LastBack;
        public float FreeTime => OpponentEta - MyEta;
        public bool UnderPressure => float.IsFinite(PressureTime);
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

        /// <summary>
        /// Deterministic total order for race ownership. Pairwise epsilon comparisons are non-transitive:
        /// A can tie B, B can tie C, while C still beats A. Fixed ETA buckets preserve a stable tie band
        /// without allowing different observers to elect different winners from the same snapshot.
        /// </summary>
        public static bool WinsTie(float eta, int index, float otherEta, int otherIndex, float margin = 0.08f)
        {
            float bucketSize = MathF.Max(margin, 0.001f);
            long bucket = float.IsFinite(eta) ? (long)MathF.Floor(MathF.Max(0, eta) / bucketSize) : long.MaxValue;
            long otherBucket = float.IsFinite(otherEta) ? (long)MathF.Floor(MathF.Max(0, otherEta) / bucketSize) : long.MaxValue;
            return bucket != otherBucket ? bucket < otherBucket : index < otherIndex;
        }

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
            foreach (Car car in Cars.AllLivingCars)
            {
                if (car.Index == bot.Index) continue;
                float eta = GroundEta(car);
                if (car.Team != bot.Team)
                {
                    result.OpponentEta = MathF.Min(result.OpponentEta, eta);
                    continue;
                }

                result.TeamCount++;
                result.TeammateEta = MathF.Min(result.TeammateEta, eta);
                if (car.Location.y * side > bot.Me.Location.y * side + 100) result.LastBack = false;
                if (WinsTie(eta, car.Index, result.MyEta, bot.Index)) result.TeamRank++;
                if (WinsTie(eta, car.Index, best, result.FirstMan))
                {
                    best = eta;
                    result.FirstMan = car.Index;
                }
            }
            return result;
        }

        /// <summary>
        /// Distinct second-man and anchor lanes. The anchor stays outside the goal and uses the far-post
        /// side; support stays higher and laterally offset so two non-challengers cannot stack.
        /// </summary>
        public static Vec3 ShadowTarget(Vec3 ball, Vec3 ownGoal, bool lastBack)
        {
            float side = ownGoal.y < 0 ? -1 : 1;
            Vec3 goalward = ControlMath.FlatUnit(ownGoal - ball, new Vec3(0, side, 0));
            Vec3 target;
            if (ball.y * side > 3000)
            {
                if (lastBack)
                {
                    float postX = MathF.Abs(ball.x) < 150 ? 0 : -MathF.Sign(ball.x) * 700;
                    target = new Vec3(postX, side * 4400, 17);
                }
                else
                {
                    float supportX = System.Math.Clamp(ball.x * 0.35f, -1200, 1200);
                    target = new Vec3(supportX, side * 3250, 17);
                }
            }
            else if (lastBack)
            {
                float postBias = MathF.Abs(ball.x) < 250 ? 0 : -MathF.Sign(ball.x) * 450;
                target = ball + goalward * 1900 + Vec3.X * postBias;
            }
            else
            {
                float lane = MathF.Abs(ball.x) > 150 ? -MathF.Sign(ball.x) : 1;
                target = ball + goalward * 900 + Vec3.X * lane * 700;
            }
            return new Vec3(System.Math.Clamp(target.x, -3200, 3200),
                System.Math.Clamp(target.y, -4500, 4500), 17);
        }

        /// <summary>
        /// Short-horizon pre-contact threat estimate. RLBot's ball prediction intentionally contains no
        /// future car collisions, so use player intent only to decide whether an opponent is about to
        /// create a new ball path; actual post-touch defense still follows refreshed ball prediction.
        /// </summary>
        public static float OpponentPressure(IEnumerable<Car> opponents, Ball ball, Vec3 ownGoal,
            float maxContactTime = 1.35f)
        {
            if (opponents == null || ball == null || !ControlMath.Finite(ball.location))
                return float.PositiveInfinity;

            float earliest = float.PositiveInfinity;
            Vec3 attackDirection = ControlMath.FlatUnit(ownGoal - ball.location,
                new Vec3(0, ownGoal.y < 0 ? -1 : 1, 0));

            foreach (Car opponent in opponents)
            {
                if (opponent == null || opponent.IsDemolished || !ControlMath.Finite(opponent.Location))
                    continue;

                Vec3 toBall = ControlMath.FlatUnit(ball.location - opponent.Location, opponent.Forward);
                float attackAlignment = toBall.Dot(attackDirection);
                if (attackAlignment < 0.35f) continue;

                float facing = opponent.Forward.FlatNorm().Dot(toBall);
                float closing = (opponent.Velocity - ball.velocity).Dot(toBall);
                var input = opponent.LastInput ?? new RLBot.Flat.ControllerStateT();
                bool forwardIntent = input.Boost || input.Throttle > 0.2f || closing > 450;
                if (!forwardIntent || (facing < 0.4f && closing < 500)) continue;

                float eta = Drive.GetEta(opponent, ball.location);
                if (!opponent.IsGrounded && closing > 100)
                    eta = MathF.Min(eta, opponent.Location.Dist(ball.location) / closing);
                if (float.IsFinite(eta) && eta >= 0 && eta <= maxContactTime)
                    earliest = MathF.Min(earliest, eta);
            }
            return earliest;
        }

        /// <summary>
        /// Braking-aware speed for defensive parking. Rocket League braking is ~3500 uu/s^2;
        /// leave a buffer so the car reaches the guard point under control instead of crossing it.
        /// </summary>
        public static float GuardSpeed(Car car, Vec3 target, float cruiseSpeed)
        {
            if (car == null || !ControlMath.Finite(target) || !float.IsFinite(cruiseSpeed))
                return 350;

            float distance = Field.LimitToNearestSurface(car.Location).Dist(Field.LimitToNearestSurface(target));
            float usable = MathF.Max(0, distance - 120);
            float brakingSpeed = MathF.Sqrt(2 * Car.BrakeAccel * usable);
            return System.Math.Clamp(brakingSpeed, 350, MathF.Max(350, cruiseSpeed));
        }

        /// <summary>
        /// A car already behind the goal line must first leave through the central mouth before it
        /// is asked to park elsewhere. This avoids diagonal post cuts and driving deeper into net.
        /// </summary>
        public static Vec3 GoalReturnTarget(Car car, Vec3 desiredGuard, Vec3 ownGoal)
        {
            if (car == null || !ControlMath.Finite(desiredGuard) || !ControlMath.Finite(ownGoal))
                return desiredGuard;

            float side = ownGoal.y < 0 ? -1 : 1;
            bool behindLine = car.Location.y * side > MathF.Abs(ownGoal.y) + 40;
            bool insideMouth = MathF.Abs(car.Location.x) < Goal.Width / 2 + 220;
            if (!behindLine || !insideMouth) return desiredGuard;

            float safeHalfWidth = Goal.Width / 2 - 160;
            float x = System.Math.Clamp(desiredGuard.x, -safeHalfWidth, safeHalfWidth);
            float y = side * (MathF.Abs(ownGoal.y) - 300);
            return new Vec3(x, y, 17);
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
