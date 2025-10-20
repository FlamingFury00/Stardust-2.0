using System;
using System.Collections.Generic;
using System.Text;
using RLBot.Flat;
using RLBot.Manager;

namespace RedUtils
{
	/// <summary>The main class in RedUtils. 
	/// <para>It contains properties that are unique to your bot, such as your bot's car, your teammates, etc.</para>
	/// <para>It also receives the GameTickPacket from the RLBot framework, and processes all the data in it.</para>
	/// </summary>
	public abstract partial class RUBot : Bot
	{
		// --- Team shot claim coordination ---
		private readonly Dictionary<int, (float sliceTime, float receivedAt)> _teamShotClaims = new();
		private bool _shotClaimActive = false;
		private float _lastClaimedSliceTime = -1f;
		private float _lastReleaseTime = -10f;

		/// <summary>A tool to draw debug lines in-game</summary>
		public new ExtendedRenderer Renderer { get; internal set; }

		/// <summary>Your car</summary>
		public Car Me => Index < Cars.Count ? Cars.AllCars[Index] : new Car();

		/// <summary>A list of all of the cars on your team (not including yourself, and including ones that are respawning)</summary>
		public List<Car> Teammates 
		{ 
			get 
			{ 
				List<Car> teammates = Cars.AllCars.FindAll(car => car.Team == Team);
				teammates.Remove(Me);
				return teammates;
			} 
		}
		/// <summary>A list of all of the cars on your team (not including yourself, and NOT including ones that are respawning)</summary>
		public List<Car> LivingTeammates
		{
			get
			{
				List<Car> teammates = Cars.AllCars.FindAll(car => !car.IsDemolished && car.Team == Team);
				teammates.Remove(Me);
				return teammates;
			}
		}

		/// <summary>A list of all of the cars on the opposing team (including ones that are respawning)</summary>
		public List<Car> Opponents => Cars.AllCars.FindAll(car => car.Team != Team);
		/// <summary>A list of all of the cars on the opposing team (NOT including ones that are respawning)</summary>
		public List<Car> LivingOpponents => Cars.AllCars.FindAll(car => !car.IsDemolished && car.Team != Team);

		/// <summary>Our team's goal</summary>
		public Goal OurGoal => Field.Goals[Team];
		/// <summary>The opposing team's goal</summary>
		public Goal TheirGoal => Field.Goals[1 - Team];

		/// <summary>The current score for your team</summary>
		public uint OurScore => Game.Scores[Team];
		/// <summary>The current score for the opposing teams</summary>
		public uint TheirScore => Game.Scores[1 - Team];

		/// <summary>Whether or not it's time for kickoff</summary>
		public bool IsKickoff { get; private set; }
		/// <summary>The change in time since the last tick</summary>
		public float DeltaTime { get; private set; }

		/// <summary>The inputs that are returned to RLBot every tick</summary>
		public ControllerStateT Controller = new ControllerStateT();
		/// <summary>
		/// The action that will be executed every tick, along side the "Run" function. 
		/// <para>Resets when the ball is touched, unless the action isn't interruptible. Otherwise only resets when the action has finished</para>
		/// </summary>
		public IAction Action = null;

		/// <summary>Whether the "GetReady" function has run</summary>
		private bool _ready = false;
		/// <summary>The last time the ball was touched</summary>
		private float _lastTouchTime;
		/// <summary>The previous moment in time. <para>Used to calculate DeltaTime</summary>
		private float _lastTime = 0;



		//a
		protected RUBot(string defaultAgentId = null) : base(defaultAgentId)
        {
			Console.WriteLine($"RedUtils bot \"{GetType().Name}\" is up and running.");
		}

		/// <summary>Initializes some static classes using data from the packet</summary>
		/// <param name="packet">Contains all information about the current game state</param>
		private void GetReady(GamePacketT packet)
		{
			Renderer = new ExtendedRenderer(base.Renderer);
            if (!Game.Initialized)
            {
                Game.Initialize();

                Field.Initialize(FieldInfo);
                Cars.Initialize(packet);
            }
            _ready = true;
		}

		/// <summary>Processes data from the packet, and uses said data to update some static classes</summary>
		/// <param name="packet">Contains all information about the current game state</param>
		private void Process(GamePacketT packet)
		{
            if (Game.Time < packet.MatchInfo.SecondsElapsed)
            {
                // only update static stuff if it hasn't already been updated this tick

                if (Cars.Count != packet.Players.Count)
                {
                    // Reinitializes the cars if someone has left or joined the game
                    Cars.Initialize(packet);
                }
                else
                {
                    // Updates the cars' positions, velocities, etc
                    Cars.Update(packet);
                }
				if (packet.Balls.Count > 0)
				{
                    // Updates the ball's position, velocity, etc
                    Ball.Update(this, packet.Balls[0]);
                }
                // Updates the game's score, time, etc
                Game.Update(packet);
                // Updates the boost pads
                Field.Update(packet);
            }

            if (!IsKickoff && Game.MatchPhase == MatchPhase.Kickoff)
            {
                // Reset the action right as a kickoff starts
                Action = null;
            }

            IsKickoff = Game.MatchPhase == MatchPhase.Kickoff;
        }

