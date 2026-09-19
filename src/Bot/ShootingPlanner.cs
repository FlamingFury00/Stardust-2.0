using System;
using System.Collections.Generic;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public static class ShootingPlanner
    {
        /// <summary>Aim inside the posts/crossbar, then prefer an unblocked lane over a tiny solver margin.</summary>
        public static Vec3 ScoringPoint(Car car, BallSlice slice, Goal goal, IEnumerable<Car> opponents, float now)
        {
            float t = MathF.Max(0.05f, slice.Time - now);
            Vec3 approach = ((slice.Location - car.Location) / t).Cap(0, Car.MaxSpeed);
            Ball after = slice.ToBall();
            // Restore the original lateral-velocity treatment; do not reduce all ball momentum to 25%.
            after.velocity = approach + slice.Velocity.Flatten(ControlMath.Unit(approach, car.Forward)) * 0.8f;
            Vec3 easy = new Target(goal).Clamp(after);
            float baseX = ControlMath.Finite(easy) ? System.Math.Clamp(easy.x, -580, 580) : 0;
            Vec3 best = new Vec3(baseX, goal.Location.y, 220);
            float score = float.PositiveInfinity;
            foreach (float x in new[] { baseX, 0, -500, 500 })
            {
                Vec3 aim = new Vec3(x, goal.Location.y, 220);
                Vec3 direction = ControlMath.FlatUnit(aim - slice.Location, car.Forward);
                float angle = 1 - direction.Dot(ControlMath.FlatUnit(slice.Location - car.Location, car.Forward));
                float cost = angle * 2 + MathF.Abs(x) / 6000;
                if (opponents != null)
                    foreach (Car opponent in opponents)
                    {
                        if (opponent == null || opponent.IsDemolished) continue;
                        Vec3 p = opponent.Location + opponent.Velocity * MathF.Min(t + 0.2f, 0.65f);
                        float along = (p - slice.Location).Dot(direction);
                        if (along > 0 && along < slice.Location.FlatDist(aim))
                            cost += MathF.Max(0, 1 - (p - slice.Location - direction * along).FlatLen() / 400) * 0.5f;
                    }
                if (cost < score) { score = cost; best = aim; }
            }
            return best;
        }

        public static bool HasSetupTime(Shot shot, Car car, float now)
        {
            float t = shot.Slice.Time - now;
            if (t <= 0.035f || !ControlMath.Finite(shot.TargetLocation) || !ControlMath.Finite(shot.ShotDirection)) return false;
            // All grounded shot families must have time for the setup waypoint they actually execute.
            Arrive arrive = shot switch
            {
                GroundShot ground => ground.ArriveAction,
                JumpShot jump => jump.ArriveAction,
                DoubleJumpShot doubleJump => doubleJump.ArriveAction,
                _ => null
            };
            if (arrive != null && car.IsGrounded)
            {
                Vec3 setup = arrive.ApproachTarget(car, arrive.Distance(car) / t);
                float eta = Drive.GetEta(car, setup, false) + setup.FlatDist(shot.TargetLocation) /
                    MathF.Max(1000, MathF.Min(1800, car.Velocity.FlatLen()));
                return float.IsFinite(eta) && eta + 0.025f <= t;
            }
            return true;
        }

        /// <summary>
        /// Restore the original Target.Clamp and shot-family validity checks. Scan the
        /// WHOLE horizon coarsely before refining the first success; do not spend the
        /// construction budget exclusively on early, unreachable slices.
        /// </summary>
        public static Shot Select(RUBot bot, bool emergency, float opponentEta, Func<float, bool> claimed)
        {
            BallSlice[] slices = Ball.Prediction.Slices;
            if (slices == null || slices.Length == 0) return null;
            Target target = new Target(emergency ? bot.OurGoal : bot.TheirGoal, emergency);
            float now = Game.Time;
            float threat = emergency ? Tactics.GoalThreat(slices, bot.OurGoal.Location, now, 3) : float.PositiveInfinity;
            float deadline = MathF.Min(3, threat);
            if (deadline <= 0.025f) return null;
            Shot At(BallSlice slice)
            {
                float t = slice.Time - now;
                if (t <= 0.025f || t >= deadline || !ControlMath.Finite(slice.Location) ||
                    !ControlMath.Finite(slice.Velocity) || MathF.Abs(slice.Location.y) > 5120 ||
                    bot.Me.Location.Dist(slice.Location) > Car.MaxSpeed * t + 180 ||
                    !target.Fits(slice.Location) || (!emergency && claimed != null && claimed(slice.Time))) return null;
                // Opponent ETA to the CURRENT ball is not a proof that every future
                // intercept is lost. In particular it must not disable all 1v1 challenges.
                Vec3 approach = ((slice.Location - bot.Me.Location) / t).Cap(0, Car.MaxSpeed);
                Ball after = slice.ToBall();
                after.velocity = approach + slice.Velocity.Flatten(ControlMath.Unit(approach, bot.Me.Forward)) * 0.8f;
                Vec3 destination = target.Clamp(after);
                if (!ControlMath.Finite(destination)) return null;
                // Original v5 family order and reachability contracts. The previously
                // added setup estimate is not an exact impossibility test and is no longer a veto.
                Shot candidate = new AerialShot(bot.Me, slice, destination);
                if (candidate.IsValid(bot.Me)) return candidate;
                candidate = new GroundShot(bot.Me, slice, destination);
                if (candidate.IsValid(bot.Me)) return candidate;
                candidate = new JumpShot(bot.Me, slice, destination);
                if (candidate.IsValid(bot.Me)) return candidate;
                candidate = new DoubleJumpShot(bot.Me, slice, destination);
                return candidate.IsValid(bot.Me) ? candidate : null;
            }
            Shot best = null;
            float next = now + 0.026f, previousTime = now + 0.025f;
            int coarse = 0;
            foreach (BallSlice slice in slices)
            {
                if (slice == null || !float.IsFinite(slice.Time) || slice.Time < next) continue;
                if (slice.Time - now >= deadline || coarse++ >= (emergency ? 88 : 32)) break;
                best = At(slice);
                if (best != null) break;
                previousTime = slice.Time;
                float t = slice.Time - now;
                next = slice.Time + (emergency && t < 0.35f ? 0.008f : emergency && t < 1 ? 0.025f : 0.095f);
            }
            if (best == null) return null;
            // At most 12 more checks at native prediction resolution around that hit.
            int fine = 0;
            foreach (BallSlice slice in slices)
            {
                if (slice == null || slice.Time <= previousTime) continue;
                if (slice.Time >= best.Slice.Time || fine++ >= 12) break;
                Shot refined = At(slice);
                if (refined != null) return refined;
            }
            return best;
        }
    }
}
