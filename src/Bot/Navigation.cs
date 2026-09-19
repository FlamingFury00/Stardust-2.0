using System;
using System.Collections.Generic;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;

namespace Bot
{
    /// <summary>Ground-only positioning geometry. Shot executors retain their own contact paths.</summary>
    public static class Navigation
    {
        public static Vec3 FieldTarget(Vec3 target, bool urgent = false)
        {
            if (!ControlMath.Finite(target)) target = Vec3.Zero;
            float y = System.Math.Clamp(target.y, urgent ? -4940 : -4600, urgent ? 4940 : 4600);
            float limitX = MathF.Min(3600, Field.CornerIntersection - 350 - MathF.Abs(y));
            return new Vec3(System.Math.Clamp(target.x, -limitX, limitX), y, 17);
        }
        public static Vec3 Waypoint(Vec3 car, Vec3 target, bool urgent = false)
        {
            Vec3 destination = FieldTarget(target, urgent);
            float side = car.y < 0 ? -1 : 1;
            float depth = MathF.Abs(car.y);
            // Get through the aperture BEFORE traversing sideways. Do not cut through a post.
            if (depth > 5100 && MathF.Abs(car.x) < Goal.Width / 2)
            {
                if (MathF.Abs(car.x) > 600) return new Vec3(MathF.Sign(car.x) * 480, car.y, 17);
                return new Vec3(System.Math.Clamp(destination.x, -480, 480), side * 4600, 17);
            }
            // A car outside the aperture must come forward before moving across the goal mouth.
            if (depth > 4700 && MathF.Abs(car.x) >= 650) return new Vec3(car.x, side * 4400, 17);
            return destination;
        }
        public static float ArrivalSpeed(float distance, float maximum) =>
            MathF.Min(MathF.Max(0, maximum), MathF.Sqrt(2 * 2300 * MathF.Max(0, distance - 85)));
        public static ControllerStateT Control(Car car, Vec3 waypoint, float maximum, bool stop,
            bool urgent, float reserve = 25)
        {
            Vec3 delta = (waypoint - car.Location).Flatten();
            float distance = delta.Length();
            Vec3 local = car.Local(ControlMath.Unit(delta, car.Forward));
            float angle = MathF.Atan2(local.y, local.x);
            bool reverse = MathF.Abs(angle) > 2.4f && distance < 1100;
            float steeringAngle = reverse ? MathF.Atan2(-local.y, -local.x) : angle;
            float desired = MathF.Min(maximum, 2300 / (1 + 1.8f * MathF.Abs(steeringAngle)));
            if (stop) desired = MathF.Min(desired, ArrivalSpeed(distance, maximum));
            if (stop && distance < 90) desired = 0;
            if (reverse) desired = -MathF.Min(desired, 900);
            float speed = car.Velocity.Dot(car.Forward);
            var controls = new ControllerStateT
            {
                Steer = ControlRuntime.Axis((3.2f * steeringAngle - 0.25f * car.LocalAngularVelocity.z) * (reverse ? -1 : 1)),
                Throttle = ControlRuntime.Axis((desired - speed) / 220),
                Handbrake = false,
                Jump = false
            };
            controls.Boost = !reverse && MathF.Abs(angle) < 0.14f && controls.Throttle > 0.95f &&
                desired > 1450 && speed < desired - 120 && speed < 2200 && distance > 450 &&
                float.IsFinite(car.Boost) && car.Boost > (urgent ? 0 : reserve);
            return controls;
        }
        public static bool CollisionCourse(Car me, Car other)
        {
            Vec3 delta = (other.Location - me.Location).Flatten();
            Vec3 velocity = (other.Velocity - me.Velocity).Flatten();
            float squaredSpeed = velocity.Dot(velocity);
            if (delta.Length() > 900 || squaredSpeed < 1) return false;
            float time = System.Math.Clamp(-delta.Dot(velocity) / squaredSpeed, 0, 0.65f);
            return time > 0 && (delta + velocity * time).Length() < 220;
        }
    }
    /// <summary>Persistent arrival-aware navigation, without travel dodges or goal-area drifting.</summary>
    public sealed class Navigate : IAction
    {
        public Vec3 Target;
        public float Speed;
        public bool Urgent, StopAtTarget = true;
        public bool Finished => false;
        public bool Interruptible => true;
        private Drive surfaceDrive;
        public Navigate(Vec3 target, float speed = 1410, bool urgent = false)
        { Target = target; Speed = speed; Urgent = urgent; }
        public void Run(RUBot bot)
        {
            Car car = bot.Me;
            Vec3 waypoint = Navigation.Waypoint(car.Location, Target, Urgent);
            if (!car.IsGrounded || car.Up.z < 0.7f)
            {
                surfaceDrive ??= new Drive(car, waypoint, Speed, allowDodges: false, wasteBoost: false);
                surfaceDrive.Target = waypoint;
                surfaceDrive.TargetSpeed = MathF.Max(100, Speed);
                surfaceDrive.Run(bot);
                bot.Controller.Boost = false;
                return;
            }
            float speed = Speed;
            if (!Urgent)
                foreach (Car other in bot.LivingTeammates)
                    if (car.Index > other.Index && Navigation.CollisionCourse(car, other)) speed = MathF.Min(speed, 500);
            bot.Controller = Navigation.Control(car, waypoint, speed, StopAtTarget, Urgent);
            if (StopAtTarget && car.Location.FlatDist(waypoint) < 100 && car.Velocity.FlatLen() < 120)
            {
                Vec3 local = car.Local(Ball.Location - car.Location);
                bot.Controller.Steer = ControlRuntime.Axis(2 * MathF.Atan2(local.y, local.x));
                bot.Controller.Boost = false;
            }
        }
    }
}
