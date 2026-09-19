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
