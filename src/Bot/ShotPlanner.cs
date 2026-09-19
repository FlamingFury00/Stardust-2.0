using System;
using System.Collections.Generic;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    /// <summary>Conservative shot selection. Geometric margins are not a calibrated scoring probability.</summary>
    public static class ShotPlanner
    {
        public const int CandidateBudget = 96;
        public static IEnumerable<BallSlice> Candidates(BallSlice[] slices, float now, float deadline)
        {
            if (slices == null || !float.IsFinite(now)) yield break;
            float next = now + 1f / 120f;
            int count = 0;
            foreach (BallSlice slice in slices)
            {
                if (slice == null || !float.IsFinite(slice.Time) || !ControlMath.Finite(slice.Location) ||
                    !ControlMath.Finite(slice.Velocity) || slice.Time < next) continue;
                if (slice.Time > deadline || slice.Time > now + 3 || MathF.Abs(slice.Location.y) > 5250 ||
                    count >= CandidateBudget) yield break;
                yield return slice;
                count++;
                float t = slice.Time - now;
                next = slice.Time + (t < 0.4f ? 0.007f : t < 1.4f ? 0.032f : 0.065f);
            }
        }

        public static Vec3 InteriorTarget(Vec3 target, Vec3 goal) => new(
            System.Math.Clamp(target.x, goal.x - 620, goal.x + 620), goal.y,
            System.Math.Clamp(target.z, 160, 450));

        public static bool DeadlineAllows(float intercept, float now, float goalTime) =>
            float.IsFinite(intercept) && intercept > now && intercept < now + goalTime - 0.035f;

        public static bool ContactFeasible(Shot shot, Car car, float remaining)
        {
            if (shot == null || remaining <= 0 || !ControlMath.Finite(shot.TargetLocation) ||
                !ControlMath.Finite(shot.ShotDirection) || !shot.IsValid(car)) return false;
            // GroundShot.IsValid used straight travel ETA while its actual executor uses an oriented arrival.
            // The executor's path, not merely distance to the ball, must fit the time budget.
            if (shot is GroundShot ground)
            {
                float arrival = ground.ArriveAction.Eta(car);
                return float.IsFinite(arrival) && arrival <= remaining + 0.01f;
            }
            return true;
        }

        public static Shot Select(RUBot bot, bool emergency, float opponentEta, Func<float, bool> claimed)
        {
            BallSlice[] slices = Ball.Prediction.Slices;
            float goalTime = emergency ? Tactics.GoalThreat(slices, bot.OurGoal.Location, Game.Time) : float.PositiveInfinity;
            float deadline = MathF.Min(Game.Time + 3, Game.Time + goalTime - 0.035f);
            Target region = new(emergency ? bot.OurGoal : bot.TheirGoal, emergency);
            Shot best = null;
            float bestScore = float.NegativeInfinity;
            foreach (BallSlice slice in Candidates(slices, Game.Time, deadline))
            {
                float t = slice.Time - Game.Time;
                if (best != null && t > best.Slice.Time - Game.Time + (emergency ? 0.10f : 0.22f)) break;
                if (!region.Fits(slice.Location) || (!emergency && claimed != null && claimed(slice.Time))) continue;
                if (!emergency && opponentEta < 1.5f && t > opponentEta + 0.2f) continue;
                if (bot.Me.Location.Dist(slice.Location) > Car.MaxSpeed * t + 180) continue;
                Ball after = slice.ToBall();
                Vec3 approach = ((slice.Location - bot.Me.Location) / t).Cap(0, Car.MaxSpeed);
                // Restore the lateral-velocity model used by the original shot selector.
                after.velocity = approach + slice.Velocity.Flatten(ControlMath.Unit(approach, bot.Me.Forward)) * 0.8f;
                Vec3 destination = region.Clamp(after);
                if (!ControlMath.Finite(destination)) continue;
                if (!emergency) destination = InteriorTarget(destination, bot.TheirGoal.Location);
                Shot candidate = new GroundShot(bot.Me, slice, destination);
                float cost = 0;
                if (!ContactFeasible(candidate, bot.Me, t)) { candidate = new JumpShot(bot.Me, slice, destination); cost = 0.08f; }
                if (!ContactFeasible(candidate, bot.Me, t)) { candidate = new DoubleJumpShot(bot.Me, slice, destination); cost = 0.18f; }
                if (!ContactFeasible(candidate, bot.Me, t))
                {
                    // Optional offense should leave some fuel for the recovery. A save can spend the reserve.
                    Car budgetCar = new(bot.Me);
                    if (!emergency) budgetCar.Boost = MathF.Max(0, budgetCar.Boost - 12);
                    candidate = new AerialShot(budgetCar, slice, destination); cost = 0.32f;
                    if (!ContactFeasible(candidate, budgetCar, t)) continue;
                }
                if (!ContactFeasible(candidate, bot.Me, t)) continue;
                float approachAlignment = ControlMath.FlatUnit(slice.Location - bot.Me.Location, bot.Me.Forward)
                    .Dot(ControlMath.FlatUnit(candidate.ShotDirection, bot.Me.Forward));
                float score = -t - (emergency ? cost * 0.1f : cost + 0.15f * (1 - approachAlignment));
                if (!emergency) score -= 2 * MathF.Max(0, t - opponentEta);
                if (score > bestScore) { best = candidate; bestScore = score; }
            }
            return best;
        }

        public static bool OpenLane(Shot shot, IEnumerable<Car> opponents)
        {
            if (shot == null || shot.Slice.Time - Game.Time > 1.15f) return false;
            Vec3 a = shot.Slice.Location, b = shot.ShotTarget;
            Vec3 ab = (b - a).Flatten();
            float lengthSquared = ab.Dot(ab);
            if (lengthSquared < 1) return false;
            foreach (Car foe in opponents)
            {
                if (foe.IsDemolished) continue;
                float u = System.Math.Clamp((foe.Location - a).Dot(ab) / lengthSquared, 0, 1);
                if (u > 0.05f && foe.Location.FlatDist(a + ab * u) < 420) return false;
            }
            return true;
        }
    }
}
