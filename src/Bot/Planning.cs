using System;
using System.Collections.Generic;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public sealed class TacticalFrame
    {
        public float MyEta, OpponentEta = 6, TeammateEta = 6;
        public int FirstMan;
        public bool LastBack, HasCover;
        public TeamRole Role;
        public Vec3 SupportTarget;
        public TeamPlan Allocation = new();
        public float FreeTime => OpponentEta - MyEta;
    }

    public static class Tactics
    {
        public static float GoalThreat(BallSlice[] slices, Vec3 goal, float now, float horizon = 2.5f)
        {
            if (slices == null) return float.PositiveInfinity;
            float side = goal.y < 0 ? -1 : 1;
            foreach (BallSlice b in slices)
            {
                if (b == null || b.Time < now || !ControlMath.Finite(b.Location)) continue;
                if (b.Time > now + horizon) break;
                if (b.Location.y * side >= MathF.Abs(goal.y) &&
                    MathF.Abs(b.Location.x - goal.x) < Goal.Width / 2 + Ball.Radius &&
                    b.Location.z >= 0 && b.Location.z < Goal.Height + Ball.Radius) return b.Time - now;
            }
            return float.PositiveInfinity;
        }

        public static bool UnderPressure(Ball ball, Vec3 goal, float opponentEta)
        {
            float side = goal.y < 0 ? -1 : 1;
            float depth = ball.location.y * side;
            return (depth > 1200 && (ball.velocity.y * side > 350 || opponentEta < 1.25f)) ||
                (depth > 3200 && MathF.Abs(ball.location.x) < 1900);
        }

        public static float FastGoalThreat(Ball ball, Vec3 goal)
        {
            float side = goal.y < 0 ? -1 : 1;
            if (ball.velocity.y * side < 300) return float.PositiveInfinity;
            float t = (goal.y - ball.location.y) / ball.velocity.y;
            if (t < 0 || t > 2.2f) return float.PositiveInfinity;
            float x = ball.location.x + ball.velocity.x * t;
            float z = MathF.Max(Ball.Radius, ball.location.z + ball.velocity.z * t + Game.Gravity.z * t * t / 2);
            return MathF.Abs(x) < Goal.Width / 2 && z < Goal.Height ? t : float.PositiveInfinity;
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
            if (car.Location.Dist(Ball.Location) < 220 && (car.Velocity - Ball.Velocity).Length() < 600) return 0.05f;
            float next = Game.Time + 0.1f;
            BallSlice[] slices = Ball.Prediction.Slices;
            if (slices != null)
                foreach (BallSlice slice in slices)
                {
                    if (slice == null || slice.Time < next) continue;
                    float t = slice.Time - Game.Time;
                    if (t > 3) break;
                    next = slice.Time + 0.15f;
                    if (slice.Location.z > 300) continue;
                    float eta = TeamPlanning.EstimateEta(car, slice.Location);
                    if (eta <= t) return t;
                }
            return TeamPlanning.EstimateEta(car, Ball.Location);
        }

        /// <summary>Choose a defender against the incoming trajectory, not the current ball's ground race.</summary>
        public static int SaveOwner(IEnumerable<Car> cars, int team, BallSlice[] slices, float now, float deadline)
        {
            if (slices == null) return -1;
            int best = -1;
            float bestTime = float.PositiveInfinity, bestEta = float.PositiveInfinity;
            foreach (Car car in cars)
            {
                if (car == null || car.Team != team || car.IsDemolished) continue;
                float next = now + 0.05f;
                foreach (BallSlice slice in slices)
                {
                    if (slice == null || slice.Time < next) continue;
                    float t = slice.Time - now;
                    if (t >= deadline || t > 3) break;
                    next = slice.Time + 0.06f;
                    if (slice.Location.z > 650 || slice.Location.y * Field.Side(team) > 5100) continue;
                    float eta = TeamPlanning.EstimateEta(car, slice.Location);
                    float vertical = slice.Location.z > 250 ? 0.2f + (slice.Location.z - 250) / 1100 : 0;
                    if (car.IsGrounded && vertical > t) continue;
                    if (eta > t) continue;
                    if (best < 0 || (int)(t / 0.04f) < (int)(bestTime / 0.04f) ||
                        ((int)(t / 0.04f) == (int)(bestTime / 0.04f) &&
                         ((int)(eta / 0.04f) < (int)(bestEta / 0.04f) ||
                          ((int)(eta / 0.04f) == (int)(bestEta / 0.04f) && car.Index < best))))
                    { best = car.Index; bestTime = t; bestEta = eta; }
                    break;
                }
            }
            return best;
        }

        public static TacticalFrame Evaluate(RUBot bot)
        {
            var cars = Cars.AllLivingCars;
            var times = new Dictionary<int, float>();
            foreach (Car car in cars) times[car.Index] = GroundEta(car);
            float threat = GoalThreat(Ball.Prediction.Slices, bot.OurGoal.Location, Game.Time, 3);
            int defender = float.IsFinite(threat) ? SaveOwner(cars, bot.Team, Ball.Prediction.Slices, Game.Time, threat) : -1;
            var allocation = TeamPlanning.Assign(cars, Ball.MainBall, bot.Team, c => times[c.Index], defender);
            var result = new TacticalFrame
            {
                MyEta = times.TryGetValue(bot.Index, out float eta) ? eta : 6,
                FirstMan = allocation.Challenger,
                LastBack = allocation.Anchor == bot.Index,
                HasCover = allocation.HasCover,
                Role = allocation.Roles.TryGetValue(bot.Index, out TeamRole role) ? role : TeamRole.Anchor,
                SupportTarget = allocation.Targets.TryGetValue(bot.Index, out Vec3 target) ? target : ShadowTarget(Ball.Location, bot.OurGoal.Location, true),
                Allocation = allocation
            };
            foreach (Car car in cars)
            {
                if (car.Index == bot.Index) continue;
                if (car.Team != bot.Team) result.OpponentEta = MathF.Min(result.OpponentEta, times[car.Index]);
                else result.TeammateEta = MathF.Min(result.TeammateEta, times[car.Index]);
            }
            return result;
        }

        public static Vec3 ShadowTarget(Vec3 ball, Vec3 ownGoal, bool lastBack)
        {
            float side = ownGoal.y < 0 ? -1 : 1;
            if (ball.y * side > 3400)
                return new Vec3(ball.x > 0 ? -550 : 550, side * 4350, 17);
            Vec3 target = ball + ControlMath.FlatUnit(ownGoal - ball, new Vec3(0, side, 0)) * (lastBack ? 1700 : 1000);
            return Navigation.FieldTarget(target);
        }

        public static Shot SelectShot(RUBot bot, bool emergency, float opponentEta, Func<float, bool> claimed) =>
            ShootingPlanner.Select(bot, emergency, opponentEta, claimed);
    }
}
