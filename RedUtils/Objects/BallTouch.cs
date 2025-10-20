using RedUtils.Math;
using RLBot.Flat;

namespace RedUtils
{
	/// <summary>Contains info on a collision between the ball and a car</summary>
	public class BallTouch
	{
		/// <summary>The time at which point this collision happened</summary>
		public readonly float Time;
		/// <summary>The location of this collision</summary>
		public readonly Vec3 Location;
		/// <summary>The normal of this collision</summary>
		public readonly Vec3 Normal;
		/// <summary>The name of the player who collided with the ball</summary>
		public readonly string PlayerName;
		/// <summary>The index of the player who collided with the ball</summary>
		public readonly int PlayerIndex;
		/// <summary>The team of the player who collided with the ball</summary>
		public readonly uint Team;
		/// <summary>The index of the ball that was touched</summary>
		public readonly uint BallIndex;

		/// <summary>Initializes a new ball touch with data from the packet</summary>
		public BallTouch(PlayerInfoT player)
		{
            Time = player.LatestTouch.GameSeconds;
            Location = new Vec3(player.LatestTouch.Location);
            Normal = new Vec3(player.LatestTouch.Normal);
            PlayerName = player.Name;
            PlayerIndex = player.PlayerId;
			BallIndex = player.LatestTouch.BallIndex;
            Team = player.Team;
        }
	}
}
