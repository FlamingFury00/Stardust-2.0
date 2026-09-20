using System;

namespace RedUtils
{
    /// <summary>One frame of a speed-flip input sequence.</summary>
    public readonly struct SpeedFlipFrame
    {
        public readonly bool Jump;
        public readonly bool Dodge;
        public readonly bool Handbrake;
        public readonly bool Finished;
        public readonly float Pitch;
        public readonly float Yaw;
        public readonly float Roll;

        public SpeedFlipFrame(bool jump = false, bool dodge = false, bool handbrake = false,
            bool finished = false, float pitch = 0, float yaw = 0, float roll = 0)
        {
            Jump = jump;
            Dodge = dodge;
            Handbrake = handbrake;
            Finished = finished;
            Pitch = pitch;
            Yaw = yaw;
            Roll = roll;
        }
    }

    /// <summary>
    /// Time-based speed-flip sequencer. It always emits at least one observed jump-release
    /// frame before the dodge rising edge, so dropped control packets cannot skip the flip.
    /// </summary>
    public sealed class SpeedFlipTimeline
    {
        private float startedAt = float.NaN;
        private float releasedAt = float.NaN;
        private float dodgedAt = float.NaN;

        public SpeedFlipFrame Step(float now, int side)
        {
            if (!float.IsFinite(now))
                return new SpeedFlipFrame(finished: true);

            side = side < 0 ? -1 : 1;
            if (!float.IsFinite(startedAt) || now < startedAt)
            {
                startedAt = now;
                releasedAt = float.NaN;
                dodgedAt = float.NaN;
            }

            float elapsed = now - startedAt;

            // Hold the first jump for about the same duration as the legacy sequence.
            if (!float.IsFinite(releasedAt) && elapsed < 0.10f)
                return new SpeedFlipFrame(jump: true);

            // A dodge is edge-triggered. Always output one neutral jump frame first,
            // even if the next packet arrived after the old 0.12-0.15 s window.
            if (!float.IsFinite(releasedAt))
            {
                releasedAt = now;
                return new SpeedFlipFrame();
            }
            if (!float.IsFinite(dodgedAt))
            {
                if (now <= releasedAt)
                    return new SpeedFlipFrame();

                dodgedAt = now;
                return new SpeedFlipFrame(jump: true, dodge: true, pitch: -1, roll: side * 0.5f);
            }

            float sinceDodge = now - dodgedAt;
            if (sinceDodge < 0.05f)
                return new SpeedFlipFrame(jump: true, dodge: true, pitch: -1, roll: side * 0.5f);
            if (sinceDodge < 0.65f)
                return new SpeedFlipFrame(pitch: 1, roll: side);
            if (sinceDodge < 0.80f)
                return new SpeedFlipFrame(handbrake: true, pitch: 1, yaw: side, roll: side);

            return new SpeedFlipFrame(finished: true);
        }
    }
}
