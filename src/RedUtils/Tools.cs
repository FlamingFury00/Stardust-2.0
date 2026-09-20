using System;
using System.Collections.Generic;
using System.Linq;
using RedUtils.Math;
using RLBot.Flat;
using RLBot.Manager;

namespace RedUtils
{
	public readonly struct AimAngles
	{
		public readonly float Pitch;
		public readonly float Yaw;
		public readonly float Roll;
		public AimAngles(float pitch, float yaw, float roll) { Pitch = pitch; Yaw = yaw; Roll = roll; }
		public float this[int index] => index switch { 0 => Pitch, 1 => Yaw, 2 => Roll, _ => float.NaN };
	}

	public abstract partial class RUBot : Bot
	{
		/// <summary>Encapsulates a function that finds the best shot for any ball slice, and target.</summary>
		public delegate Shot ShotCheck(BallSlice slice, Target target);

		/// <summary>Throttles and boosts to reach the given target speed</summary>
		/// <returns>The current forward speed of the car</returns>
		public float Throttle(float targetSpeed, bool backwards = false)
		{
			float carSpeed = Me.Local(Me.Velocity).x; // The car's speed in the forward direction
			float speedDiff = (targetSpeed * (backwards ? -1 : 1)) - carSpeed;
			Controller.Throttle = Utils.Cap(MathF.Pow(speedDiff, 2) * MathF.Sign(speedDiff) / 1000, -1, 1);
			Controller.Boost = targetSpeed > 1400 && speedDiff > 50 && carSpeed < 2250 && Controller.Throttle == 1 && !backwards;
			return carSpeed;
		}

		/// <summary>Turns to face a given target</summary>
		/// <param name="up">Which direction to face your roof</param>
		/// <returns>The target angles for pitch, yaw, and roll</returns>
		public static AimAngles CalculateAimAngles(Car car, Vec3 targetLocation, Vec3 up = new(), bool backwards = false)
		{
			Vec3 localTarget = car.Local(targetLocation - car.Location) * (backwards ? -1 : 1);
			Vec3 safeUp = up.Length() != 0 ? up : Vec3.Up;
			Vec3 localUp = car.Local(safeUp.Normalize());
			return new AimAngles(
				MathF.Atan2(localTarget.z, localTarget.x),
				MathF.Atan2(localTarget.y, localTarget.x),
				MathF.Atan2(localUp.y, localUp.z));
		}

		public AimAngles AimAt(Vec3 targetLocation, Vec3 up = new(), bool backwards = false)
		{
			AimAngles targetAngles = CalculateAimAngles(Me, targetLocation, up, backwards);
			Controller.Steer = SteerPD(targetAngles.Yaw, -Me.LocalAngularVelocity[2] * 0.01f) * (backwards ? -1 : 1);
			Controller.Pitch = SteerPD(targetAngles.Pitch, Me.LocalAngularVelocity[1] * 0.2f);
			Controller.Yaw = SteerPD(targetAngles.Yaw, -Me.LocalAngularVelocity[2] * 0.15f);
			Controller.Roll = SteerPD(targetAngles.Roll, Me.LocalAngularVelocity[0] * 0.25f);
			return targetAngles;
		}

		/// <summary>A Proportional-Derivative control loop used for the "AimAt" function</summary>
		private static float SteerPD(float angle, float rate)
		{
			return Utils.Cap(MathF.Pow(35 * (angle + rate), 3) / 10, -1f, 1f);
		}

		/// <summary>Searches through the ball prediction for the first valid shot given by the ShotCheck</summary>
		/// <param name="shotCheck">The function that determines which shot to go for, if any</param>
		/// <param name="target">The final resting place of the ball after we hit it (hopefully)</param>
		public static Shot FindShot(ShotCheck shotCheck, Target target)
		{
			return shotCheck(Ball.Prediction.Find(slice => shotCheck(slice, target) != null), target);
		}

