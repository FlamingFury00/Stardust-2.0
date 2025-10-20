using RedUtils.Math;
using RLBot.Flat;

namespace RedUtils
{
	/// <summary>A predicted state of the ball in the future</summary>
	public class BallSlice
	{
		/// <summary>The location of the ball at this future point in time</summary>
		public readonly Vec3 Location;
		/// <summary>The velocity of the ball at this future point in time</summary>
		public readonly Vec3 Velocity;
		/// <summary>The angular velocity of the ball at this future point in time</summary>
		public readonly Vec3 AngularVelocity;
		/// <summary>The time in the future that this slice predicts</summary>
		public readonly float Time;

		/// <summary>Initializes a new ball slice</summary>
		public BallSlice(PredictionSliceT slice)
		{
            Location = new Vec3(slice.Physics.Location);
            Velocity = new Vec3(slice.Physics.Velocity);
            AngularVelocity = new Vec3(slice.Physics.AngularVelocity);
            Time = slice.GameSeconds;
        }

		/// <summary>Converts this ball slice to a ball</summary>
		public Ball ToBall()
		{
			return new Ball(Location, Velocity, AngularVelocity);
		}
	}
}
