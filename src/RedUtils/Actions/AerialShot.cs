using System;
using RedUtils.Math;

namespace RedUtils
{
	/// <summary>An aerial shot action, where the car flys into the ball</summary>
	public class AerialShot : Shot
	{
		/// <summary>Whether or not this aerial has finished</summary>
		public override bool Finished { get; internal set; }
		/// <summary>Whether or not this aerial can be interrupted</summary>
		public override bool Interruptible { get; internal set; }

		/// <summary>The future ball state at which time we are planning to hit the aerial</summary>
		public override BallSlice Slice { get; internal set; }
		/// <summary>The exact position we will hit the ball towards</summary>
		public override Vec3 ShotTarget { get; internal set; }
		/// <summary>The final position of the car at the point of collision</summary>
		public override Vec3 TargetLocation { get; internal set; }
		/// <summary>The direction from the car to the ball at the point of collision</summary>
		public override Vec3 ShotDirection { get; internal set; }

		/// <summary>The location to drive to</summary>
		public Vec3 DriveLocation { get; internal set; }
		/// <summary>The drive sub action</summary>
		public Drive DriveAction { get; internal set; }
		/// <summary>Whether or not we are going to do a double jump aerial</summary>
		public bool DoubleJumping { get; internal set; }

		/// <summary>Whether or not we should jump immediatly or turn and then jump</summary>
		private readonly bool _jumpImmediatly = false;
		/// <summary>The amount of boost we have when starting this action</summary>
		/// <summary>Whether or not the car is currently double jumping</summary>
		private bool _currentlyDoubleJumping = false;
		/// <summary>Whether or not the car is no longer turning to face the target</summary>
		private bool _aerialing = false;
		/// <summary>Whether or not we have finished launching for the aerial</summary>
		private bool _jumped = false;
		/// <summary>The amount of time that has passed since the start of the aerial</summary>
		private float _elapsedTime = 0;
		/// <summary>Whether one real controller output has released jump before the second press.</summary>
		private bool _releaseObserved = false;
		/// <summary>Whether the second-jump rising edge has already been emitted.</summary>
		private bool _secondJumpPressed = false;
		private readonly ImpulseBoostGate _boostGate = new();

		/// <summary>Initializes a new aerial shot, with a specific ball slice and a shot target</summary>
		public AerialShot(Car car, BallSlice slice, Vec3 shotTarget)
		{
			// Initializes some default values
			Finished = false;
			Interruptible = true;

			Slice = slice;
			ShotTarget = shotTarget;
			// Airborne entries must not execute or predict a fresh ground launch or waste
			// their first control tick on a ground-driving prelude.
			_jumped = !car.IsGrounded;
			_aerialing = !car.IsGrounded;

			// Sets the target location and shot direction such that we hit the ball towards our target
			SetTargetLocation(car);

			// Figures out whether or not the car should jump immediatly, or wait until the car faces the target
			_jumpImmediatly = car.Velocity.FlatAngle(car.Location.Direction(TargetLocation), car.Up) < 0.6f || car.Velocity.FlatLen(car.Up) < 500 || !car.IsGrounded;

			// Sets up the drive location and action
			DriveLocation = Drive.GetEta(car, TargetLocation.Flatten(), false) <= Drive.GetEta(car, TargetLocation, false) ? TargetLocation.Flatten() : TargetLocation;
			DriveAction = new Drive(car, DriveLocation, Drive.GetDistance(car, DriveLocation) / MathF.Max(Slice.Time - Game.Time, 0.001f));
		}

