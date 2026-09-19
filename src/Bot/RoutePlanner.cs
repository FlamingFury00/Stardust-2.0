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

        /// <summary>Choose active small OR large pads on the defensive route, not a corner-boost excursion.</summary>
        public static Boost SelectBoost(Car car, IEnumerable<Boost> pads, Vec3 ball, Vec3 destination,
            int team, float opponentEta, Func<Car, Vec3, float> travelTime = null)
        {
            if (pads == null || car.IsDemolished || car.Boost >= 35 || !float.IsFinite(opponentEta) || opponentEta < 1) return null;
            travelTime ??= (c, target) => Drive.GetEta(c, target);
            Boost best = null;
            float bestCost = float.PositiveInfinity;
            foreach (Boost pad in pads)
            {
                if (pad == null || !pad.IsActive || !ControlMath.Finite(pad.Location)) continue;
                if (pad.Location.y * Field.Side(team) < ball.y * Field.Side(team)) continue;
                float detour = Detour(car.Location, pad.Location, destination);
                if (detour > 400) continue;
                float eta = travelTime(car, pad.Location);
                if (!float.IsFinite(eta) || eta < 0 || eta + 0.5f > opponentEta) continue;
                float usefulBoost = MathF.Min(100 - car.Boost, pad.IsLarge ? 100 : 12);
                float cost = detour + 120 * eta - 5 * usefulBoost;
                if (cost < bestCost) { best = pad; bestCost = cost; }
            }
            return best;
        }
    }
}