		/// <summary>The default shot check. Will go for pretty much anything it can</summary>
		/// <param name="slice">The future moment of the ball we are aiming to hit</param>
		/// <param name="target">The final resting place of the ball after we hit it (hopefully)</param>
		public Shot DefaultShotCheck(BallSlice slice, Target target)
		{
			if (slice != null) // Check if the slice even exists
			{
				float timeRemaining = slice.Time - Game.Time;
				// If a teammate has an earlier or equal claim, reject this slice entirely to avoid double commit
				if (HasTeammateEarlierShot(slice.Time))
				{
					return null;
				}

				// Check first if the slice is in the future and if it's even possible to shoot at our target
				if (timeRemaining > 0 && target.Fits(slice.Location))
				{
					Ball ballAfterHit = slice.ToBall();
					Vec3 carFinVel = ((slice.Location - Me.Location) / timeRemaining).Cap(0, Car.MaxSpeed);
					ballAfterHit.velocity = carFinVel + slice.Velocity.Flatten(carFinVel.Normalize()) * 0.8f;
					Vec3 shotTarget = target.Clamp(ballAfterHit);

					// First, check if we can aerial
					AerialShot aerialShot = new AerialShot(Me, slice, shotTarget);
					if (aerialShot.IsValid(Me) && !HasTeammateEarlierShot(slice.Time))
					{
						return aerialShot; // If so, go for it!
					}

					// If we can't aerial, let's try a ground shot
					GroundShot groundShot = new GroundShot(Me, slice, shotTarget);
					if (groundShot.IsValid(Me) && !HasTeammateEarlierShot(slice.Time))
					{
						return groundShot;
					}

					// Otherwise, we'll try a jump shot
					JumpShot jumpShot = new JumpShot(Me, slice, shotTarget);
					if (jumpShot.IsValid(Me) && !HasTeammateEarlierShot(slice.Time))
					{
						return jumpShot;
					}

					// And lastly, a double jump shot
					DoubleJumpShot doubleJumpShot = new DoubleJumpShot(Me, slice, shotTarget);
					if (doubleJumpShot.IsValid(Me) && !HasTeammateEarlierShot(slice.Time))
					{
						return doubleJumpShot;
					}
				}
			}

			return null; // if none of those work, we'll just return null (meaning no shot was found)
		}

		public bool IsClosestKickoff(Car ownCar)
		{
			// distance is calculated as distance + index - boost
			// boost added to allow the bot with the most boost to go for the ball in situations
			// where 2 bots are more or less equally close
			if (Teammates.Count == 0)
			{
				return true;
			}

			double ownDistance = (ownCar.Location - Ball.Location).Length() + ownCar.Index;
			foreach (Car car in Teammates.Concat(new List<Car> { Me }))
			{
				if (ownCar.Index == car.Index)
				{
					continue;
				}

				double otherDistance = (car.Location - Ball.Location).Length() + car.Index;
				if (otherDistance < ownDistance)
				{
					return false;
				}
			}

			return true;
		}

		public bool IsSecondClosestKickoff()
		{
			if (IsClosestKickoff(Me))
			{
				return false;
			}

			if (Teammates.Count < 1)
			{
				return false;
			}

			double ownDistance = (Me.Location - Ball.Location).Length() + Me.Index;
			int closestIndex = -1;
			foreach (Car car in Teammates)
			{
				if (IsClosestKickoff(car))
				{
					closestIndex = car.Index;
				}
			}

			if (closestIndex == Index)
			{
				return false;
			}

			foreach (Car car in Teammates)
			{
				if (car.Index == closestIndex)
				{
					continue;
				}

				double otherDistance = (car.Location - Ball.Location).Length() + car.Index;
				if (otherDistance < ownDistance)
				{
					return false;
				}
			}

			return true;
		}

		public bool IsLastOneBack()
		{
			// don't use on defense
			double myY = Me.Location.y * Field.Side(Team);
			foreach (Car car in Teammates)
			{
				if (car.Location.y * Field.Side(Team) > myY)
				{
					return false;
				}
			}
			return true;
		}

		public bool InGoalArea()
		{
			if (System.Math.Abs(Me.Location.y) > 5050)
			{
				if (System.Math.Abs(Me.Location.x) < 880)
				{
					return true;
				}
			}
			return false;
		}

		public bool IsAheadOfBall()
		{
			return (Me.Location.y > Ball.Location.y + 500 && Team == 0) ||
				   (Me.Location.y < Ball.Location.y - 500 && Team == 1);
		}

		public static bool IsAheadOfBall2(Vec3 location, int team)
		{
			return (location.y > Ball.Location.y + 500 && team == 0) ||
				   (location.y < Ball.Location.y - 500 && team == 1);
		}