		/// <summary>Sets the target location and the shot direction based on the velocity of the ball and other factors</summary>
		private void SetTargetLocation(Car car)
		{
			// How much time until we should hit the ball
			float timeRemaining = MathF.Max(Slice.Time - Game.Time, 0.001f);

			// Predicts the ball's state after contact
			Ball ballAfterHit = Slice.ToBall();
			Vec3 carFinVel = ((Slice.Location - car.Location) / timeRemaining).Cap(0, Car.MaxSpeed);
			ballAfterHit.velocity = (carFinVel * 6 + Slice.Velocity) / 7;

			// Predicts how long it will take the ball to hit the target after being hit
			Vec3 directionToScore = Slice.Location.FlatDirection(ShotTarget);
			float velocityDiff = (carFinVel - Slice.Velocity).Length();
			float timeToScore = Slice.Location.FlatDist(ShotTarget) / Utils.Cap(velocityDiff * Utils.ShotPowerModifier(velocityDiff) + ballAfterHit.velocity.Dot(directionToScore), 500, Ball.MaxSpeed);

			// Calculates the shot direction, and target location
			ballAfterHit.velocity = (carFinVel + Slice.Velocity * 2) / 3;
			ShotDirection = ballAfterHit.PredictLocation(timeToScore).Direction(ShotTarget);
			TargetLocation = Slice.Location - ShotDirection * 155;

			// Gets the surface normal, for the closest surface to the target location
			Vec3 normal = Field.NearestSurface(TargetLocation).Normal;

			// if the target location is too close to the wall, change the shot direction and the target location so it is flatter against the wall
			float distFromSurface = (TargetLocation - Field.LimitToNearestSurface(TargetLocation)).Dot(normal);
			if (distFromSurface < 50)
			{
				float angle = MathF.Asin(Utils.Cap((distFromSurface - 50) / 155, -1, 1));
				ShotDirection = (ShotDirection.FlatNorm(normal) * MathF.Cos(angle) + normal * MathF.Sin(angle)).Normalize();
				TargetLocation = Slice.Location - ShotDirection * 155;
			}
		}

		/// <summary>Perfoms this aerial</summary>
		public override void Run(RUBot bot)
		{
			// How much time until we should hit the ball
			float timeRemaining = Slice.Time - Game.Time;
			if (!float.IsFinite(timeRemaining) || timeRemaining <= 0)
			{
				Finished = true;
				return;
			}

			if (!_aerialing)
			{
				// When we aren't aerialing, just drive towards the ball
				DriveAction.TargetSpeed = Drive.GetDistance(bot.Me, DriveLocation) / timeRemaining;
				DriveAction.Run(bot);

				// A boost pickup cannot make a shot less feasible, and a zero-boost coast/jump
				// can still be valid. Feasibility below decides whether thrust is actually required.
				if (!ShotValid())
				{
					Finished = true;
				}
				else if (_jumpImmediatly || MathF.Abs(bot.Controller.Steer) < 0.2f)
				{
					// Once we are ready to aerial, let's check if we even can
					if (CanHit(bot.Me, out bool doubleJumping))
					{
						// If we can, then we set _aerialing to true, and set DoubleJumping to the out variable through CanHit
						DoubleJumping = doubleJumping;
						_aerialing = true;
					}
					else
					{
						// Otherwise, we stop this aerial
						Finished = true;
					}
				}
			}
			else
			{
				// Now that we are up in the air, we set interruptible to false, as we don't want to be interrupted while aerialing
				Interruptible = false;
				_elapsedTime += bot.DeltaTime;
				_currentlyDoubleJumping = false;

				if (!_jumped)
				{
					if (_elapsedTime <= Car.JumpMaxDuration)
					{
						// Holds the jump button for the first 0.2 seconds, giving us the maximum acceleration possible by that first jump
						bot.Controller.Jump = true;
					}
					else if (DoubleJumping && !_releaseObserved)
					{
						// A second jump is edge-triggered. Emit exactly one real release output.
						bot.Controller.Jump = false;
						_releaseObserved = true;
					}
					else if (DoubleJumping && !_secondJumpPressed)
					{
						// Emit one rising edge for the neutral second jump.
						bot.Controller.Jump = true;
						_currentlyDoubleJumping = true;
						_secondJumpPressed = true;
					}
					else
					{
						// Launch sequencing is complete; subsequent ticks are normal aerial control.
						_jumped = true;
					}
				}

				// Predict the uncontrolled endpoint first. While still launching, the jump model already
				// includes gravity and jump forces. Once launched, solve the finite-time acceleration
				// directly from the current car state with gravity feed-forward.
				Vec3 finPos = _jumped ? bot.Me.PredictLocation(timeRemaining) :
					(DoubleJumping ? bot.Me.LocationAfterDoubleJump(timeRemaining, _elapsedTime) :
					bot.Me.LocationAfterJump(timeRemaining, _elapsedTime));
				Vec3 offset = TargetLocation - finPos;
				Vec3 requiredControl = _jumped
					? AerialPhysics.RequiredAcceleration(bot.Me.Location, bot.Me.Velocity,
						TargetLocation, timeRemaining, Game.Gravity)
					: offset * (2 / MathF.Max(timeRemaining * timeRemaining, 0.000001f));
				float requiredAccel = requiredControl.Length();

				Vec3 desiredForward = requiredAccel > 1
					? requiredControl / requiredAccel
					: (offset.Length() > 10 ? offset.Normalize() : ShotDirection);
				if (desiredForward.Length() < 0.001f || !ControlFinite(desiredForward))
					desiredForward = bot.Me.Forward;

				bot.AimAt(bot.Me.Location + desiredForward,
					_jumped ? bot.Me.Location.Direction(Slice.Location) : Vec3.Up);

				// Air throttle supplies the small continuous component; boost supplies only the residual
				// through minimum-duration impulse pulses. Avoid starting an unnecessary new burst when
				// already in the final contact envelope.
				float forwardDemand = MathF.Max(0, requiredControl.Dot(bot.Me.Forward));
				float alignment = bot.Me.Forward.Dot(desiredForward);
				bool contactEnvelope = _jumped && timeRemaining < AerialPhysics.MinimumBoostTime &&
					offset.Length() < 100 && forwardDemand < Car.BoostAccel * 0.5f;
				bot.Controller.Boost = _boostGate.Step(Game.Time, forwardDemand, alignment,
					bot.Me.Boost, contactEnvelope);
				bot.Controller.Throttle = Utils.Cap(requiredControl.Dot(bot.Me.Forward) /
					Car.AirThrottleAccel, -1, 1);

				// If we are currently double jumping, let go of all direction keys so we don't flip on accident
				if (_currentlyDoubleJumping)
				{
					bot.Controller.Steer = 0;
					bot.Controller.Yaw = 0;
					bot.Controller.Pitch = 0;
					bot.Controller.Roll = 0;
				}

				// If the aerial is finished, or the finite-time correction exceeds the available
				// forward acceleration budget by a wide margin, stop instead of blindly boosting.
				float availableAccel = bot.Me.Boost > 0
					? Car.BoostAccel + Car.AirThrottleAccel
					: Car.AirThrottleAccel;
				if (timeRemaining <= 0f || (_jumped && offset.Length() > 50 && timeRemaining > 0.5f &&
					requiredAccel * 0.8f > availableAccel) || (!ShotValid() && timeRemaining > 0.5f) ||
					(bot.Me.IsGrounded && _jumped))
				{
					Finished = true;
				}
				else if (_jumped && bot.Jump.CanDodge && timeRemaining < 0.1f && offset.Length() < 100)
				{
					// If it's possible to dodge before hitting the ball, why not do it?
					bot.Action = new Dodge(ShotDirection.FlatNorm(), 0.1f);
				}
				else if (offset.Length() < 50 && !_currentlyDoubleJumping)
				{
					// When we are about to hit the ball, face in the shot direction
					bot.AimAt(bot.Me.Location + ShotDirection, bot.Me.Location.Direction(Slice.Location));
				}
			}
		}