		/// <summary>Updates DeltaTime... pretty self explanitory</summary>
		private void UpdateDeltaTime()
		{
			DeltaTime = Game.Time - _lastTime;
			_lastTime = Game.Time;
		}

		/// <summary>The function that gets called every tick to get inputs from your bot</summary>
		/// <param name="packet">Contains all information about the current game state</param>
		/// <returns>The bot's inputs for that tick</returns>
		public override ControllerStateT GetOutput(GamePacketT packet)
		{
			// Resets the Controller every tick
			Controller = new ControllerStateT(); 

			if (!_ready)
			{
				// Gets the packet ready for the first time during startup
				GetReady(packet); 
			}
			// Proccesses the packet so that data is up to date during this frame
			Process(packet);

			// Begin a render group for this tick so ExtendedRenderer draw calls are flushed
			base.Renderer.Begin($"BOT_{Index}");
			// Runs our strategy code
			Run(); 

			// if there is an action to execute...
			if (Action != null)
			{
				Action.Run(this); // execute it!

				// If the ball hasn't been touched, set it to -1, so we don't get errors.
                float latestTouchTime = Ball.LatestTouch == null ? -1 : Ball.LatestTouch.Time; 
				if (Action.Finished || (_lastTouchTime != latestTouchTime && Action.Interruptible) || Me.IsDemolished) 
				{
					// If the action has completed, or the ball has been touched and the action is interruptible,
					// or if our bot is demolished reset the action
					_lastTouchTime = latestTouchTime;
					Action = null;
				}
			}

			// Manage shot claim lifecycle based on current action or lack thereof
			UpdateShotClaimState();

			UpdateDeltaTime();

			// End the render group and flush drawings to RLBot
			base.Renderer.End();

			// returns our inputs to RLBot
			return Controller; 
		}

		/// <summary>
		/// Receive team communications to coordinate shot claims.
		/// </summary>
		public override void HandleMatchComm(int Index, int Team, List<byte> Content, string? Display, bool teamOnly)
		{
			// Only care about our team's messages
			if (Team != this.Team) return;
			if (Content == null || Content.Count == 0) return;
			string msg;
			try
			{
				msg = Encoding.ASCII.GetString(Content.ToArray());
			}
			catch
			{
				return;
			}

			// Format: CLAIM_SHOT;{sliceTime}
			//         RELEASE_SHOT
			if (msg.StartsWith("CLAIM_SHOT;"))
			{
				string[] parts = msg.Split(';');
				if (parts.Length >= 2 && float.TryParse(parts[1], out float sliceTime))
				{
					_teamShotClaims[Index] = (sliceTime, Game.Time);
				}
			}
			else if (msg.StartsWith("RELEASE_SHOT"))
			{
				_teamShotClaims.Remove(Index);
			}
		}

		/// <summary>
		/// Returns true if a teammate has claimed a shot with an earlier or equal intercept time.
		/// </summary>
		protected bool HasTeammateEarlierShot(float mySliceTime, float grace = 0.02f)
		{
			float now = Game.Time;
			foreach (var kv in _teamShotClaims)
			{
				int idx = kv.Key;
				if (idx == Index) continue; // ignore own claim
				(float slice, float receivedAt) = kv.Value;
				bool fresh = (now - receivedAt) < 2.0f && now < slice + 0.6f;
				if (!fresh) continue;
				if (slice <= mySliceTime + grace) return true;
			}
			return false;
		}

		private void UpdateShotClaimState()
		{
			// Called once per tick after Action.Run and possible Action reset decisions
			if (Action is Shot s && s.Slice != null)
			{
				float sliceTime = s.Slice.Time;
				// Debounce claiming to avoid two bots re-claiming in sync
				bool canClaim = (Game.Time - _lastReleaseTime) > 0.2f;
				if (canClaim && (!_shotClaimActive || MathF.Abs(sliceTime - _lastClaimedSliceTime) > 0.01f))
				{
					// Send or update claim to teammates
					string text = $"CLAIM_SHOT;{sliceTime:F3}";
					base.SendMatchComm(Index, Team, new List<byte>(Encoding.ASCII.GetBytes(text)), null, teamOnly: true);
					_shotClaimActive = true;
					_lastClaimedSliceTime = sliceTime;
					// Also store our own claim locally so HasTeammateEarlierShot has consistent data across bots
					_teamShotClaims[Index] = (sliceTime, Game.Time);
				}
			}
			else
			{
				if (_shotClaimActive)
				{
					// Release our claim when leaving shot state
					base.SendMatchComm(Index, Team, new List<byte>(Encoding.ASCII.GetBytes("RELEASE_SHOT")), null, teamOnly: true);
					_shotClaimActive = false;
					_lastClaimedSliceTime = -1f;
					_teamShotClaims.Remove(Index);
					_lastReleaseTime = Game.Time;
				}
			}
		}

		/// <summary>Runs every tick. Overwrite with your own strategy code!</summary>
		public abstract void Run();

		/// <summary>Gets the ball prediction struct from the framework</summary>
		internal BallPrediction GetBallPrediction() => new BallPrediction(base.BallPrediction);
	}
}