		public Tuple<bool, int> DemoRotation()
		{
			List<Car> possibleCars = new();
			foreach (Car car in Opponents)
			{
				if (car.Location.y * Field.Side(Team) < -4000)
				{
					double distanceToTarget = (Me.Location - car.Location).Length();
					float velocity = Me.Velocity.Length();
					float velocityNeeded = 2200 - velocity;
					float timeBoostingRequired = velocityNeeded / 991.666f;
					float boostRequired = 33.3f * timeBoostingRequired;
					float distanceRequired = (velocity * timeBoostingRequired) + (0.5f * 991.666f * (timeBoostingRequired * timeBoostingRequired));

					if (velocity < 2200)
					{
						if (Me.Boost < boostRequired)
						{
							continue;
						}
						else if (distanceRequired > distanceToTarget)
						{
							continue;
						}
						possibleCars.Add(car);
					}

					else
					{
						possibleCars.Add(car);
					}
				}
			}

			if (possibleCars.Count == 0)
			{
				return Tuple.Create(false, -1);
			}
			if (possibleCars.Count == 1)
			{
				return Tuple.Create(true, possibleCars[0].Index);
			}
			else
			{
				possibleCars.Sort((car1, car2) => (TheirGoal.Location - car1.Location).Length().CompareTo((TheirGoal.Location - car2.Location).Length()));
				return Tuple.Create(true, possibleCars[0].Index);
			}
		}

		public int FriendsAheadOfBall()
		{
			int count = 0;
			foreach (Car car in Teammates)
			{
				if ((car.Location.y > Ball.Location.y + 1500 && Team == 0) ||
					(car.Location.y < Ball.Location.y - 1500 && Team == 1))
				{
					count += 1;
				}
			}
			return count;
		}

		public bool ShouldRotate()
		{
			return (!IsLastOneBack() || InGoalArea())
&& (!DemoRotation().Item1 || IsAheadOfBall()) && (FriendsAheadOfBall() != 0 || IsAheadOfBall());
		}

		public Vec3 Zone5Positioning()
		{
			float ballY = Ball.Location.y;
			float dY = ballY + (1707 * Field.Side(Team));
			return new Vec3(0, 2 * dY / 3, 0);
		}

		public bool IsInFrontOfBall()
		{
			double meToGoal = (Me.Location - TheirGoal.Location).Length();
			double ballToGoal = (Ball.Location - TheirGoal.Location).Length();
			return meToGoal < ballToGoal;
		}

		public bool AreNoBotsBack()
		{
			if (!IsAheadOfBall() || IsClosest(Me))
			{
				return false;
			}
			foreach (Car car in Teammates)
			{
				if (!IsAheadOfBall2(car.Location, Team))
				{
					return false;
				}
			}
			return true;
		}

		public bool IsClosest(Car ownCar, bool teamOnly = false)
		{
			if (Teammates.Count == 0 && teamOnly)
			{
				return true;
			}

			bool aheadCounts = teamOnly && AreNoBotsBack();

			if (IsAheadOfBall() && !aheadCounts)
			{
				return false;
			}
			float factor = 1;
			if (Me.Location.y * Field.Side(Team) < Ball.Location.y * Field.Side(Team))
			{
				factor = 5;
			}
			Vec3 actualDistanceVector = ownCar.Location - Ball.Location;
			float ownDistance;
			if (teamOnly)
			{
				Vec3 biasedDistanceVector = new(2 * actualDistanceVector.x, factor * actualDistanceVector.y, actualDistanceVector.z);
				ownDistance = biasedDistanceVector.Length() - (10 * ownCar.Boost);
			}
			else
			{
				ownDistance = ((ownCar.Location - Ball.Location).Length() * factor) - (10 * ownCar.Boost);
			}

			if (!teamOnly)
			{
				foreach (Car car in Opponents)
				{
					factor = 1;
					if (-car.Location.y * Field.Side(Team) < -Ball.Location.y * Field.Side(Team))
					{
						factor = 5;
					}
					float otherDistance = ((car.Location - Ball.Location).Length() * factor) - (10 * car.Boost);
					if (otherDistance < ownDistance)
					{
						return false;
					}
				}
			}

			foreach (Car car in Teammates.Concat(new List<Car> { Me }))
			{
				if (ownCar.Index == car.Index)
				{
					continue;
				}
				factor = 1;
				if (car.Location.y * Field.Side(Team) < Ball.Location.y * Field.Side(Team))
				{
					factor = 5;
				}

				float otherDistance;
				if (teamOnly)
				{
					Vec3 otherActualDistanceVector = car.Location - Ball.Location;
					Vec3 otherBiasedDistanceVector = new(2 * otherActualDistanceVector.x, factor * otherActualDistanceVector.y, otherActualDistanceVector.z);
					otherDistance = otherBiasedDistanceVector.Length() - (10 * car.Boost);
				}
				else
				{
					otherDistance = ((car.Location - Ball.Location).Length() * factor) - (10 * car.Boost);
				}

				if (IsAheadOfBall2(car.Location, Team) && !aheadCounts)
				{
					continue;
				}
				if (otherDistance < ownDistance)
				{
					return false;
				}
			}

			return true;
		}

