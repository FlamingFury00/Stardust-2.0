using System;
using System.Collections.Generic;
using System.Globalization;
using RLBot.Flat;

namespace RedUtils
{
    /// <summary>Possession controllers survive their OWN touches, never another player's touch.</summary>
    public interface IPossessionAction : IAction { float ClaimTime { get; } }

    public sealed class TickClock
    {
        private float previous = float.NaN;
        public bool Discontinuity { get; private set; }
        public float Step(float now)
        {
            Discontinuity = false;
            if (!float.IsFinite(now)) { Discontinuity = true; previous = float.NaN; return 0; }
            if (!float.IsFinite(previous)) { previous = now; return 1f / 120f; }
            float elapsed = now - previous;
            previous = now;
            if (elapsed < 0 || elapsed > 0.25f) { Discontinuity = true; return 1f / 120f; }
            return MathF.Min(elapsed, 1f / 15f);
        }
    }

    /// <summary>Persistent packet flags, NOT transient Jumping/Dodging animation states.</summary>
    public readonly struct JumpState
    {
        public readonly bool Grounded, Jumped, DoubleJumped, Dodged;
        public readonly AirState State;
        public readonly float DodgeTimeout;
        public JumpState(PlayerInfoT player)
        {
            Grounded = player.AirState == AirState.OnGround;
            Jumped = player.HasJumped;
            DoubleJumped = player.HasDoubleJumped;
            Dodged = player.HasDodged;
            State = player.AirState;
            DodgeTimeout = player.DodgeTimeout;
        }
        public bool HasReset => State == AirState.InAir && !Jumped && !DoubleJumped && !Dodged;
        public bool CanDodge => !Grounded && !DoubleJumped && !Dodged &&
            (HasReset || (Jumped && DodgeTimeout > 0));
        public static void Apply(Car car, PlayerInfoT player)
        {
            car.HasJumped = player.HasJumped;
            car.HasDoubleJumped = player.HasDoubleJumped || player.HasDodged;
        }
    }

    public static class ControlRuntime
    {
        public static bool CancelBeforeRun(IAction action, bool changedTouch, bool ownTouch,
            bool demolished, bool discontinuity)
        {
            if (action == null) return false;
            return action.Finished || demolished || discontinuity ||
                (changedTouch && action.Interruptible && !(ownTouch && action is IPossessionAction));
        }
        public static float Axis(float value) => float.IsFinite(value) ? System.Math.Clamp(value, -1f, 1f) : 0f;
        public static ControllerStateT Sanitize(ControllerStateT input, bool demolished, float boost)
        {
            if (demolished || input == null) return new ControllerStateT();
            input.Throttle = Axis(input.Throttle);
            input.Steer = Axis(input.Steer);
            input.Pitch = Axis(input.Pitch);
            input.Yaw = Axis(input.Yaw);
            input.Roll = Axis(input.Roll);
            input.Boost &= float.IsFinite(boost) && boost > 0;
            return input;
        }
    }

    /// <summary>Bounded, thread-safe, locale-independent claims with deterministic equal-time ownership.</summary>
    public sealed class ShotClaimLedger
    {
        private readonly Dictionary<int, (float Slice, float Received)> claims = new();
        private readonly object gate = new();
        public void Clear() { lock (gate) claims.Clear(); }
        public void Release(int index) { lock (gate) claims.Remove(index); }
        public bool Receive(int index, string message, float now)
        {
            if (index < 0 || !float.IsFinite(now) || message == null || message.Length > 64) return false;
            if (message == "RELEASE_SHOT") { Release(index); return true; }
            const string prefix = "CLAIM_SHOT;";
            if (!message.StartsWith(prefix, StringComparison.Ordinal) ||
                !float.TryParse(message.Substring(prefix.Length), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float slice) || !float.IsFinite(slice) ||
                slice < now - 0.2f || slice > now + 6.5f) return false;
            lock (gate)
            {
                if (claims.Count >= 64 && !claims.ContainsKey(index)) return false;
                claims[index] = (slice, now);
            }
            return true;
        }
        public bool EarlierThan(int myIndex, float mySlice, float now, float grace = 0.04f)
        {
            lock (gate)
            {
                foreach (var entry in claims)
                {
                    if (entry.Key == myIndex) continue;
                    var claim = entry.Value;
                    if (now < claim.Received || now - claim.Received > 1f || now > claim.Slice + 0.2f) continue;
                    float difference = claim.Slice - mySlice;
                    if (difference < -grace || (MathF.Abs(difference) <= grace && entry.Key < myIndex)) return true;
                }
            }
            return false;
        }
        public static string Encode(float slice) => "CLAIM_SHOT;" + slice.ToString("R", CultureInfo.InvariantCulture);
    }
}
