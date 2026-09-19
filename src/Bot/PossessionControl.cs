using System;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;

namespace Bot
{
    public static class PossessionControl
    {
        /// <summary>Predict RELATIVE motion: equal world velocity must not create a fictitious hood error.</summary>
        public static ControllerStateT GroundCarry(Car car, Ball ball, Vec3 lane)
        {
            lane = ControlMath.FlatUnit(lane, car.Forward);
            Vec3 relativeVelocity = car.Local(ball.velocity - car.Velocity);
            Vec3 local = car.Local(ball.location - car.Location);
            Vec3 hoodError = car.Local(ball.location - car.Location +
                (ball.velocity - car.Velocity) * 0.10f - lane * 38);
            float lateral = System.Math.Clamp(hoodError.y * 4.5f + relativeVelocity.y * 0.7f, -650, 650);
            Vec3 heading = lane * 350 + car.Right * lateral;
            float speed = System.Math.Clamp(ball.velocity.Dot(car.Forward) + hoodError.x * 3.8f, -350, 1550);
            speed *= 1 - 0.35f * MathF.Min(1, MathF.Abs(local.y) / 160);
            var controls = new ControllerStateT();
            ControlMath.Ground(car, controls, heading, speed);
            return controls;
        }

        /// <summary>Both objects are advanced to the same look-ahead time before computing flight error.</summary>
        public static Vec3 FlightAtHorizon(Car car, Vec3 targetPosition, Vec3 targetVelocity, float horizon) =>
            ControlMath.FlightAcceleration(car.PredictLocation(horizon), car.PredictVelocity(horizon),
                targetPosition, targetVelocity, Game.Gravity);
    }
}
