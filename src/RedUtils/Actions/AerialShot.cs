using System;
using RedUtils.Math;

namespace RedUtils
{
    /// <summary>The existing timed aerial-shot solver, with corrected turn geometry and expired-time handling.</summary>
    public class AerialShot : Shot
    {
        public override bool Finished { get; internal set; }
        public override bool Interruptible { get; internal set; }
        public override BallSlice Slice { get; internal set; }
        public override Vec3 ShotTarget { get; internal set; }
        public override Vec3 TargetLocation { get; internal set; }
        public override Vec3 ShotDirection { get; internal set; }
        public Vec3 DriveLocation { get; internal set; }
        public Drive DriveAction { get; internal set; }
        public bool DoubleJumping { get; internal set; }
        private readonly bool _jumpImmediatly;
        private readonly float _startBoostAmount;
        private bool _currentlyDoubleJumping, _aerialing, _jumped;
        private float _elapsedTime;
        private int _step;

        public AerialShot(Car car, BallSlice slice, Vec3 shotTarget)
        {
            Interruptible = true;
            Slice = slice;
            ShotTarget = shotTarget;
            _startBoostAmount = car.Boost;
            // An already airborne car has no ground launch to execute or predict.
            _jumped = !car.IsGrounded;
            SetTargetLocation(car);
            _jumpImmediatly = car.Velocity.FlatAngle(car.Location.Direction(TargetLocation), car.Up) < 0.6f ||
                car.Velocity.FlatLen(car.Up) < 500 || !car.IsGrounded;
            DriveLocation = Drive.GetEta(car, TargetLocation.Flatten(), false) <= Drive.GetEta(car, TargetLocation, false) ?
                TargetLocation.Flatten() : TargetLocation;
            DriveAction = new Drive(car, DriveLocation, Drive.GetDistance(car, DriveLocation) / MathF.Max(Slice.Time - Game.Time, 0.001f));
        }
        private void SetTargetLocation(Car car)
        {
            float timeRemaining = MathF.Max(Slice.Time - Game.Time, 0.001f);
            Ball ballAfterHit = Slice.ToBall();
            Vec3 carFinVel = ((Slice.Location - car.Location) / timeRemaining).Cap(0, Car.MaxSpeed);
            ballAfterHit.velocity = (carFinVel * 6 + Slice.Velocity) / 7;
            Vec3 directionToScore = Slice.Location.FlatDirection(ShotTarget);
            float velocityDiff = (carFinVel - Slice.Velocity).Length();
            float timeToScore = Slice.Location.FlatDist(ShotTarget) / Utils.Cap(velocityDiff * Utils.ShotPowerModifier(velocityDiff) +
                ballAfterHit.velocity.Dot(directionToScore), 500, Ball.MaxSpeed);
            ballAfterHit.velocity = (carFinVel + Slice.Velocity * 2) / 3;
            ShotDirection = ballAfterHit.PredictLocation(timeToScore).Direction(ShotTarget);
            TargetLocation = Slice.Location - ShotDirection * 155;
            Vec3 normal = Field.NearestSurface(TargetLocation).Normal;
            float distFromSurface = (TargetLocation - Field.LimitToNearestSurface(TargetLocation)).Dot(normal);
            if (distFromSurface < 50)
            {
                float angle = MathF.Asin(Utils.Cap((distFromSurface - 50) / 155, -1, 1));
                ShotDirection = (ShotDirection.FlatNorm(normal) * MathF.Cos(angle) + normal * MathF.Sin(angle)).Normalize();
                TargetLocation = Slice.Location - ShotDirection * 155;
            }
        }
        public override void Run(RUBot bot)
        {
            float timeRemaining = Slice.Time - Game.Time;
            if (!float.IsFinite(timeRemaining) || timeRemaining <= 0) { Finished = true; return; }
            if (!_aerialing)
            {
                DriveAction.TargetSpeed = Drive.GetDistance(bot.Me, DriveLocation) / timeRemaining;
                DriveAction.Run(bot);
                if (_startBoostAmount < bot.Me.Boost || bot.Me.Boost == 0 || !ShotValid()) Finished = true;
                else if (_jumpImmediatly || MathF.Abs(bot.Controller.Steer) < 0.2f)
                {
                    if (CanHit(bot.Me, out bool doubleJumping))
                    { DoubleJumping = doubleJumping; _aerialing = true; }
                    else Finished = true;
                }
            }
            else
            {
                Interruptible = false;
                _elapsedTime += bot.DeltaTime;
                _currentlyDoubleJumping = false;
                if (!_jumped)
                {
                    if (_elapsedTime <= Car.JumpMaxDuration) bot.Controller.Jump = true;
                    else if (_step < 3 && DoubleJumping) { bot.Controller.Jump = false; _step++; }
                    else if (_step < 6 && DoubleJumping)
                    { bot.Controller.Jump = true; _currentlyDoubleJumping = true; _step++; }
                    else _jumped = true;
                }
                Vec3 finPos = _jumped ? bot.Me.PredictLocation(timeRemaining) :
                    DoubleJumping ? bot.Me.LocationAfterDoubleJump(timeRemaining, _elapsedTime) : bot.Me.LocationAfterJump(timeRemaining, _elapsedTime);
                Vec3 offset = TargetLocation - finPos;
                float requiredAccel = 2 * offset.Length() / MathF.Pow(timeRemaining, 2);
                bot.AimAt(bot.Me.Location + offset, _jumped ? bot.Me.Location.Direction(Slice.Location) : Vec3.Up);
                bot.Controller.Boost = offset.Dot(bot.Me.Forward) / timeRemaining >=
                    (Car.BoostAccel + Car.AirThrottleAccel) * MathF.Max(bot.DeltaTime, 13f / 120f) && offset.Angle(bot.Me.Forward) < 0.4f;
                bot.Controller.Throttle = Utils.Cap(offset.Dot(bot.Me.Forward) / timeRemaining /
                    (Car.AirThrottleAccel * MathF.Max(bot.DeltaTime, 1f / 120f)), -1, 1);
                if (_currentlyDoubleJumping)
                { bot.Controller.Steer = 0; bot.Controller.Yaw = 0; bot.Controller.Pitch = 0; bot.Controller.Roll = 0; }
                if ((_jumped && offset.Length() > 50 && timeRemaining > 0.5f && requiredAccel * 0.8f > Car.AirThrottleAccel &&
                    (bot.Me.Boost == 0 || requiredAccel * 0.8f > Car.BoostAccel + Car.AirThrottleAccel)) ||
                    (!ShotValid() && timeRemaining > 0.5f) || (bot.Me.IsGrounded && _jumped)) Finished = true;
                else if (_jumped && bot.Jump.CanDodge && timeRemaining < 0.1f && offset.Length() < 100)
                    bot.Action = new Dodge(ShotDirection.FlatNorm(), 0.1f);
                else if (offset.Length() < 50 && !_currentlyDoubleJumping)
                    bot.AimAt(bot.Me.Location + ShotDirection, bot.Me.Location.Direction(Slice.Location));
            }
        }
        /// <summary>Both components of a circular turn displacement have units of distance.</summary>
        public static Vec3 TurnDisplacement(Vec3 driveDirection, Vec3 turnSide, float radius, float angle) =>
            turnSide * radius * (1 - MathF.Cos(angle)) + driveDirection * radius * MathF.Sin(angle);

