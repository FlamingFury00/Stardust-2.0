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

        public static Boost SelectBoost(Car car, IEnumerable<Boost> pads, Vec3 ball, Vec3 destination,
            int team, float opponentEta, Func<Car, Vec3, float> travelTime = null,
            IEnumerable<Car> cars = null, bool pressure = false, int preferred = -1)
        {
            if (pads == null || car.IsDemolished || !float.IsFinite(car.Boost) || car.Boost >= 65 ||
                !float.IsFinite(opponentEta)) return null;
            travelTime ??= (c, target) => Drive.GetEta(c, target, false);
            Boost best = null;
            float bestCost = float.PositiveInfinity;
            int side = Field.Side(team);
            foreach (Boost pad in pads)
            {
                if (pad == null || !pad.IsActive || !ControlMath.Finite(pad.Location)) continue;
                if (pad.Location.y * side < ball.y * side) continue;
                float detour = Detour(car.Location, pad.Location, destination);
                bool urgent = pressure || opponentEta < 1;
                if (detour > (urgent ? 80 : car.Boost < 15 ? 650 : 420)) continue;
                if (urgent && (pad.IsLarge || car.Location.FlatDist(pad.Location) > 300)) continue;
                float eta = travelTime(car, pad.Location);
                if (!float.IsFinite(eta) || eta < 0) continue;
                // Keep the original conservative race gate when no opponent locations are supplied.
                if (cars == null && eta + 0.5f > opponentEta) continue;
                bool contested = false;
                if (cars != null)
                    foreach (Car other in cars)
                    {
                        if (other.Index == car.Index || other.IsDemolished) continue;
                        float otherEta = travelTime(other, pad.Location);
                        if (!float.IsFinite(otherEta)) continue;
                        if (other.Team != team && otherEta < eta + 0.15f) { contested = true; break; }
                        if (other.Team == team && other.Boost < 65 &&
                            (otherEta < eta - 0.15f || (MathF.Abs(otherEta - eta) <= 0.15f && other.Index < car.Index)))
                        { contested = true; break; }
                    }
                if (contested) continue;
                float usefulBoost = MathF.Min(100 - car.Boost, pad.IsLarge ? 100 : 12);
                float cost = detour + 180 * eta - 4 * usefulBoost - (pad.Index == preferred ? 120 : 0);
                if (cost < bestCost) { best = pad; bestCost = cost; }
            }
            return best;
        }
    }
}
