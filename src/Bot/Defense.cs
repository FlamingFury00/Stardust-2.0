using System;
using System.Collections.Generic;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;

namespace Bot
{
    public readonly struct DefensiveRead
    {
        public readonly float GoalTime;
        public readonly bool Pressure;
        public readonly Vec3 BlockTarget;
        public bool Emergency => float.IsFinite(GoalTime);
        public DefensiveRead(float goalTime, bool pressure, Vec3 target)
        { GoalTime = goalTime; Pressure = pressure; BlockTarget = target; }
    }

    public static class Defense
    {
        /// <summary>Cheap threat supervision runs each control frame, before the tactical timer.</summary>
        public static DefensiveRead Read(Ball ball, BallSlice[] slices, Vec3 goal, int team,
            IEnumerable<Car> cars, float now)
        {
            float goalTime = Tactics.GoalThreat(slices, goal, now);
            float side = Field.Side(team), depth = ball.location.y * side;
            bool incoming = ball.velocity.y * side > 350 && depth + ball.velocity.y * side * 0.9f > 1900;
            bool pressure = incoming || (depth > 3400 && MathF.Abs(ball.location.x) < 2700);
            foreach (Car foe in cars)
                if (foe.Team != team && !foe.IsDemolished && depth > 1200 &&
                    foe.Location.Dist(ball.location) < 650) pressure = true;
            Vec3 block = ball.location + ball.velocity * 0.3f;
            if (slices != null)
                foreach (BallSlice slice in slices)
                    if (slice != null && slice.Time >= now && slice.Time <= now + 2.5f &&
                        slice.Location.y * side > 4750)
                    { block = slice.Location; break; }
            block = new Vec3(System.Math.Clamp(block.x, -620, 620), side * 4800, 17);
            return new DefensiveRead(goalTime, pressure, block);
        }

        public static int Defender(IEnumerable<Car> cars, int team, Vec3 target,
            Func<Car, Vec3, float> eta = null)
        {
            eta ??= (car, point) => Drive.GetEta(car, point, false);
            float best = float.PositiveInfinity;
            int winner = -1;
            foreach (Car car in cars)
            {
                if (car.Team != team || car.IsDemolished) continue;
                float time = eta(car, target);
                if (!float.IsFinite(time)) time = 100;
                if (winner < 0 || Tactics.WinsTie(time, car.Index, best, winner))
                { winner = car.Index; best = time; }
            }
            return winner;
        }

        public static bool MayInterrupt(IAction action, JumpState jump)
        {
            if (action == null || action.Interruptible) return true;
            // An aerial action's broad "committed" flag is not the same as an active dodge/jump force.
            if (jump.State == AirState.Jumping || jump.State == AirState.DoubleJumping || jump.State == AirState.Dodging)
                return false;
            return action is AerialShot;
        }

        public static bool SaveStillTimely(Shot shot, float now, float goalTime) => shot != null &&
            ShotPlanner.DeadlineAllows(shot.Slice.Time, now, goalTime) && shot.IsPredictionValid();
    }
}
