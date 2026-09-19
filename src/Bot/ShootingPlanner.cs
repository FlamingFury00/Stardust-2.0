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

        public static Shot Select(RUBot bot, bool emergency, float opponentEta, Func<float, bool> claimed)
        {
            BallSlice[] slices = Ball.Prediction.Slices;
            if (slices == null || slices.Length == 0) return null;
            Target target = new Target(emergency ? bot.OurGoal : bot.TheirGoal, emergency);
            float deadline = emergency ? Tactics.GoalThreat(slices, bot.OurGoal.Location, Game.Time, 3) : 3;
            deadline = float.IsFinite(deadline) ? MathF.Min(3, deadline) : 2;
            float next = Game.Time + 0.05f, bestScore = float.NegativeInfinity;
            int evaluated = 0, solved = 0;
            Shot best = null;
            var opponents = bot.LivingOpponents;
            foreach (BallSlice slice in slices)
            {
                if (slice == null || slice.Time < next) continue;
                float t = slice.Time - Game.Time;
                if (t > deadline || evaluated >= 96 || solved >= 64 || MathF.Abs(slice.Location.y) > 5250) break;
                if (best != null && t > best.Slice.Time - Game.Time + 0.18f) break;
                if (bot.Me.Location.Dist(slice.Location) > Car.MaxSpeed * t + 180) continue;
                next = slice.Time + (t < 0.8f ? 1f / 60 : 1f / 30);
                evaluated++;
                if (!target.Fits(slice.Location) || (!emergency && claimed != null && claimed(slice.Time))) continue;
                if (!emergency && opponentEta < 1.5f && t > opponentEta + 0.25f) continue;
                Vec3 destination;
                if (emergency)
                {
                    Ball after = slice.ToBall();
                    Vec3 approach = ((slice.Location - bot.Me.Location) / t).Cap(0, Car.MaxSpeed);
                    after.velocity = approach + slice.Velocity.Flatten(ControlMath.Unit(approach, bot.Me.Forward)) * 0.8f;
                    destination = target.Clamp(after);
                }
                else destination = ScoringPoint(bot.Me, slice, bot.TheirGoal, opponents, Game.Time);
                if (!ControlMath.Finite(destination)) continue;
                Shot candidate = null;
                float cost = 0;
                Surface surface = Field.NearestSurface(slice.Location);
                float height = (slice.Location - surface.Limit(slice.Location)).Dot(surface.Normal);
                for (int kind = 0; kind < 4 && solved < 64; kind++)
                {
                    if (kind == 0 && height > 160) continue;
                    if (kind == 1 && height > 430) continue;
                    if (kind == 2 && (slice.Location.z < 180 || slice.Location.z > 670)) continue;
                    if (kind == 3)
                    {
                        bool canSpend = emergency || (bot.Me.Boost >= 25 &&
                            (!(bot is Stardust st) || st.Situation.HasCover || bot.LivingTeammates.Count == 0));
                        if (!canSpend || (bot.Me.IsGrounded && height < 300)) continue;
                    }
                    solved++;
                    Shot trial = kind switch
                    {
                        0 => new GroundShot(bot.Me, slice, destination),
                        1 => new JumpShot(bot.Me, slice, destination),
                        2 => new DoubleJumpShot(bot.Me, slice, destination),
                        _ => new AerialShot(bot.Me, slice, destination)
                    };
                    if (!trial.IsValid(bot.Me) || !HasSetupTime(trial, bot.Me, Game.Time)) continue;
                    if (emergency)
                    {
                        if (trial is GroundShot ground) ground.ArriveAction.AllowFlipping = false;
                        if (trial is JumpShot jump) jump.ArriveAction.AllowFlipping = false;
                        if (trial is DoubleJumpShot doubleJump) doubleJump.ArriveAction.AllowFlipping = false;
                    }
                    candidate = trial;
                    cost = kind switch { 0 => 0, 1 => 0.08f, 2 => 0.20f, _ => 0.4f };
                    break;
                }
                if (candidate == null) continue;
                float score = -1.5f * t - cost - MathF.Max(0, t - opponentEta) * (emergency ? 0 : 2);
                if (score > bestScore) { best = candidate; bestScore = score; }
                if (best != null && t > best.Slice.Time - Game.Time + 0.18f) break;
            }
            return best;
        }
    }
}
