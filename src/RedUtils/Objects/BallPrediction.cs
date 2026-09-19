using System;
using RedUtils.Math;
using RLBot.Flat;

namespace RedUtils
{
    /// <summary>Prediction samples retain framework timestamps; no fixed 60/120 Hz assumption.</summary>
    public struct BallPrediction
    {
        public BallSlice[] Slices;
        public int Length => Slices?.Length ?? 0;
        public BallSlice this[int index] => Slices[index];
        public BallPrediction(BallPredictionT prediction)
        {
            int count = prediction?.Slices?.Count ?? 0;
            Slices = new BallSlice[count];
            for (int i = 0; i < count; i++) Slices[i] = new BallSlice(prediction.Slices[i]);
        }
        public BallSlice Find(Predicate<BallSlice> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            // Old coarse refinement skipped the successful coarse sample itself and the tail.
            // Expensive feasibility checks must explicitly budget their search in their caller.
            for (int i = 0; i < Length; i++)
            {
                BallSlice slice = Slices[i];
                if (slice == null) continue;
                if (MathF.Abs(slice.Location.y) > 5250) break;
                if (predicate(slice)) return slice;
            }
            return null;
        }
        public BallSlice FindGoal(int team)
        {
            int scoringSide = -Field.Side(team);
            for (int i = 0; i < Length; i++)
                if (Slices[i] != null && Slices[i].Location.y * scoringSide > 5250) return Slices[i];
            return null;
        }

        /// <summary>Interpolate on a sorted framework prediction; never extrapolate past either endpoint.</summary>
        public bool TrySample(float time, out Ball sample)
        {
            sample = null;
            if (!float.IsFinite(time) || Length == 0 || Slices[0] == null || Slices[Length - 1] == null) return false;
            if (time < Slices[0].Time || time > Slices[Length - 1].Time) return false;
            int low = 0, high = Length - 1;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (Slices[mid] == null) return false;
                if (Slices[mid].Time < time) low = mid + 1;
                else high = mid;
            }
            BallSlice right = Slices[low];
            if (right == null || !float.IsFinite(right.Time)) return false;
            if (right.Time == time) { sample = right.ToBall(); return Finite(sample); }
            if (low == 0) return false;
            BallSlice left = Slices[low - 1];
            if (left == null) return false;
            float duration = right.Time - left.Time;
            if (!float.IsFinite(duration) || duration <= 0) return false;
            float fraction = (time - left.Time) / duration;
            sample = new Ball(Utils.Lerp(fraction, left.Location, right.Location),
                Utils.Lerp(fraction, left.Velocity, right.Velocity),
                Utils.Lerp(fraction, left.AngularVelocity, right.AngularVelocity));
            return Finite(sample);
        }
        private static bool Finite(Ball ball) => Finite(ball.location) && Finite(ball.velocity) && Finite(ball.angularVelocity);
        private static bool Finite(Vec3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