        public override bool IsValid(Car car)
        {
            bool jumpImmediatly = CanHit(car) && _jumpImmediatly;
            bool shouldTurn = car.IsGrounded;
            if (shouldTurn)
            {
                Car carAfterTurn = new(car);
                Drive.GetDistance(car, DriveLocation, DriveAction.Backwards, out float angle, out float radius);
                Vec3 normal = Field.NearestSurface(car.Location).Normal;
                Vec3 driveDirection = car.Forward.FlatNorm(normal) * (DriveAction.Backwards ? -1 : 1);
                Vec3 turnSide = car.Right.FlatNorm(normal) * MathF.Sign(car.Right.Dot(DriveLocation - car.Location));
                carAfterTurn.Location = car.Location + TurnDisplacement(driveDirection, turnSide, radius, angle);
                carAfterTurn.Forward = carAfterTurn.Location.FlatDirection(DriveLocation, normal);
                carAfterTurn.Up = normal;
                carAfterTurn.Velocity = carAfterTurn.Forward * Drive.SpeedAfterTurn(car.Velocity.Length(), angle, DriveAction.Backwards ? 0.8f : 1);
                shouldTurn = CanHit(carAfterTurn) && !_jumpImmediatly;
            }
            return TargetLocation.z > 300 && (jumpImmediatly || shouldTurn);
        }
        private bool CanHit(Car car) => GetBoostEstimate(car, false) >= 0 || GetBoostEstimate(car, true) >= 0;
        private bool CanHit(Car car, out bool doubleJumping)
        {
            float jumpBoostEstimate = GetBoostEstimate(car, false);
            float doubleJumpBoostEstimate = GetBoostEstimate(car, true);
            doubleJumping = doubleJumpBoostEstimate >= 0 && (doubleJumpBoostEstimate < jumpBoostEstimate || jumpBoostEstimate < 0);
            return jumpBoostEstimate >= 0 || doubleJumpBoostEstimate >= 0;
        }
        private float GetBoostEstimate(Car car, bool doubleJumping)
        {
            float timeRemaining = Slice.Time - Game.Time;
            if (!float.IsFinite(timeRemaining) || timeRemaining <= 0) return -1;
            Vec3 finPos = car.IsGrounded ? (doubleJumping ? car.LocationAfterDoubleJump(timeRemaining, 0) : car.LocationAfterJump(timeRemaining, 0)) : car.PredictLocation(timeRemaining);
            Vec3 finVel = car.IsGrounded ? (doubleJumping ? car.VelocityAfterDoubleJump(timeRemaining, 0) : car.VelocityAfterJump(timeRemaining, 0)) : car.PredictVelocity(timeRemaining);
            Vec3 deltaX = TargetLocation - finPos;
            Vec3 direction = deltaX.Normalize();
            float angle = MathF.Max(direction.Angle(car.Forward), 0.0001f);
            float turnTime = 0.6f * (2 * MathF.Sqrt(angle / 9));
            float tau1 = turnTime * Utils.Cap(1 - 0.4f / angle, 0, 1);
            if (tau1 >= timeRemaining) return -1;
            float requiredAccel = 2 * deltaX.Length() / MathF.Pow(timeRemaining - tau1, 2);
            float ratio = requiredAccel / (Car.BoostAccel + Car.AirThrottleAccel);
            float tau2 = timeRemaining - (timeRemaining - tau1) * MathF.Sqrt(1 - Utils.Cap(ratio, 0, 1));
            Vec3 velocityEstimate = finVel + (Car.BoostAccel + Car.AirThrottleAccel) * (tau2 - tau1) * direction;
            float boostEstimate = (tau2 - tau1) * Car.BoostConsumption;
            bool enoughBoost = boostEstimate < car.Boost * 0.9f;
            bool enoughTime = MathF.Abs(ratio) < 0.9f;
            return velocityEstimate.Length() < Car.MaxSpeed * 0.9f && enoughBoost && enoughTime ? boostEstimate : -1;
        }
    }
}
