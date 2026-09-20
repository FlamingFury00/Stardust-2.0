using System;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;

namespace Bot
{
    /// <summary>Pure control primitives. Rocket League axes are x forward, y right, z roof.</summary>
    public static class ControlMath
    {
        public static bool Finite(Vec3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        public static Vec3 Unit(Vec3 v, Vec3 fallback)
        {
            float length = v.Length();
            return Finite(v) && length > 0.0001f ? v / length : fallback;
        }
        public static Vec3 FlatUnit(Vec3 v, Vec3 fallback) => Unit(new Vec3(v.x, v.y, 0), fallback);

        /// <summary>Shortest SO(3) rotation in car-local coordinates, including exactly inverted cars.</summary>
        public static Vec3 RotationError(Car car, Vec3 forward, Vec3 roof)
        {
            Vec3 f = Unit(forward, car.Forward);
            Vec3 upHint = Unit(roof, Vec3.Up);
            if (MathF.Abs(f.Dot(upHint)) > 0.98f)
                upHint = MathF.Abs(f.z) < 0.9f ? Vec3.Up : new Vec3(0, 1, 0);
            Vec3 r = Unit(upHint.Cross(f), car.Right);
            Vec3 u = Unit(f.Cross(r), car.Up);
            float m00 = car.Forward.Dot(f), m01 = car.Forward.Dot(r), m02 = car.Forward.Dot(u);
            float m10 = car.Right.Dot(f), m11 = car.Right.Dot(r), m12 = car.Right.Dot(u);
            float m20 = car.Up.Dot(f), m21 = car.Up.Dot(r), m22 = car.Up.Dot(u);
            float w, x, y, z, s;
            float trace = m00 + m11 + m22;
            if (trace > 0)
            {
                s = MathF.Sqrt(trace + 1) * 2;
                w = 0.25f * s; x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                s = MathF.Sqrt(MathF.Max(0.000001f, 1 + m00 - m11 - m22)) * 2;
                w = (m21 - m12) / s; x = 0.25f * s; y = (m01 + m10) / s; z = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                s = MathF.Sqrt(MathF.Max(0.000001f, 1 + m11 - m00 - m22)) * 2;
                w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25f * s; z = (m12 + m21) / s;
            }
            else
            {
                s = MathF.Sqrt(MathF.Max(0.000001f, 1 + m22 - m00 - m11)) * 2;
                w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25f * s;
            }
            Vec3 imaginary = new Vec3(x, y, z) * (w < 0 ? -1 : 1);
            float norm = imaginary.Length();
            return norm < 0.00001f ? imaginary * 2 : imaginary * (2 * MathF.Atan2(norm, MathF.Abs(w)) / norm);
        }

        public static void Aim(Car car, ControllerStateT controls, Vec3 forward, Vec3 roof)
        {
            Vec3 error = RotationError(car, forward, roof);
            Vec3 rate = car.LocalAngularVelocity;
            // Pitch and roll torque signs are negative in the RL coordinate convention.
            controls.Roll = ControlRuntime.Axis(-(8 * error.x - 2.4f * rate.x) / 5);
            controls.Pitch = ControlRuntime.Axis(-(7 * error.y - 2.5f * rate.y) / 4);
            controls.Yaw = ControlRuntime.Axis((7 * error.z - 2.5f * rate.z) / 4);
        }

        /// <summary>Velocity-matched PD with gravity feed-forward. Target position is remeasured every tick.</summary>
        public static Vec3 FlightAcceleration(Vec3 position, Vec3 velocity, Vec3 targetPosition,
            Vec3 targetVelocity, Vec3 gravity) => (targetPosition - position) * 5f + (targetVelocity - velocity) * 3.2f - gravity;

        public static void Ground(Car car, ControllerStateT controls, Vec3 direction, float targetSpeed)
        {
            Vec3 local = car.Local(Unit(direction, car.Forward));
            float angle = MathF.Atan2(local.y, local.x);
            controls.Steer = ControlRuntime.Axis(3.5f * angle - 0.25f * car.LocalAngularVelocity.z);
            float speed = car.Velocity.Dot(car.Forward);
            controls.Throttle = ControlRuntime.Axis((targetSpeed - speed) / 250f);
            controls.Handbrake = false;
            controls.Boost = false;
        }
    }

    /// <summary>
    /// Continuous acceleration demand translated into minimum-duration boost pulses.
    /// The implementation lives in RedUtils so timed aerial shots and possession mechanics share one model.
    /// </summary>
    public sealed class BoostGate
    {
        private readonly ImpulseBoostGate gate = new();
        public bool Step(float now, float demand, float alignment, float fuel, bool gentleContact) =>
            gate.Step(now, demand, alignment, fuel, gentleContact);
        public void Reset() => gate.Reset();
    }

    public readonly struct JumpCommand
    {
        public readonly bool Jump, Dodge;
        public JumpCommand(bool jump, bool dodge) { Jump = jump; Dodge = dodge; }
    }

    /// <summary>Enforces an observed release tick before a second jump, even after dropped frames.</summary>
    public sealed class JumpSequence
    {
        private readonly float hold;
        private float start = float.NaN, releasedAt = float.NaN, firedAt = float.NaN;
        public bool Fired => float.IsFinite(firedAt);
        public JumpSequence(float holdSeconds = 0.12f) { hold = System.Math.Clamp(holdSeconds, 0.025f, 0.2f); }
        public JumpCommand Step(float now, bool canDodge)
        {
            if (!float.IsFinite(start)) start = now;
            if (now - start < hold) return new JumpCommand(true, false);
            if (!float.IsFinite(releasedAt)) { releasedAt = now; return new JumpCommand(false, false); }
            if (now <= releasedAt) return new JumpCommand(false, false);
            if (!Fired && canDodge && now - releasedAt <= 0.35f) firedAt = now;
            bool dodge = Fired && now - firedAt < 0.05f;
            return new JumpCommand(dodge, dodge);
        }
    }

    /// <summary>Confirms a newly acquired reset only after spent-flip state and a recent own wheel contact.</summary>
    public sealed class ResetEvidence
    {
        private bool spent;
        private float contact = float.NegativeInfinity;
        public bool Confirmed { get; private set; }
        public bool Observe(JumpState state, bool ownTouch, bool wheelsAligned, float height, float now)
        {
            spent |= state.DoubleJumped || state.Dodged;
            if (spent && ownTouch && wheelsAligned && height > 250) contact = now;
            if (spent && state.HasReset && height > 250 && now >= contact && now - contact <= 0.2f) Confirmed = true;
            return Confirmed;
        }
    }
}
