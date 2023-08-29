using System;
using System.Drawing;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using RedUtils.Math;
using RLBotDotNet;

/* 
 * This file extends the RUBot class with some extra tools that make bot creation easier.
 * You probably won't want to edit to much in here, except the ShotCheck functions.
 * You are encouraged to make your own, although feel free to continue using the default one if you like.
 */

namespace RedUtils
{
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
		public float[] AimAt(Vec3 targetLocation, Vec3 up = new(), bool backwards = false)
		{
			Vec3 localTarget = Me.Local(targetLocation - Me.Location) * (backwards ? -1 : 1); // Where our target is in local coordinates
			Vec3 safeUp = up.Length() != 0 ? up : Vec3.Up; // Make sure "up" is not the zero vector (which is the default argument)
			Vec3 localUp = Me.Local(safeUp.Normalize()); // Where "up" is in local coordinates
			float[] targetAngles = new float[3] {
				MathF.Atan2(localTarget.z, localTarget.x), // Angle to pitch towards target
				MathF.Atan2(localTarget.y, localTarget.x), // Angle to yaw towards target
				MathF.Atan2(localUp.y, localUp.z) // Angle to roll upright
			};
			// Now that we have the angles we need to rotate, we feed them into the PD loops to determine the controller inputs
			Controller.Steer = SteerPD(targetAngles[1], -Me.LocalAngularVelocity[2] * 0.01f) * (backwards ? -1 : 1);
			Controller.Pitch = SteerPD(targetAngles[0], Me.LocalAngularVelocity[1] * 0.2f);
			Controller.Yaw = SteerPD(targetAngles[1], -Me.LocalAngularVelocity[2] * 0.15f);
			Controller.Roll = SteerPD(targetAngles[2], Me.LocalAngularVelocity[0] * 0.25f);

			return targetAngles; // Returns the angles, which could be useful for other purposes
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

				// Check first if the slice is in the future and if it's even possible to shoot at our target
				if (timeRemaining > 0 && target.Fits(slice.Location))
				{
					Ball ballAfterHit = slice.ToBall();
					Vec3 carFinVel = ((slice.Location - Me.Location) / timeRemaining).Cap(0, Car.MaxSpeed);
					ballAfterHit.velocity = carFinVel + slice.Velocity.Flatten(carFinVel.Normalize()) * 0.77f;
					Vec3 shotTarget = target.Clamp(ballAfterHit);

					// First, check if we can aerial
					AerialShot aerialShot = new AerialShot(Me, slice, shotTarget);
					if (aerialShot.IsValid(Me))
					{
						return aerialShot; // If so, go for it!
					}

					// If we can't aerial, let's try a ground shot
					GroundShot groundShot = new GroundShot(Me, slice, shotTarget);
					if (groundShot.IsValid(Me))
					{
						return groundShot;
					}

					// Otherwise, we'll try a jump shot
					JumpShot jumpShot = new JumpShot(Me, slice, shotTarget);
					if (jumpShot.IsValid(Me))
					{
						return jumpShot;
					}

					// And lastly, a double jump shot
					DoubleJumpShot doubleJumpShot = new DoubleJumpShot(Me, slice, shotTarget);
					if (doubleJumpShot.IsValid(Me))
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

		public bool IsAheadOfBall2(Vec3 location, int team)
		{
			return (location.y > Ball.Location.y + 500 && team == 0) ||
				   (location.y < Ball.Location.y - 500 && team == 1);
		}

		public Tuple<bool, int> DemoRotation()
		{
			List<Car> possibleCars = new List<Car>();
			foreach (Car car in Opponents)
			{
				if (car.Location.y * Field.Side(Team) < -4000)
				{
					double distanceToTarget = (Me.Location - car.Location).Length();
					float velocity = Me.Velocity.Length();
					float velocityNeeded = 2200 - velocity;
					float timeBoostingRequired = velocityNeeded / 991.666f;
					float boostRequired = 33.3f * timeBoostingRequired;
					float distanceRequired = velocity * timeBoostingRequired + 0.5f * 991.666f * (timeBoostingRequired * timeBoostingRequired);

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
			if (IsLastOneBack() && !InGoalArea())
			{
				return false;
			}

			if (DemoRotation().Item1 && !IsAheadOfBall())
			{
				return false;
			}

			if (FriendsAheadOfBall() == 0 && !IsAheadOfBall())
			{
				return false;
			}

			return true;
		}

		public Vec3 Zone5Positioning()
		{
			float ballY = Ball.Location.y;
			float dY = ballY + 1707 * Field.Side(Team);
			return new Vec3(0, 2 * dY / 3, 0);
		}

		public bool IsInFrontOfBall()
		{
			double meToGoal = (Me.Location - TheirGoal.Location).Length();
			double ballToGoal = (Ball.Location - TheirGoal.Location).Length();
			if (meToGoal > 1000 && meToGoal < ballToGoal)
			{
				return true;
			}
			return false;
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

			bool aheadCounts = teamOnly && AreNoBotsBack() ? true : false;

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
				Vec3 biasedDistanceVector = new Vec3(2 * actualDistanceVector.x, factor * actualDistanceVector.y, actualDistanceVector.z);
				ownDistance = biasedDistanceVector.Length() - (10 * ownCar.Boost);
			}
			else
			{
				ownDistance = (ownCar.Location - Ball.Location).Length() * factor - (10 * ownCar.Boost);
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
					float otherDistance = (car.Location - Ball.Location).Length() * factor - (10 * car.Boost);
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
					Vec3 otherBiasedDistanceVector = new Vec3(2 * otherActualDistanceVector.x, factor * otherActualDistanceVector.y, otherActualDistanceVector.z);
					otherDistance = otherBiasedDistanceVector.Length() - (10 * car.Boost);
				}
				else
				{
					otherDistance = (car.Location - Ball.Location).Length() * factor - (10 * car.Boost);
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
			Vec3 biasedDistanceVector = new Vec3(
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
				Vec3 otherBiasedDistanceVector = new Vec3(
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
			if (Ball.Location.y * Field.Side(Team) <= 0)
			{
				return true;
			}

			return false;
		}

		public bool ShouldDefend()
		{
			if (Ball.Location.y * Field.Side(Team) > 0)
			{
				return true;
			}

			return false;
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
	}
}