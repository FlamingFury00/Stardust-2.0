using System;
using System.Collections.Generic;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public enum TeamRole { Challenger, Support, Cover }
    public sealed class TeamAssignment
    {
        public int FirstMan = -1, Cover = -1, LastBack = -1, Count;
        public readonly Dictionary<int, TeamRole> Roles = new();
        public readonly Dictionary<int, Vec3> Targets = new();
    }
    /// <summary>One election from the same world snapshot; never seed the election with the observing bot.</summary>
    public static class TeamShape
    {
        public static TeamAssignment Assign(IEnumerable<Car> cars, int team, Vec3 ball, Func<Car, float> eta)
        {
            var living = new List<Car>();
            foreach (Car car in cars)
                if (car != null && !car.IsDemolished && car.Team == team) living.Add(car);
            living.Sort((a, b) => a.Index.CompareTo(b.Index));
            var result = new TeamAssignment { Count = living.Count };
            if (living.Count == 0) return result;
            int side = Field.Side(team);
            float best = float.PositiveInfinity;
            foreach (Car car in living)
            {
                float bid = eta(car);
                if (!float.IsFinite(bid)) bid = 6;
                if (car.Location.y * side < ball.y * side - 200) bid += 0.65f;
                if (car.Boost < 12 && car.Location.FlatDist(ball) > 1200) bid += 0.22f;
                if (result.FirstMan < 0 || Tactics.WinsTie(bid, car.Index, best, result.FirstMan))
                { best = bid; result.FirstMan = car.Index; }
                if (result.LastBack < 0 || Deeper(car, living.Find(c => c.Index == result.LastBack), side)) result.LastBack = car.Index;
            }
            foreach (Car car in living)
                if (car.Index != result.FirstMan && (result.Cover < 0 || Deeper(car, living.Find(c => c.Index == result.Cover), side))) result.Cover = car.Index;
            if (result.Cover < 0) result.Cover = result.FirstMan;
            int supportIndex = 0;
            foreach (Car car in living)
            {
                TeamRole role = car.Index == result.FirstMan ? TeamRole.Challenger : car.Index == result.Cover ? TeamRole.Cover : TeamRole.Support;
                result.Roles[car.Index] = role;
                result.Targets[car.Index] = Target(ball, Field.Goals[team].Location, role, role == TeamRole.Support ? supportIndex++ : 0);
            }
            return result;
        }
        private static bool Deeper(Car a, Car b, int side) => b == null || a.Location.y * side > b.Location.y * side + 1 ||
            (MathF.Abs(a.Location.y - b.Location.y) <= 1 && a.Index < b.Index);
        public static Vec3 Target(Vec3 ball, Vec3 goal, TeamRole role, int slot = 0)
        {
            float side = goal.y < 0 ? -1 : 1;
            float depth = ball.y * side;
            float wing = MathF.Abs(ball.x) > 200 ? -MathF.Sign(ball.x) : -side;
            if (role == TeamRole.Cover)
                return new Vec3(System.Math.Clamp(-ball.x * 0.18f, -550, 550), side * System.Math.Clamp(depth + 2450, -800, 4400), 17);
            if (role == TeamRole.Support)
                return new Vec3(wing * (slot % 2 == 0 ? 1750 : -1750), side * System.Math.Clamp(depth + 1200 + slot / 2 * 600, -3400, 3150), 17);
            return new Vec3(System.Math.Clamp(ball.x * 0.75f, -3100, 3100), side * System.Math.Clamp(depth + 850, -3900, 4350), 17);
        }
        public static bool HasCover(IEnumerable<Car> cars, Car me, Vec3 ball, int team)
        {
            int side = Field.Side(team);
            foreach (Car car in cars)
                if (car.Index != me.Index && car.Team == team && !car.IsDemolished && car.Location.z < 350 &&
                    car.Location.y * side >= ball.y * side + 250 && MathF.Abs(car.Location.x) < 2600) return true;
            return false;
        }
    }
}