		public bool IsSecondClosest()
		{
			if (IsClosest(Me, true))
			{
				return false;
			}
			if (IsAheadOfBall())
			{
				return false;
			}
			if (Teammates.Count < 1)
			{
				return false;
			}
			float factor = 1;
			if (Me.Location.y * Field.Side(Team) < Ball.Location.y * Field.Side(Team))
			{
				factor = 5;
			}

			Vec3 actualDistanceVector = Me.Location - Ball.Location;
			Vec3 biasedDistanceVector = new(
				2 * actualDistanceVector.x,
				factor * actualDistanceVector.y,
				actualDistanceVector.z
			);
			float ownDistance = biasedDistanceVector.Length() - (10 * Me.Boost);
			int closestIndex = -1;
			foreach (Car car in Teammates)
			{
				if (IsClosest(car, true))
				{
					closestIndex = car.Index;
				}
			}

			if (closestIndex == Index)
			{
				return false;
			}
			foreach (Car car in Teammates)
			{
				if (car.Index == closestIndex)
				{
					continue;
				}
				factor = 1;
				if (car.Location.y * Field.Side(Team) < Ball.Location.y * Field.Side(Team))
				{
					factor = 5;
				}

				Vec3 otherActualDistanceVector = car.Location - Ball.Location;
				Vec3 otherBiasedDistanceVector = new(
					2 * otherActualDistanceVector.x,
					factor * otherActualDistanceVector.y,
					otherActualDistanceVector.z
				);
				float otherDistance = otherBiasedDistanceVector.Length() - (10 * car.Boost);
				if (otherDistance < ownDistance)
				{
					return false;
				}
			}
			return true;
		}

		public int FriendsAttacking()
		{
			int count = 0;
			foreach (Car car in Teammates)
			{
				if (car.Location.y * Field.Side(Team) < 0)
				{
					count += 1;
				}
			}
			return count;
		}

		public bool ShouldAttack()
		{
			return Ball.Location.y * Field.Side(Team) <= 0;
		}

		public bool ShouldDefend()
		{
			return Ball.Location.y * Field.Side(Team) > 0;
		}

		public Car GetClosestTeammate()
		{
			Car closestTeammate = Teammates[0];
			float closestDistance = float.PositiveInfinity;

			foreach (Car mate in Teammates)
			{
				float distance = (mate.Location - Me.Location).Length();
				if (distance < closestDistance)
				{
					closestDistance = distance;
					closestTeammate = mate;
				}
			}

			return closestTeammate;
		}

		public Car GetClosestOpponent()
		{
			Car closestOpponent = Opponents[0];
			float closestDistance = float.PositiveInfinity;

			foreach (Car foe in Opponents)
			{
				float distance = (foe.Location - Me.Location).Length();
				if (distance < closestDistance)
				{
					closestDistance = distance;
					closestOpponent = foe;
				}
			}

			return closestOpponent;

		}
		public Boost GetBestBoost()
		{
			Boost bestBoost = null;
			float bestScore = float.PositiveInfinity;

			// Consider both big and small pads when urgent; prefer big pads when time allows
			IEnumerable<Boost> candidates = Field.Boosts.Where(b =>
				((b.IsActive && b.IsLarge) || (b.TimeUntilActive < 3f && b.IsLarge)) // near active
				&& Field.InField(b.Location, 80)
			);

			foreach (Boost boost in candidates)
			{
				// Safety: avoid leaving net open and prefer pads that keep a good lane to own goal coverage
				float etaToBoost = Drive.GetEta(Me, boost.Location);
				float etaToNet = Drive.GetEta(Me, OurGoal.Location);
				float ballToNetTime = Ball.Location.Dist(OurGoal.Location) / MathF.Max(500, Ball.Velocity.FlatLen());
				bool safe = etaToNet < ballToNetTime + 0.4f;
				if (!safe && !boost.IsLarge) continue; // only risk small pads if net is covered

				// Geometry: prefer small angle to approach and lane from pad to ball/goal
				Vec3 botToBoost = (boost.Location - Me.Location).Normalize();
				float approachAngle = Me.Forward.FlatAngle(botToBoost);
				float distToBoost = Me.Location.Dist(boost.Location);
				float distBoostToBall = boost.Location.Dist(Ball.Location);
				// Favor pads ahead of us in field progression
				bool forwardPad = (boost.Location.y - Me.Location.y) * Field.Side(Team) > -400;
				float largeBias = boost.IsLarge ? 0f : 400f; // prefer large pads

				float score = distToBoost + 0.6f * distBoostToBall + 350f * approachAngle + largeBias + (forwardPad ? -250f : 150f);

				if (score < bestScore)
				{
					bestScore = score;
					bestBoost = boost;
				}
			}

			return bestBoost;
		}
	}
}
