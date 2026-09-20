using System;
using RedUtils.Math;

namespace RedUtils
{
	/// <summary>Represents a target to be shot at or away from</summary>
	public class Target
	{
		/// <summary>The minimum room for error for any shot. 
		/// <para>For example, an error of 0 would mean shots with a very extreme angle, with zero room for error, would be allowed</para>
		/// </summary>
		private const float MinError = 50f;
		/// <summary>
		/// Extra center-of-ball clearance from attacking posts/crossbar beyond the ball radius.
		/// This leaves the floor unchanged so rolling finishes remain available.
		/// </summary>
		public const float GoalSafetyMargin = 70f;

		/// <summary>The top left point of the target
		/// <para>Note that the ball has to fit between the points, meaning the points have to be far enough apart for a ball to fit between them</para>
		/// </summary>
		public Vec3 TopLeft;
		/// <summary>The top right point of the target
		/// <para>Note that the ball has to fit between the points, meaning the points have to be far enough apart for a ball to fit between them</para>
		/// </summary>
		public Vec3 BottomRight;
		/// <summary>The surface containing all the points the ball could go through between the top left and bottom right points
		/// <para>Note that this isn't an actual collidable surface</para>
		/// </summary>
		public Surface TargetSurface;

		/// <summary>Initiliazes a new target using the top left and bottom right points
		/// <para>If the ball is behind the target, then we will attempt to shoot it away from the target.</para>
		/// <para>Note that the ball has to fit between the points, meaning the horizontal and vertical distance between the points has to be > 186.3f</para>
		/// </summary>
		public Target(Vec3 topLeft, Vec3 bottomRight)
		{
			TopLeft = topLeft;
			BottomRight = bottomRight;
			TargetSurface = new Surface("target", (topLeft + BottomRight) / 2, (TopLeft - BottomRight).Cross(Vec3.Up).Normalize(), 
							new Vec3(MathF.Max(TopLeft.FlatDist(BottomRight) - Ball.Radius * 2, 1), MathF.Max(MathF.Abs(TopLeft.z - BottomRight.z) - Ball.Radius * 2, 1)), 
							(TopLeft - BottomRight).FlatNorm(), Vec3.Up);
		}

		/// <summary>Initiliazes a new target using a goal object</summary>
		public Target(Goal goal, bool shootAwayFromGoal = false)
		{
			if (shootAwayFromGoal)
			{
				// Swap top left and bottom right
				TopLeft = goal.BottomRightCorner;
				BottomRight = goal.TopLeftCorner;
			}
			else
			{
				float side = Field.Side(goal.Team);
				// Do not plan nominal attacking contacts on the mathematical edge of the
				// scoring aperture. Contact/orientation error near an edge becomes a post
				// or crossbar hit; preserve the floor so ground shots can still roll in.
				TopLeft = goal.TopLeftCorner - Vec3.X * side * GoalSafetyMargin - Vec3.Up * GoalSafetyMargin;
				BottomRight = goal.BottomRightCorner + Vec3.X * side * GoalSafetyMargin;
			}
			TargetSurface = new Surface("target", (TopLeft + BottomRight) / 2 + Vec3.Y * Field.Side(goal.Team) * Ball.Radius, -Vec3.Y * Field.Side(goal.Team),
							new Vec3(MathF.Max(TopLeft.FlatDist(BottomRight) - Ball.Radius * 2, 1), MathF.Max(MathF.Abs(TopLeft.z - BottomRight.z) - Ball.Radius * 2, 1)),
							(TopLeft - BottomRight).FlatNorm(), Vec3.Up);
		}

		/// <summary>Whether or not it's possible to shoot between the two points, given the current location of the ball</summary>
		public bool Fits(Vec3 ballLocation)
		{
			Vec3 goalLine = (BottomRight - TopLeft).FlatNorm();
			GetCorrectedLimits(ballLocation, out Vec3 correctedLeft, out Vec3 correctedRight, out Vec3 correctedTop, out Vec3 correctedBottom);

			float width = correctedLeft.FlatDist(correctedRight);
			float height = MathF.Abs(correctedLeft.z - correctedRight.z);
			Vec3 horizontalPerp = (correctedLeft - correctedRight).Cross(Vec3.Up).Normalize();
			Vec3 verticalPerp = (correctedTop - correctedBottom).Cross(goalLine).Normalize();
			Vec3 ballToHorizontalCenter = (correctedLeft + correctedRight) / 2 - ballLocation;
			Vec3 ballToVerticalCenter = (correctedTop + correctedBottom) / 2 - ballLocation;

			return ((ballToHorizontalCenter.Normalize().Dot(-horizontalPerp) * width > MinError || ballToHorizontalCenter.Length() < width / 2) &&
					(ballToVerticalCenter.Normalize().Dot(-verticalPerp) * height > MinError || ballToVerticalCenter.Length() < height / 2)) ||
					TopLeft.FlatDirection(BottomRight).Cross(Vec3.Up).Dot(ballLocation.FlatDirection((TopLeft + BottomRight) / 2)) < 0;
		}