		/// <summary>
		/// Circular displacement while turning on a surface. Both orthogonal components
		/// are radius-scaled; omitting the radius from the forward term underestimates travel.
		/// </summary>
		public static Vec3 TurnDisplacement(Vec3 driveDirection, Vec3 turnSide, float radius, float angle)
		{
			return turnSide * radius * (1 - MathF.Cos(angle)) + driveDirection * radius * MathF.Sin(angle);
		}

		/// <summary>Returns whether this aerial is possible</summary>
		public override bool IsValid(Car car)
		{
			// if the car is grounded, check if we should turn to face the ball before aerialing
			bool jumpImmediatly = CanHit(car) && _jumpImmediatly;
			bool shouldTurn = car.IsGrounded;
			if (shouldTurn)
			{
				Car carAfterTurn = new Car(car);
				Drive.GetDistance(car, DriveLocation, DriveAction.Backwards, out float angle, out float radius);

				Vec3 normal = Field.NearestSurface(car.Location).Normal;
				Vec3 driveDirection = car.Forward.FlatNorm(normal) * (DriveAction.Backwards ? -1 : 1);
				Vec3 turnSide = car.Right.FlatNorm(normal) * MathF.Sign(car.Right.Dot(DriveLocation - car.Location));

				// Predicts the car's location, orientation, and velocity after turning
				carAfterTurn.Location = car.Location + TurnDisplacement(driveDirection, turnSide, radius, angle);
				carAfterTurn.Forward = carAfterTurn.Location.FlatDirection(DriveLocation, normal);
				carAfterTurn.Up = normal;
				carAfterTurn.Velocity = carAfterTurn.Forward * Drive.SpeedAfterTurn(car.Velocity.Length(), angle, DriveAction.Backwards ? 0.8f : 1);

				shouldTurn = CanHit(carAfterTurn) && !_jumpImmediatly;
			}

			// Returns true if the target is high enough, and we can get to the ball, either by turning first or jumping immediatly
			return TargetLocation.z > 300 && (jumpImmediatly || shouldTurn);
		}

