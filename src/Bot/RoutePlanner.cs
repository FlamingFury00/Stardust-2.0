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
        /// Select a covered refill. Small pads remain strict on-route pickups; a critically low support
        /// car may make a bounded full-pad excursion only when the opponent window covers the pickup.
        /// </summary>
        public static Boost SelectBoost(Car car, IEnumerable<Boost> pads, Vec3 ball, Vec3 destination,
            int team, float opponentEta, Func<Car, Vec3, float> travelTime = null)
        {
            if (pads == null || car.IsDemolished || car.Boost >= 35 || !float.IsFinite(opponentEta) || opponentEta < 1)
                return null;

            travelTime ??= (c, target) => Drive.GetEta(c, target);
            bool critical = car.Boost < 20;
            Boost best = null;
            float bestCost = float.PositiveInfinity;

            foreach (Boost pad in pads)
            {
                if (pad == null || !ControlMath.Finite(pad.Location)) continue;
                // Defensive/support refills stay goal-side of the ball.
                if (pad.Location.y * Field.Side(team) < ball.y * Field.Side(team)) continue;

                float eta = travelTime(car, pad.Location);
                if (!float.IsFinite(eta) || eta < 0) continue;
                // A pad which respawns before arrival is a valid pickup; do not require it active now.
                if (!pad.IsActive && pad.TimeUntilActive > eta + 0.12f) continue;

                float detour = Detour(car.Location, pad.Location, destination);
                bool deliberateFull = pad.IsLarge && critical && detour <= 2600 &&
                    opponentEta >= eta + 1.0f;

                if (!deliberateFull)
                {
                    if (detour > 450 || eta + 0.5f > opponentEta) continue;
                }

                float usefulBoost = MathF.Min(100 - car.Boost, pad.IsLarge ? 100 : 12);
                float value = (pad.IsLarge ? 9f : 5f) * usefulBoost;
                float cost = detour + 120 * eta - value;
                if (cost < bestCost)
                {
                    best = pad;
                    bestCost = cost;
                }
            }
            return best;
        }
    }
}