		/// <summary>Returns the easiest point to shoot at on the target
		/// <para>Note that if the ball is behind the target, it will return you the easiest point to shoot at that is NOT on target</para>
		/// </summary>
		public Vec3 Clamp(Ball ball)
		{
			Vec3 goalLine = (BottomRight - TopLeft).FlatNorm();
			Vec3 planeNormal = goalLine.Cross(-Vec3.Up).Normalize();
			GetCorrectedLimits(ball.location, out Vec3 correctedLeft, out Vec3 correctedRight, out Vec3 correctedTop, out Vec3 correctedBottom);

			if (planeNormal.Dot(ball.location - (TopLeft + BottomRight) / 2) > 0)
			{
				float signedDistance = (ball.location - TopLeft).Dot(planeNormal);
				float normalVelocity = ball.velocity.Dot(planeNormal);
				float interceptTime = MathF.Abs(normalVelocity) > 0.001f ? -signedDistance / normalVelocity : float.NaN;

				// Only extrapolate when the ball is genuinely moving toward the target plane.
				// Otherwise use the orthogonal projection; backward-time extrapolation can drag
				// a stationary/retreating ball to a post or poison the geometry with Infinity.
				Vec3 target = float.IsFinite(interceptTime) && interceptTime > 0 && interceptTime <= 6
					? TargetSurface.Limit(ball.PredictLocation(interceptTime))
					: TargetSurface.Limit(ball.location - planeNormal * signedDistance);
				if (!Finite(target)) target = TargetSurface.Location;

				Vec3 directionClampedHorizontally = (target - ball.location).Normalize().Clamp(
					(correctedLeft - ball.location).Normalize(), (correctedRight - ball.location).Normalize());
				Vec3 directionClamped = directionClampedHorizontally.Clamp(
					(correctedTop - ball.location).Normalize(), (correctedBottom - ball.location).Normalize(), goalLine);
				float planeRate = directionClamped.Dot(planeNormal);
				if (!float.IsFinite(planeRate) || MathF.Abs(planeRate) < 0.0001f) return target;

				float travel = -signedDistance / planeRate;
				if (!float.IsFinite(travel) || travel <= 0) return target;
				Vec3 result = TargetSurface.Limit(ball.location + directionClamped * travel);
				return Finite(result) ? result : target;
			}
			else
			{
				Vec3 seed = ball.velocity.Length() > 1 ? ball.velocity : ball.location - TargetSurface.Location;
				if (seed.Length() < 0.001f) seed = planeNormal;
				Vec3 directionClampedHorizontally = seed.Normalize().Clamp(
					(correctedLeft - ball.location).Normalize(), (correctedRight - ball.location).Normalize());
				Vec3 directionClamped = directionClampedHorizontally.Clamp(
					(correctedTop - ball.location).Normalize(), (correctedBottom - ball.location).Normalize(), goalLine);

				return ball.location + directionClamped.Normalize() * 1000;
			}
		}

		/// <summary>Gives the corrected edge positions through out variables based on the current ball location</summary>
		public void GetCorrectedLimits(Vec3 ballLocation, out Vec3 correctedLeft, out Vec3 correctedRight, out Vec3 correctedTop, out Vec3 correctedBottom)
		{
			Vec3 goalLine = (BottomRight - TopLeft).FlatNorm();

			Vec3 leftAdjusted = TopLeft + (TopLeft - ballLocation).Normalize().Rotate(-SafeAsinRatio(Ball.Radius, TopLeft.FlatDist(ballLocation))).Cross(-Vec3.Up).Normalize() * Ball.Radius;
			Vec3 rightAdjusted = BottomRight + (BottomRight - ballLocation).Normalize().Rotate(SafeAsinRatio(Ball.Radius, BottomRight.FlatDist(ballLocation))).Cross(Vec3.Up).Normalize() * Ball.Radius;
			Vec3 top = (TopLeft + BottomRight).Flatten() / 2 + Vec3.Up * TopLeft.z;
			Vec3 topAdjusted = top + (TopLeft - ballLocation).Normalize().Rotate(-SafeAsinRatio(Ball.Radius, TopLeft.FlatDist(ballLocation, goalLine)), goalLine).Cross(-goalLine).Normalize() * Ball.Radius;
			Vec3 bottom = (TopLeft + BottomRight).Flatten() / 2 + Vec3.Up * BottomRight.z;
			Vec3 bottomAdjusted = bottom + (BottomRight - ballLocation).Normalize().Rotate(SafeAsinRatio(Ball.Radius, BottomRight.FlatDist(ballLocation, goalLine)), goalLine).Cross(goalLine).Normalize() * Ball.Radius;

			correctedLeft = (TopLeft - ballLocation).Flatten().Dot((TopLeft - BottomRight).Flatten()) > 0f ? TopLeft : leftAdjusted;
			correctedRight = (BottomRight - ballLocation).Flatten().Dot((BottomRight - TopLeft).Flatten()) > 0f ? BottomRight : rightAdjusted;
			correctedTop = (TopLeft - ballLocation).Flatten(goalLine).Dot(Vec3.Up) > 0f ? top : topAdjusted;
			correctedBottom = (BottomRight - ballLocation).Flatten(goalLine).Dot(-Vec3.Up) > 0f ? bottom : bottomAdjusted;
		}

		private static bool Finite(Vec3 value) =>
			float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

		private static float SafeAsinRatio(float numerator, float denominator)
		{
			if (!float.IsFinite(denominator) || denominator <= 0.0001f) return MathF.PI / 2;
			return MathF.Asin(Utils.Cap(numerator / denominator, -1f, 1f));
		}
	}
}