		/// <summary>Whether or not this aerial is possible</summary>
		private bool CanHit(Car car)
		{
			float jumpBoostEstimate = GetBoostEstimate(car, false);
			float doubleJumpBoostEstimate = GetBoostEstimate(car, true);

			return jumpBoostEstimate >= 0 || doubleJumpBoostEstimate >= 0;
		}

		/// <summary>Whether or not this aerial is possible</summary>
		/// <param name="doubleJumping">Whether or not we should double jump</param>
		private bool CanHit(Car car, out bool doubleJumping)
		{
			float jumpBoostEstimate = GetBoostEstimate(car, false);
			float doubleJumpBoostEstimate = GetBoostEstimate(car, true);

			doubleJumping = doubleJumpBoostEstimate >= 0 && (doubleJumpBoostEstimate < jumpBoostEstimate || jumpBoostEstimate < 0);

			return jumpBoostEstimate >= 0 || doubleJumpBoostEstimate >= 0;
		}

		/// <summary>Returns an estimate for how much boost an aerial takes (returns -1 if the aerial isn't possible)</summary>
		private float GetBoostEstimate(Car car, bool doubleJumping)
		{
			float timeRemaining = Slice.Time - Game.Time;
			if (!float.IsFinite(timeRemaining) || timeRemaining <= 0)
				return -1;

			Vec3 finPos = car.IsGrounded ? (doubleJumping ? car.LocationAfterDoubleJump(timeRemaining, 0) : car.LocationAfterJump(timeRemaining, 0)) : car.PredictLocation(timeRemaining);
			Vec3 finVel = car.IsGrounded ? (doubleJumping ? car.VelocityAfterDoubleJump(timeRemaining, 0) : car.VelocityAfterJump(timeRemaining, 0)) : car.PredictVelocity(timeRemaining);

			Vec3 deltaX = TargetLocation - finPos;
			float correction = deltaX.Length();
			if (!float.IsFinite(correction) || !ControlFinite(finVel)) return -1;
			// A zero correction is already on the required ballistic path. Treating the
			// zero vector as a direction produces a fictitious 90-degree reorientation.
			if (correction < 0.5f)
				return finVel.Length() <= Car.MaxSpeed + 1 ? 0 : -1;
			Vec3 direction = deltaX / correction;
			float angle = MathF.Max(direction.Angle(car.Forward), 0.0001f);
			float turnTime = 0.6f * (2 * MathF.Sqrt(angle / 9));

			float tau1 = turnTime * Utils.Cap(1 - 0.4f / angle, 0, 1);
			if (tau1 >= timeRemaining)
				return -1;
			float requiredAccel = 2 * deltaX.Length() / MathF.Pow(timeRemaining - tau1, 2);
			float ratio = requiredAccel / (Car.BoostAccel + Car.AirThrottleAccel);
			float tau2 = timeRemaining - (timeRemaining - tau1) * MathF.Sqrt(1 - Utils.Cap(ratio, 0, 1));
			Vec3 velocityEstimate = finVel + (Car.BoostAccel + Car.AirThrottleAccel) * (tau2 - tau1) * direction;
			float boostEstimate = (tau2 - tau1) * Car.BoostConsumption;
			bool enoughBoost = boostEstimate <= MathF.Max(0, car.Boost * 0.9f);
			bool enoughTime = MathF.Abs(ratio) < 0.9f;

			return (velocityEstimate.Length() <= Car.MaxSpeed + 1 && enoughBoost && enoughTime) ? boostEstimate : -1;
		}

		private static bool ControlFinite(Vec3 value) =>
			float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
	}
}
