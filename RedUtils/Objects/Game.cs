using RedUtils.Math;
using RLBot.Flat;

namespace RedUtils
{
	/// <summary>Contains basic info on the current match</summary>
	public static class Game
	{
		/// <summary>The scores for the blue team, and orange team (in that order)</summary>
		public static uint[] Scores { get; private set; }
		/// <summary>The blue team's score</summary>
		public static uint BlueScore => Scores[1];
		/// <summary>The orange team's score</summary>
		public static uint OrangeScore => Scores[0];

		/// <summary>How much time has passed since the game has began</summary>
		public static float Time { get; private set; }
		/// <summary>How much time is remaining before the game ends</summary>
		public static float TimeRemaining { get; private set; }
		/// <summary>The speed at which game plays</summary>
		public static float GameSpeed { get; private set; }

		/// <summary>Whether or not the game is configured with unlimited time</summary>
		public static bool IsUnlimitedTime { get; private set; }
		/// <summary>Whether or not the game is currently in overtime</summary>
		public static bool IsOvertime { get; private set; }
        /// <summary>The current state of the match, eg. in a replay, kickoff, game time, etc.</summary>
        public static MatchPhase MatchPhase { get; private set; }

        /// <summary>The acceleration caused by gravity</summary>
        public static Vec3 Gravity { get; private set; }

		/// <summary>Whether we have loaded the game or not</summary>
        public static bool Initialized { get; private set; }

        static Game()
		{
			Scores = new uint[2] { 0, 0 };;

			Time = 0;
			TimeRemaining = 300;
			GameSpeed = 1;

			IsUnlimitedTime = false;
			IsOvertime = false;
            MatchPhase = MatchPhase.Inactive;

            Gravity = new Vec3(0, 0, -650);
		}

        public static void Initialize()
        {
            Initialized = true;
        }

        /// <summary>Updates info about the game using data from the packet</summary>
        public static void Update(GamePacketT packet)
        {
            Scores = new uint[2] { packet.Teams[0].Score, packet.Teams[1].Score };

            Time = packet.MatchInfo.SecondsElapsed;
            TimeRemaining = packet.MatchInfo.GameTimeRemaining;
            GameSpeed = packet.MatchInfo.GameSpeed;

            IsUnlimitedTime = packet.MatchInfo.IsUnlimitedTime;
            IsOvertime = packet.MatchInfo.IsOvertime;
            MatchPhase = packet.MatchInfo.MatchPhase;

            Gravity = new Vec3(0, 0, packet.MatchInfo.WorldGravityZ);
        }
    }
}
