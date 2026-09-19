using System;
using System.Collections.Generic;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public sealed class TacticalFrame
    {
        public float MyEta, OpponentEta = 6, TeammateEta = 6;
        public int FirstMan, Cover, TeamCount;
        public bool LastBack, HasCover;
        public TeamRole Role;
        public Vec3 SupportTarget;
        public float FreeTime => OpponentEta - MyEta;
    }

    public static class Tactics
    {
        public static float GoalThreat(BallSlice[] slices, Vec3 goal, float now, float horizon = 2.5f)
        {
            if (slices == null) return float.PositiveInfinity;
            float side = goal.y < 0 ? -1 : 1;
            BallSlice previous = null;
            foreach (BallSlice b in slices)
            {
                if (b == null || !float.IsFinite(b.Time) || !ControlMath.Finite(b.Location)) continue;
                if (b.Time < now) { previous = b; continue; }
                if (b.Time > now + horizon) break;
                if (b.Location.y * side >= MathF.Abs(goal.y))
                {
                    Vec3 crossing = b.Location;
                    float time = b.Time;
                    if (previous != null && previous.Location.y * side < MathF.Abs(goal.y))
                    {
                        float fraction = (goal.y - previous.Location.y) / (b.Location.y - previous.Location.y);
                        crossing = previous.Location + (b.Location - previous.Location) * fraction;
                        time = previous.Time + (b.Time - previous.Time) * fraction;
                    }
                    if (time >= now && MathF.Abs(crossing.x - goal.x) < Goal.Width / 2 + Ball.Radius &&
                        crossing.z >= -Ball.Radius && crossing.z < Goal.Height + Ball.Radius) return time - now;
                }
                previous = b;
            }
            return float.PositiveInfinity;
        }

        /// <summary>Lexicographic quantized ETA order is transitive. Pairwise epsilon ties are not.</summary>
        public static bool WinsTie(float eta, int index, float otherEta, int otherIndex, float margin = 0.08f)
        {
            double width = float.IsFinite(margin) && margin > 0 ? margin : 0.08;
            double a = float.IsFinite(eta) ? System.Math.Floor(eta / width) : double.PositiveInfinity;
            double b = float.IsFinite(otherEta) ? System.Math.Floor(otherEta / width) : double.PositiveInfinity;
            return a < b || (a == b && index < otherIndex);
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
            float next = Game.Time + 0.05f;
            BallSlice[] slices = Ball.Prediction.Slices;
            if (slices != null)
                foreach (BallSlice slice in slices)
                {
                    if (slice == null || slice.Time < next) continue;
                    float t = slice.Time - Game.Time;
                    if (t > 3.5f || MathF.Abs(slice.Location.y) > 5250) break;
                    next = slice.Time + 0.1f;
                    if (slice.Location.z > 300) continue;
                    float eta = Drive.GetEta(car, slice.Location, false);
                    if (float.IsFinite(eta) && eta <= t) return t;
                }
            float fallback = Drive.GetEta(car, Ball.Location, false);
            return float.IsFinite(fallback) ? System.Math.Clamp(fallback, 0.05f, 6) : 6;
        }

        public static TacticalFrame Evaluate(RUBot bot)
        {
            var etas = new Dictionary<int, float>();
            foreach (Car car in Cars.AllLivingCars) etas[car.Index] = GroundEta(car);
            TeamAssignment assignment = TeamShape.Assign(Cars.AllLivingCars, bot.Team, Ball.Location, car => etas[car.Index]);
            var result = new TacticalFrame { MyEta = etas.TryGetValue(bot.Index, out float own) ? own : 6,
                FirstMan = assignment.FirstMan, Cover = assignment.Cover, LastBack = assignment.LastBack == bot.Index,
                TeamCount = assignment.Count, HasCover = TeamShape.HasCover(Cars.AllLivingCars, bot.Me, Ball.Location, bot.Team),
                Role = assignment.Roles.TryGetValue(bot.Index, out TeamRole role) ? role : TeamRole.Cover,
                SupportTarget = assignment.Targets.TryGetValue(bot.Index, out Vec3 target) ? target :
                    TeamShape.Target(Ball.Location, bot.OurGoal.Location, TeamRole.Cover) };
            foreach (Car car in Cars.AllLivingCars)
            {
                if (car.Index == bot.Index) continue;
                if (car.Team != bot.Team) result.OpponentEta = MathF.Min(result.OpponentEta, etas[car.Index]);
                else result.TeammateEta = MathF.Min(result.TeammateEta, etas[car.Index]);
            }
            return result;
        }

        // Compatibility helper: even deep defense must give the two roles different locations.
        public static Vec3 ShadowTarget(Vec3 ball, Vec3 ownGoal, bool lastBack) =>
            TeamShape.Target(ball, ownGoal, lastBack ? TeamRole.Cover : TeamRole.Support);

        public static Shot SelectShot(RUBot bot, bool emergency, float opponentEta, Func<float, bool> claimed) =>
            ShotPlanner.Select(bot, emergency, opponentEta, claimed);
    }
}
