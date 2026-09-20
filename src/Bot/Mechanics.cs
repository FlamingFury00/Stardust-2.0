using System;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public sealed class GroundDribble : IPossessionAction
    {
        private readonly float started = Game.Time;
        private float stableSince = float.NaN;
        public bool Finished { get; private set; }
        public bool Interruptible => true;
        public float ClaimTime => Game.Time + 0.25f;
        public static bool CanStart(Car car, Ball ball, float freeTime)
        {
            Vec3 local = car.Local(ball.location - car.Location);
            return car.IsGrounded && car.Up.z > 0.9f && freeTime > 0.55f && ball.location.z < 280 &&
                local.x > -70 && local.x < 550 && MathF.Abs(local.y) < 160 &&
                (ball.velocity - car.Velocity).Length() < 1000;
        }
        public void Run(RUBot bot)
        {
            Car car = bot.Me;
            Vec3 offset = Ball.Location - car.Location;
            if (!car.IsGrounded || offset.Length() > 750 || Game.Time - started > 5 || Ball.Location.z > 340)
            { Finished = true; return; }
            Vec3 lane = ControlMath.FlatUnit(bot.TheirGoal.Location - Ball.Location, car.Forward);
            Vec3 local = car.Local(offset);
            Vec3 relativeVelocity = car.Local(Ball.Velocity - car.Velocity);
            bool carried = local.z > 100 && local.z < 215 && MathF.Abs(local.x) < 105 &&
                MathF.Abs(local.y) < 65 && relativeVelocity.Length() < 400;
            stableSince = carried ? (float.IsFinite(stableSince) ? stableSince : Game.Time) : float.NaN;
            float pressure = bot is Stardust stardust ? stardust.Situation.OpponentEta : 6;
            if (carried && Game.Time - stableSince > 0.25f && pressure < 0.75f && local.x > 10 &&
                car.Velocity.Dot(car.Forward) > 500 && car.Forward.Dot(lane) > 0.9f)
            { bot.Action = new ControlledFlick(car, lane); return; }
            // The same pure control law is exercised by the deterministic regression suite.
            bot.Controller = PossessionControl.GroundCarry(car, Ball.MainBall, lane);
        }
    }

    public sealed class GroundCatch : IPossessionAction
    {
        private readonly float started = Game.Time;
        private Drive drive;
        public bool Finished { get; private set; }
        public bool Interruptible => drive?.Interruptible ?? true;
        public float ClaimTime { get; private set; }
        public static BallSlice FindCatch(Car car)
        {
            if (!car.IsGrounded || Ball.Prediction.Slices == null) return null;
            float next = Game.Time + 0.15f;
            foreach (BallSlice slice in Ball.Prediction.Slices)
            {
                if (slice == null || slice.Time < next) continue;
                float time = slice.Time - Game.Time;
                if (time > 1.5f) break;
                next = slice.Time + 0.04f;
                if (slice.Location.z < 105 || slice.Location.z > 175 || slice.Velocity.z > -80) continue;
                float eta = Drive.GetEta(car, slice.Location.Flatten());
                if (float.IsFinite(eta) && eta < time - 0.06f) return slice;
            }
            return null;
        }
        public void Run(RUBot bot)
        {
            if (Game.Time - started > 1.8f || !bot.Me.IsGrounded || GroundDribble.CanStart(bot.Me, Ball.MainBall, 1))
            { Finished = true; return; }
            BallSlice catchSlice = FindCatch(bot.Me);
            if (catchSlice == null) { Finished = true; return; }
            ClaimTime = catchSlice.Time;
            Vec3 lane = ControlMath.FlatUnit(bot.TheirGoal.Location - catchSlice.Location, bot.Me.Forward);
            Vec3 target = (catchSlice.Location - lane * 40).Flatten();
            float time = MathF.Max(0.1f, catchSlice.Time - Game.Time);
            float distance = bot.Me.Location.FlatDist(target);
            float speed = System.Math.Clamp(distance / time, 100, 1800);
            if (distance < 300) speed = MathF.Min(speed, catchSlice.Velocity.FlatLen() + 150);
            drive ??= new Drive(bot.Me, target, speed, allowDodges: false, wasteBoost: false);
            drive.Target = target;
            drive.TargetSpeed = MathF.Max(100, speed);
            drive.Run(bot);
            bot.Controller.Boost = false;
            bot.Controller.Handbrake = false;
        }
    }

    public sealed class ControlledFlick : IPossessionAction
    {
        private readonly JumpSequence jumps = new();
        private readonly Vec3 direction;
        private readonly float started = Game.Time;
        private float elapsed;
        public bool Finished { get; private set; }
        public bool Interruptible => elapsed > 0.65f;
        public float ClaimTime => started + 0.25f;
        public ControlledFlick(Car car, Vec3 direction) { this.direction = ControlMath.Unit(direction, car.Forward); }
        public void Run(RUBot bot)
        {
            elapsed = Game.Time - started;
            if (elapsed > 0.9f || (elapsed > 0.3f && bot.Me.IsGrounded)) { Finished = true; return; }
            JumpCommand command = jumps.Step(Game.Time, bot.Jump.CanDodge);
            ControlMath.Aim(bot.Me, bot.Controller, direction, Vec3.Up);
            bot.Controller.Jump = command.Jump;
            bot.Controller.Boost = false;
            bot.Controller.Throttle = 1;
            if (command.Dodge)
            {
                Vec3 local = ControlMath.FlatUnit(bot.Me.Local(direction), new Vec3(1, 0, 0));
                bot.Controller.Pitch = -local.x;
                bot.Controller.Yaw = local.y;
                bot.Controller.Roll = 0;
            }
            else if (!jumps.Fired) bot.Controller.Pitch = -0.15f;
        }
    }

    public sealed class AerialCarry : IPossessionAction
    {
        private readonly BoostGate boost = new();
        private readonly float started = Game.Time;
        public bool Finished { get; private set; }
        public bool Interruptible => true;
        public float ClaimTime => Game.Time + 0.3f;
        public static bool CanStart(Car car, Ball ball, float opponentEta)
        {
            Vec3 delta = ball.location - car.Location;
            return !car.IsGrounded && car.Location.z > 180 && ball.location.z > 300 && car.Boost > 8 &&
                delta.z > 25 && delta.z < 420 && delta.Length() < 650 &&
                (car.Velocity - ball.velocity).Length() < 1000 && opponentEta > 0.6f;
        }
        public void Run(RUBot bot)
        {
            Car car = bot.Me;
            Vec3 delta = Ball.Location - car.Location;
            if (car.IsGrounded || delta.Length() > 850 || Ball.Location.z < 180 || Game.Time - started > 3 ||
                (car.Boost <= 0 && delta.Length() > 200)) { Finished = true; return; }
            if (bot is Stardust stardust && stardust.Options.FlipResets &&
                stardust.Situation.OpponentEta > 1.2f && FlipReset.CanStart(car, Ball.MainBall, bot.Jump))
            { bot.Action = new FlipReset(bot.Jump); return; }
            const float horizon = 0.12f;
            Ball prediction = Ball.Prediction.TrySample(Game.Time + horizon, out Ball sample) ? sample : Ball.MainBall.Predict(horizon);
            Vec3 lane = ControlMath.FlatUnit(bot.TheirGoal.Location - prediction.location, car.Forward);
            Vec3 contactNormal = ControlMath.Unit(lane * 0.48f + Vec3.Up * 0.88f, Vec3.Up);
            Vec3 target = prediction.location - contactNormal * (Ball.Radius + 40);
            Vec3 targetVelocity = prediction.velocity + lane * 80 + Vec3.Up * 80;
            Vec3 acceleration = PossessionControl.FlightAtHorizon(car, target, targetVelocity, horizon);
            Vec3 nose = ControlMath.Unit(acceleration, car.Forward);
            ControlMath.Aim(car, bot.Controller, nose, Vec3.Up);
            float closing = (car.Velocity - Ball.Velocity).Dot(ControlMath.Unit(delta, Vec3.Up));
            bool gentle = delta.Length() < 185 && closing > 160;
            bot.Controller.Boost = boost.Step(Game.Time, acceleration.Dot(car.Forward), car.Forward.Dot(nose), car.Boost, gentle);
            bot.Controller.Throttle = gentle ? 0 : 1;
            bot.Controller.Jump = false;
        }
    }

    /// <summary>Experimental, evidence-gated acquisition. Enable with STARDUST_FLIP_RESETS=1.</summary>
    public sealed class FlipReset : IPossessionAction
    {
        private readonly ResetEvidence evidence = new();
        private readonly BoostGate boost = new();
        private readonly float started = Game.Time;
        private float confirmedAt = float.NaN, firedAt = float.NaN;
        public bool Finished { get; private set; }
        public bool Interruptible => !float.IsFinite(firedAt);
        public float ClaimTime => Game.Time + 0.25f;
        public FlipReset(JumpState initialState)
        {
            // Capture spent state at selection, even if contact happens before the next control tick.
            evidence.Observe(initialState, false, false, 0, Game.Time);
        }
        public static bool CanStart(Car car, Ball ball, JumpState jump)
        {
            Vec3 delta = ball.location - car.Location;
            return !car.IsGrounded && car.Location.z > 350 && ball.location.z > 550 && car.Boost > 20 &&
                (jump.DoubleJumped || jump.Dodged) && delta.z > 60 && delta.z < 280 && delta.Length() < 360 &&
                (car.Velocity - ball.velocity).Length() < 650;
        }
        public void Run(RUBot bot)
        {
            Car car = bot.Me;
            Vec3 delta = Ball.Location - car.Location;
            if (car.Location.z < 180 || delta.Length() > 650 || Game.Time - started > 2)
            { Finished = true; return; }
            Vec3 towardBall = ControlMath.Unit(delta, Vec3.Up);
            Vec3 lane = ControlMath.FlatUnit(bot.TheirGoal.Location - Ball.Location, car.Forward);
            bool wheelsAligned = (-car.Up).Dot(towardBall) > 0.85f;
            bool confirmed = evidence.Observe(bot.Jump, bot.OwnTouchThisTick, wheelsAligned, car.Location.z, Game.Time);
            bot.Controller.Jump = false;
            bot.Controller.Boost = false;
            if (confirmed)
            {
                if (!float.IsFinite(confirmedAt)) confirmedAt = Game.Time;
                ControlMath.Aim(car, bot.Controller, towardBall, Vec3.Up);
                if (!float.IsFinite(firedAt) && Game.Time - confirmedAt > 0.08f && bot.Jump.CanDodge &&
                    delta.Length() < 230 && car.Forward.Dot(towardBall) > 0.8f && car.AngularVelocity.Length() < 2.5f)
                    firedAt = Game.Time;
                if (float.IsFinite(firedAt))
                {
                    bool pulse = Game.Time - firedAt < 0.05f;
                    bot.Controller.Jump = pulse;
                    if (pulse)
                    {
                        Vec3 local = ControlMath.FlatUnit(car.Local(towardBall), new Vec3(1, 0, 0));
                        bot.Controller.Pitch = -local.x;
                        bot.Controller.Yaw = local.y;
                        bot.Controller.Roll = 0;
                    }
                    Finished = Game.Time - firedAt > 0.8f;
                }
                else if (Game.Time - confirmedAt > 0.55f) Finished = true;
                return;
            }
            if (Game.Time - started > 1.35f) { Finished = true; return; }
            const float horizon = 0.08f;
            Ball prediction = Ball.MainBall.Predict(horizon);
            Vec3 target = prediction.location - Vec3.Up * (Ball.Radius + 18) - lane * 20;
            Vec3 acceleration = PossessionControl.FlightAtHorizon(car, target, prediction.velocity, horizon);
            if (delta.Length() > 220)
            {
                Vec3 nose = ControlMath.Unit(acceleration, car.Forward);
                ControlMath.Aim(car, bot.Controller, nose, -Vec3.Up);
                bot.Controller.Boost = boost.Step(Game.Time, acceleration.Dot(car.Forward), car.Forward.Dot(nose), car.Boost, false);
            }
            else
            {
                // Coast into wheel contact rather than boosting the ball away.
                ControlMath.Aim(car, bot.Controller, lane, -Vec3.Up);
            }
        }
    }

    public sealed class Recover : IAction
    {
        private readonly float started = Game.Time;
        public bool Finished { get; private set; }
        public bool Interruptible => true;
        public void Run(RUBot bot)
        {
            Car car = bot.Me;
            if (car.IsGrounded || Game.Time - started > 1.5f) { Finished = true; return; }
            float time = car.PredictLandingTime();
            time = float.IsFinite(time) ? System.Math.Clamp(time, 0, 2) : 0.3f;
            Vec3 normal = Field.NearestSurface(car.PredictLocation(time)).Normal;
            Vec3 tangent = car.Velocity - normal * car.Velocity.Dot(normal);
            Vec3 fallback = bot.TheirGoal.Location - car.Location;
            fallback -= normal * fallback.Dot(normal);
            ControlMath.Aim(car, bot.Controller, ControlMath.Unit(tangent, ControlMath.Unit(fallback, car.Forward)), normal);
            bot.Controller.Throttle = 1;
            bot.Controller.Boost = false;
            bot.Controller.Jump = false;
            bot.Controller.Handbrake = time < 0.1f && tangent.Length() > 800;
        }
    }
}
