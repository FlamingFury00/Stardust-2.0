using System;
using System.Collections.Generic;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public static class RoutePlanner
    {
        public static float Detour(Vec3 start, Vec3 pad, Vec3 destination) =>
            MathF.Max(0, start.FlatDist(pad) + pad.FlatDist(destination) - start.FlatDist(destination));

        /// <summary>
        /// Deliberate full-pad refill, separate from collecting small pads on a shadow
        /// route. Being the only car in 1v1 is NOT a permanent prohibition on refilling.
        /// Goal return and pad races are timing estimates, not proofs of safe possession.
        /// </summary>
        public static Boost SelectRefuel(Car car, IEnumerable<Boost> pads, Ball ball, int team,
            IEnumerable<Car> teammates, IEnumerable<Car> opponents, float goalThreat)
        {
            if (pads == null || car.IsDemolished || !float.IsFinite(car.Boost) || car.Boost >= 40 ||
                !ControlMath.Finite(car.Location) || !ControlMath.Finite(ball.location)) return null;
            float side = Field.Side(team);
            Vec3 guard = new Vec3(System.Math.Clamp(ball.location.x * 0.4f, -650, 650), side * 4300, 17);
            bool covered = false;
            if (teammates != null)
                foreach (Car mate in teammates)
                    if (mate != null && mate.Index != car.Index && mate.Team == team && !mate.IsDemolished &&
                        mate.IsGrounded && mate.Location.y * side > ball.location.y * side + 600 &&
                        mate.Location.FlatDist(guard) < 3800) covered = true;
            float opponentBallEta = 6;
            if (opponents != null)
                foreach (Car opponent in opponents)
                    if (opponent != null && !opponent.IsDemolished && opponent.Team != team)
                        opponentBallEta = MathF.Min(opponentBallEta, TeamPlanning.EstimateEta(opponent, ball.location));
            float window = MathF.Min(goalThreat, opponentBallEta + ball.location.FlatDist(Field.Goals[team].Location) / 3500);
            Boost best = null;
            float bestCost = float.PositiveInfinity;
            foreach (Boost pad in pads)
            {
                if (pad == null || !pad.IsLarge || !pad.IsActive || !ControlMath.Finite(pad.Location)) continue;
                float distance = car.Location.FlatDist(pad.Location);
                if (distance > (covered ? 3500 : 1700)) continue;
                // Without another defender, refuel toward home, not through the opponent.
                if (!covered && pad.Location.y * side < ball.location.y * side - 200) continue;
                float eta = Drive.GetEta(car, pad.Location);
                if (!float.IsFinite(eta) || eta > 2.3f) continue;
                var returning = new Car(car) { Location = new Vec3(pad.Location.x, pad.Location.y, 17), Boost = 100 };
                float returnEta = Drive.GetEta(returning, guard);
                if (!covered && (!float.IsFinite(returnEta) || eta + returnEta + 0.2f > window)) continue;
                if (float.IsFinite(goalThreat) && eta + returnEta + 0.2f > goalThreat) continue;
                bool contested = false;
                if (opponents != null)
                    foreach (Car opponent in opponents)
                        if (opponent != null && opponent.Team != team && !opponent.IsDemolished &&
                            TeamPlanning.EstimateEta(opponent, pad.Location) + 0.1f < eta) contested = true;
                if (teammates != null)
                    foreach (Car mate in teammates)
                        if (mate != null && mate.Team == team && mate.Index != car.Index && !mate.IsDemolished && mate.Boost < 40)
                        {
                            float otherEta = Drive.GetEta(mate, pad.Location);
                            if (otherEta < eta - 0.1f || (MathF.Abs(otherEta - eta) <= 0.1f && mate.Index < car.Index)) contested = true;
                        }
                if (contested) continue;
                float cost = eta + (covered ? 0 : returnEta * 0.15f);
                if (cost < bestCost) { best = pad; bestCost = cost; }
            }
            return best;
        }

        /// <summary>Active pads, arrival deadline, role-specific detours, and deterministic teammate ownership.</summary>
        public static Boost SelectBoost(Car car, IEnumerable<Boost> pads, Vec3 ball, Vec3 destination,
            int team, float opponentEta, Func<Car, Vec3, float> travelTime = null,
            bool lastBack = false, bool pressured = false, IEnumerable<Car> teammates = null, int preferred = -1)
        {
            if (pads == null || car.IsDemolished || !float.IsFinite(car.Boost) || car.Boost >= 65 ||
                !float.IsFinite(opponentEta) || !ControlMath.Finite(destination)) return null;
            travelTime ??= TeamPlanning.EstimateEta;
            Boost best = null;
            float bestCost = float.PositiveInfinity;
            float direct = car.Location.FlatDist(destination);
            foreach (Boost pad in pads)
            {
                if (pad == null || !pad.IsActive || !ControlMath.Finite(pad.Location)) continue;
                float distance = car.Location.FlatDist(pad.Location);
                if (distance < 85 || distance > 3500 || distance > direct + 500) continue;
                float detour = Detour(car.Location, pad.Location, destination);
                bool inlineSmall = !pad.IsLarge && detour < 70 && distance < 1400;
                if (pad.Location.y * Field.Side(team) < ball.y * Field.Side(team) - (inlineSmall ? 250 : 0)) continue;
                float allowance = pressured || lastBack ? 100 : car.Boost < 20 ? 650 : 350;
                if (detour > allowance || ((lastBack || pressured) && pad.IsLarge)) continue;
                if (car.Boost >= 45 && pad.IsLarge) continue;
                float eta = travelTime(car, pad.Location);
                if (!float.IsFinite(eta) || eta < 0) continue;
                // Under pressure, permit only almost-free small pads en route to the defensive slot.
                if (!inlineSmall && eta + detour / 1400 + 0.5f > opponentEta) continue;
                bool reserved = false;
                if (teammates != null)
                    foreach (Car mate in teammates)
                    {
                        if (mate == null || mate.IsDemolished || mate.Index == car.Index || mate.Team != team || mate.Boost >= 65) continue;
                        float other = travelTime(mate, pad.Location);
                        if (float.IsFinite(other) && (other < eta - 0.1f ||
                            (MathF.Abs(other - eta) <= 0.1f && mate.Index < car.Index))) { reserved = true; break; }
                    }
                if (reserved) continue;
                float usefulBoost = MathF.Min(100 - car.Boost, pad.IsLarge ? 100 : 12);
                float cost = detour + 180 * eta - 5 * usefulBoost - (pad.Index == preferred ? 100 : 0);
                if (cost < bestCost || (cost == bestCost && (best == null || pad.Index < best.Index)))
                { best = pad; bestCost = cost; }
            }
            return best;
        }
    }
}
