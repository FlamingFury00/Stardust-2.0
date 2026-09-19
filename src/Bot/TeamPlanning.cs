using System;
using System.Collections.Generic;
using System.Linq;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public enum TeamRole { Challenge, Support, Anchor }

    public sealed class TeamPlan
    {
        public int Challenger = -1, Anchor = -1;
        public readonly Dictionary<int, TeamRole> Roles = new();
        public readonly Dictionary<int, Vec3> Targets = new();
        public bool HasCover;
    }

    /// <summary>Every car derives the same allocation from the same world snapshot; no pairwise fuzzy sorting.</summary>
    public static class TeamPlanning
    {
        public static float EstimateEta(Car car, Vec3 target)
        {
            if (car.IsDemolished || !ControlMath.Finite(car.Location) || !ControlMath.Finite(target)) return 9;
            Vec3 delta = target - car.Location;
            float distance = delta.FlatLen();
            float angle = MathF.Abs(MathF.Atan2(car.Local(delta).y, car.Local(delta).x));
            float speed = MathF.Max(0, car.Velocity.Dot(ControlMath.FlatUnit(delta, car.Forward)));
            // Conservative acceleration-limited race estimate (not a shot-feasibility test).
            float acceleration = car.Boost > 10 ? 1500 : 700;
            float cap = car.Boost > 10 ? 2200 : 1400;
            float ramp = MathF.Max(0, (cap - speed) / acceleration);
            float rampDistance = speed * ramp + 0.5f * acceleration * ramp * ramp;
            float travel = distance <= rampDistance
                ? (MathF.Sqrt(speed * speed + 2 * acceleration * distance) - speed) / acceleration
                : ramp + (distance - rampDistance) / MathF.Max(cap, speed);
            float airborne = !car.IsGrounded && delta.Length() > 650 ? 0.35f : 0;
            return System.Math.Clamp(travel + angle * 0.35f + airborne, 0.05f, 9);
        }

        public static TeamPlan Assign(IEnumerable<Car> allCars, Ball ball, int team, Func<Car, float> eta = null, int defender = -1)
        {
            var plan = new TeamPlan();
            Car[] cars = allCars.Where(c => c != null && !c.IsDemolished && c.Team == team &&
                ControlMath.Finite(c.Location)).OrderBy(c => c.Index).ToArray();
            if (cars.Length == 0) return plan;
            eta ??= c => EstimateEta(c, ball.location);
            int side = Field.Side(team);
            float Score(Car c)
            {
                float t = eta(c);
                if (!float.IsFinite(t)) t = 9;
                Vec3 relative = ball.location - c.Location;
                bool controlling = relative.Length() < 270 && relative.z > 50 && relative.z < 240 &&
                    (ball.velocity - c.Velocity).Length() < 500;
                float wrongSide = c.Location.y * side < ball.location.y * side - 200 ? 0.65f : 0;
                return controlling ? 0 : t + wrongSide + (c.Boost < 10 && ball.location.z > 350 ? 0.45f : 0);
            }
            // Fixed buckets form a total order. A fuzzy pairwise comparator is not transitive for 3+ cars.
            Car first = cars.FirstOrDefault(c => c.Index == defender) ?? cars.OrderBy(c => (int)(Score(c) / 0.08f)).ThenBy(c => c.Index).First();
            plan.Challenger = first.Index;
            Car[] remaining = cars.Where(c => c.Index != first.Index).ToArray();
            Car anchor = remaining.OrderByDescending(c => (c.Location.y + c.Velocity.y * 0.25f) * side)
                .ThenBy(c => c.Index).FirstOrDefault();
            plan.Anchor = anchor?.Index ?? first.Index;
            plan.HasCover = remaining.Any(c => c.Location.y * side > ball.location.y * side + 500 &&
                c.Location.z < 250 && c.Location.FlatDist(new Vec3(0, side * 4400, 17)) < 5200);
            float ballDepth = ball.location.y * side;
            float wing = MathF.Abs(ball.location.x) > 100 ? MathF.Sign(ball.location.x) :
                MathF.Abs(first.Location.x) > 100 ? MathF.Sign(first.Location.x) : -side;
            int supportSlot = 0;
            foreach (Car car in cars)
            {
                TeamRole role = car.Index == first.Index ? TeamRole.Challenge : car.Index == plan.Anchor ? TeamRole.Anchor : TeamRole.Support;
                plan.Roles[car.Index] = role;
                Vec3 target;
                if (role == TeamRole.Anchor)
                {
                    float depth = System.Math.Clamp(ballDepth + 2300, -800, 4350);
                    float x = ballDepth > 3000 ? -wing * 450 : System.Math.Clamp(ball.location.x * 0.20f, -650, 650);
                    target = new Vec3(x, side * depth, 17);
                }
                else if (role == TeamRole.Support)
                {
                    int slot = supportSlot++;
                    float x = (slot % 2 == 0 ? -wing : wing) * (1550 + 350 * (slot / 2));
                    float depth = System.Math.Clamp(ballDepth + 1150 + 400 * (slot / 2), -2700, 3050);
                    target = new Vec3(x, side * depth, 17);
                }
                else target = ball.location + ControlMath.FlatUnit(new Vec3(0, side * 5120, 0) - ball.location,
                    new Vec3(0, side, 0)) * 700;
                plan.Targets[car.Index] = Navigation.FieldTarget(target);
            }
            return plan;
        }

        /// <summary>Keep support out of a teammate's short-horizon path without moving the anchor out of its lane.</summary>
        public static Vec3 Separate(Car car, Vec3 target, IEnumerable<Car> cars, int team, bool anchor)
        {
            if (anchor) return Navigation.FieldTarget(target);
            foreach (Car mate in cars.OrderBy(c => c.Index))
            {
                if (mate.Index == car.Index || mate.Team != team || mate.IsDemolished) continue;
                Vec3 predicted = mate.Location + mate.Velocity * 0.35f;
                if (target.FlatDist(predicted) >= 900) continue;
                float direction = MathF.Abs(target.x - predicted.x) > 20 ? MathF.Sign(target.x - predicted.x) :
                    (car.Index < mate.Index ? -1 : 1) * -Field.Side(team);
                target.x = predicted.x + direction * 1000;
            }
            return Navigation.FieldTarget(target);
        }
    }
}
