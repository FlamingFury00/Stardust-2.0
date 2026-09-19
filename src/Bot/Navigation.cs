using System;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;

namespace Bot
{
    /// <summary>Goal-safe, braking-aware support driving. Never performs a speculative dodge.</summary>
    public static class Navigation
    {
        public static Vec3 FieldTarget(Vec3 target) => ControlMath.Finite(target)
            ? new Vec3(System.Math.Clamp(target.x, -3500, 3500), System.Math.Clamp(target.y, -4450, 4450), 17)
            : new Vec3(0, 0, 17);

        public static float ArrivalSpeed(float distance, float maximum, bool stop)
        {
            if (!float.IsFinite(distance) || !float.IsFinite(maximum)) return 0;
            return MathF.Min(MathF.Max(0, maximum), MathF.Sqrt(2 * 1800 * MathF.Max(0, distance - (stop ? 90 : 0))) + (stop ? 0 : 550));
        }

        public static ControllerStateT Controls(Car car, Vec3 destination, Vec3 faceTarget, int team,
            bool urgent, float reserve = 25, bool stop = true)
        {
            var output = new ControllerStateT();
            if (!ControlMath.Finite(car.Location) || !ControlMath.Finite(car.Velocity) || car.IsDemolished) return output;
            destination = FieldTarget(destination);
            Vec3 waypoint = DrivingSafety.GoalWaypoint(car.Location, destination);
            bool staging = waypoint.FlatDist(destination) > 100;
            Vec3 direction = waypoint - car.Location;
            float distance = direction.FlatLen();
            float forwardSpeed = car.Velocity.Dot(car.Forward);
            Vec3 local = car.Local(ControlMath.FlatUnit(direction, car.Forward));
            float angle = MathF.Atan2(local.y, local.x);
            bool nearOwnNet = car.Location.y * Field.Side(team) > 4000;
            bool backwards = distance < (nearOwnNet ? 3200 : 1400) &&
                MathF.Abs(angle) > (nearOwnNet ? 1.6f : 2.3f) && (forwardSpeed < -100 || MathF.Abs(forwardSpeed) < 500);
            if (backwards) angle = MathF.Atan2(-local.y, -local.x);
            float maximum = urgent ? 2300 : car.Boost > reserve + 15 ? 1800 : 1400;
            float speed = ArrivalSpeed(distance, maximum, stop && !staging);
            speed = MathF.Min(speed, MathF.Abs(angle) > 1.3f ? 450 : MathF.Abs(angle) > 0.65f ? 900 : maximum);
            if (backwards) speed = MathF.Min(speed, 900);
            // Turn toward play at low speed instead of arriving pointed deep into our net.
            if (stop && !staging && distance < 320 && MathF.Abs(forwardSpeed) < 220)
            {
                Vec3 facing = car.Local(ControlMath.FlatUnit(faceTarget - car.Location, car.Forward));
                angle = MathF.Atan2(facing.y, facing.x);
                backwards = false;
                speed = MathF.Abs(angle) > 0.55f ? 140 : 0;
            }
            output.Steer = ControlRuntime.Axis((3.2f * angle - 0.3f * car.LocalAngularVelocity.z) * (backwards ? -1 : 1));
            output.Throttle = ControlRuntime.Axis(((backwards ? -speed : speed) - forwardSpeed) / 200);
            output.Boost = !backwards && output.Throttle > 0.99f && MathF.Abs(angle) < 0.18f &&
                speed > 1450 && speed - forwardSpeed > 180 && forwardSpeed < 2200 &&
                float.IsFinite(car.Boost) && car.Boost > (urgent ? 0 : reserve) && distance > 500;
            // Longitudinal stopping distance includes a reaction margin. Brake before crossing the goal line.
            float side = Field.Side(team), inward = car.Velocity.y * side;
            float room = 4650 - car.Location.y * side;
            if (MathF.Abs(car.Location.x) < 1650 && inward > 100 &&
                inward * inward / (2 * Car.BrakeAccel) + inward * 0.12f > MathF.Max(0, room))
            {
                output.Throttle = -MathF.Sign(forwardSpeed);
                output.Boost = false;
            }
            output.Handbrake = false;
            return output;
        }
    }

    /// <summary>Use the original Drive for travel; brake locally for parking and goal geometry.</summary>
    public sealed class Positioning : IAction
    {
        public Vec3 Target;
        public bool Urgent, Stop = true;
        // Kept for local parking control. A reserve must not cap full-field travel speed.
        public float Reserve = 25;
        private Drive travelDrive;
        public bool Finished => false;
        public bool Interruptible => travelDrive?.Interruptible ?? true;
        public Positioning(Vec3 target, bool urgent = false) { Target = target; Urgent = urgent; }
        public void Run(RUBot bot)
        {
            Car car = bot.Me;
            // A speed flip necessarily leaves the ground. Complete the SAME subaction;
            // replacing it with Recover here cancels the mechanic halfway through.
            if (travelDrive?.Action != null && !travelDrive.Action.Finished)
            {
                travelDrive.Run(bot);
                return;
            }
            if (!car.IsGrounded) { bot.Action = new Recover(); return; }
            Vec3 destination = Navigation.FieldTarget(Target);
            Vec3 waypoint = DrivingSafety.GoalWaypoint(car.Location, destination);
            float distance = car.Location.FlatDist(waypoint);
            float speed = car.Velocity.FlatLen();
            float brakingZone = MathF.Max(450, speed * speed / (2 * 1800) + speed * 0.16f + 90);
            bool localArrival = Stop && distance < brakingZone;
            bool mouth = DrivingSafety.NearGoalMouth(car.Location);
            bool staged = waypoint.FlatDist(destination) > 100;
            if (car.Up.z > 0.75f && (localArrival || mouth || staged))
            {
                travelDrive = null;
                bot.Controller = Navigation.Controls(car, destination, Ball.Location, bot.Team, Urgent, Reserve, Stop);
                return;
            }
            if (travelDrive == null || travelDrive.Finished)
                travelDrive = new Drive(car, waypoint, Car.MaxSpeed, allowDodges: true, wasteBoost: false);
            travelDrive.Target = waypoint;
            travelDrive.TargetSpeed = Car.MaxSpeed;
            // The existing Drive checks speed, alignment, field boundaries and recovery
            // time. Add only a LOCAL arrival/mouth margin, never a blanket role ban.
            Vec3 landing = car.LocationAfterDodge();
            travelDrive.AllowDodges = distance > MathF.Max(1900, (speed + 500) * 1.35f + 350) &&
                !DrivingSafety.NearGoalMouth(landing) && !staged;
            travelDrive.WasteBoost = Urgent;
            travelDrive.Run(bot);
        }
    }
}
