using System;
using RedUtils.Math;

namespace RedUtils
{
    /// <summary>A speed-flip action, where the car turns slightly, diagonal dodges, cancels, and lands.</summary>
    class SpeedFlip : IAction
    {
        public const float Duration = 1.35f;
        public bool Interruptible { get; set; }
        public bool Finished { get; set; }
        public Vec3 Direction;

        private readonly SpeedFlipTimeline timeline = new();
        private bool committed;
        private int side;

        public SpeedFlip(Vec3 direction)
        {
            Interruptible = false;
            Finished = false;
            Direction = direction.Normalize();
        }

        public void Run(RUBot bot)
        {
            bot.Controller.Throttle = 1;

            if (!committed)
            {
                float forwardSpeed = bot.Me.Velocity.Dot(bot.Me.Forward);
                float radius = Drive.TurnRadius(forwardSpeed);
                float angle = 0.06f * forwardSpeed / MathF.Max(radius, 0.01f);
                Vec3 leftVec = Direction.Rotate(angle).Flatten().Normalize();
                Vec3 rightVec = Direction.Rotate(-angle).Flatten().Normalize();

                if (bot.Me.Velocity.Angle(leftVec) < bot.Me.Velocity.Angle(rightVec))
                {
                    bot.AimAt(bot.Me.Location + leftVec);
                    if (bot.Me.Velocity.FlatAngle(leftVec) < 0.05f)
                    {
                        committed = true;
                        side = 1;
                    }
                }
                else
                {
                    bot.AimAt(bot.Me.Location + rightVec);
                    if (bot.Me.Velocity.FlatAngle(rightVec) < 0.05f)
                    {
                        committed = true;
                        side = -1;
                    }
                }
            }

            if (committed)
            {
                SpeedFlipFrame frame = timeline.Step(Game.Time, side);
                bot.Controller.Jump = frame.Jump;
                bot.Controller.Pitch = frame.Pitch;
                bot.Controller.Yaw = frame.Yaw;
                bot.Controller.Roll = frame.Roll;
                bot.Controller.Handbrake = frame.Handbrake;
                Finished |= frame.Finished;
            }

            // Abort only before committing to the jump. Once airborne, the sequence must
            // finish its cancel/recovery rather than self-cancelling because it left the ground.
            if (!Field.InField(bot.Me.Location, 150) || (!committed && !bot.Me.IsGrounded))
                Finished = true;
        }
    }
}
