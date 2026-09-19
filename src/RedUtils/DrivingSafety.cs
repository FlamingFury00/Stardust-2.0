using System;
using RedUtils.Math;

namespace RedUtils
{
    /// <summary>Shared geometric gates, independent of rendering and the RLBot connection.</summary>
    public static class DrivingSafety
    {
        public static float AimAngle(Car car, Vec3 target, bool backwards = false)
        {
            Vec3 local = car.Local(target - car.Location) * (backwards ? -1 : 1);
            if (car.IsGrounded) return MathF.Abs(MathF.Atan2(local.y, local.x));
            return MathF.Atan2(MathF.Sqrt(local.y * local.y + local.z * local.z), local.x);
        }

        public static bool NearGoalMouth(Vec3 position) => MathF.Abs(position.y) > 4200 && MathF.Abs(position.x) < 1650;

        /// <summary>Route through the mouth rather than across a post/backwall. Standard Soccar only.</summary>
        public static Vec3 GoalWaypoint(Vec3 start, Vec3 target)
        {
            if (start.z > 150 || target.z > 150) return target;
            float side = start.y < 0 ? -1 : 1;
            float depth = start.y * side;
            // Exit the goal toward its central opening before driving laterally across the field.
            if (depth > 4900 && MathF.Abs(start.x) < 1000 && target.y * side < 4850)
                return new Vec3(System.Math.Clamp(start.x * 0.4f, -350, 350), side * 4550, target.z);
            // First get in front of the post when returning from a deep corner.
            if (depth > 4650 && MathF.Abs(start.x) >= 1000 &&
                (MathF.Abs(target.x) < 1000 || start.x * target.x < 0))
                return new Vec3(System.Math.Clamp(start.x, -3300, 3300), side * 4350, target.z);
            // Shots may intentionally enter a goal; support targets are clamped before calling this.
            side = target.y < 0 ? -1 : 1;
            if (target.y * side > 5050 && start.y * side < 4700 && MathF.Abs(target.x) < 900)
            {
                float t = (side * 5000 - start.y) / (target.y - start.y);
                float crossingX = start.x + (target.x - start.x) * t;
                if (MathF.Abs(crossingX) > 600)
                    return new Vec3(System.Math.Clamp(target.x, -500, 500), side * 4600, target.z);
            }
            return target;
        }
    }
}
