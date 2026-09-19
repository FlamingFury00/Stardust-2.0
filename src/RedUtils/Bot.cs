using System;
using System.Collections.Generic;
using System.Text;
using RLBot.Flat;
using RLBot.Manager;

namespace RedUtils
{
    /// <summary>Packet adapter and action lifecycle. Shared world state is serialized across bot instances.</summary>
    public abstract partial class RUBot : Bot
    {
        private static readonly object WorldGate = new();
        private readonly TickClock clock = new();
        private readonly ShotClaimLedger claims = new();
        private bool ready, hasOutput, shotClaimActive;
        private float lastTouchTime = -1f, lastClaimSent = float.NegativeInfinity;

        public new ExtendedRenderer Renderer { get; internal set; }
        public Car Me => Index >= 0 && Index < Cars.Count ? Cars.AllCars[Index] : new Car();
        public List<Car> Teammates => Cars.AllCars.FindAll(c => c.Team == Team && c.Index != Index);
        public List<Car> LivingTeammates => Cars.AllCars.FindAll(c => c.Team == Team && c.Index != Index && !c.IsDemolished);
        public List<Car> Opponents => Cars.AllCars.FindAll(c => c.Team != Team);
        public List<Car> LivingOpponents => Cars.AllCars.FindAll(c => c.Team != Team && !c.IsDemolished);
        public Goal OurGoal => Field.Goals[Team];
        public Goal TheirGoal => Field.Goals[1 - Team];
        public uint OurScore => Game.Scores[Team];
        public uint TheirScore => Game.Scores[1 - Team];
        public bool IsKickoff { get; private set; }
        public float DeltaTime { get; private set; }
        public bool ClockReset { get; private set; }
        public JumpState Jump { get; private set; }
        public bool OwnTouchThisTick { get; private set; }
        public ControllerStateT Controller = new();
        public IAction Action;

        protected RUBot(string defaultAgentId = null) : base(defaultAgentId)
        {
            Console.WriteLine($"RedUtils bot \"{GetType().Name}\" is up and running.");
        }

        private void Process(GamePacketT packet)
        {
            if (!ready)
            {
                Renderer = new ExtendedRenderer(base.Renderer);
                Game.Initialize();
                Field.Initialize(FieldInfo);
                Cars.Initialize(packet);
                ready = true;
            }
            else if (ClockReset || Cars.Count != packet.Players.Count) Cars.Initialize(packet);
            else Cars.Update(packet);

            // AirState indicates currently active forces, not whether a jump was already consumed.
            for (int i = 0; i < packet.Players.Count; i++) JumpState.Apply(Cars.AllCars[i], packet.Players[i]);
            Jump = new JumpState(packet.Players[Index]);
            Game.Update(packet);
            Field.Update(packet);
            if (packet.Balls.Count > 0) Ball.Update(this, packet.Balls[0]);
            bool kickoff = Game.MatchPhase == MatchPhase.Kickoff;
            if ((kickoff && !IsKickoff) || ClockReset)
            {
                Action = null;
                claims.Clear();
                lastTouchTime = -1f;
                lastClaimSent = float.NegativeInfinity;
            }
            IsKickoff = kickoff;
        }

        public override ControllerStateT GetOutput(GamePacketT packet)
        {
            lock (WorldGate)
            {
                if (packet?.MatchInfo == null || packet.Players == null || packet.Balls == null ||
                    Index < 0 || Index >= packet.Players.Count || !float.IsFinite(packet.MatchInfo.SecondsElapsed))
                {
                    Action = null;
                    return Controller = new ControllerStateT();
                }
                DeltaTime = clock.Step(packet.MatchInfo.SecondsElapsed);
                ClockReset = clock.Discontinuity;
                Process(packet);
                bool playing = packet.Balls.Count > 0 && !Me.IsDemolished &&
                    (Game.MatchPhase == MatchPhase.Active || IsKickoff);
                if (!playing)
                {
                    Action = null;
                    OwnTouchThisTick = false;
                    UpdateShotClaimState();
                    return Controller = new ControllerStateT();
                }
                if (DeltaTime == 0 && hasOutput) return Controller;
                hasOutput = true;
                Controller = new ControllerStateT();
                float touch = Ball.LatestTouch?.Time ?? -1f;
                bool changedTouch = touch != lastTouchTime;
                // BallTouch.PlayerIndex is a legacy name: the adapter stores PlayerId, NOT array index.
                OwnTouchThisTick = changedTouch && Ball.LatestTouch != null &&
                    Ball.LatestTouch.PlayerIndex == packet.Players[Index].PlayerId && Ball.LatestTouch.BallIndex == 0;
                lastTouchTime = touch;
                if (ControlRuntime.CancelBeforeRun(Action, changedTouch, OwnTouchThisTick, Me.IsDemolished, ClockReset)) Action = null;

                base.Renderer.Begin($"BOT_{Index}");
                try
                {
                    Run();
                    IAction executing = Action;
                    if (executing != null)
                    {
                        executing.Run(this);
                        // A subaction may replace Action. Never clear or dereference its replacement here.
                        if (ReferenceEquals(executing, Action) && executing.Finished) Action = null;
                    }
                    UpdateShotClaimState();
                    Controller = ControlRuntime.Sanitize(Controller, Me.IsDemolished, Me.Boost);
                    return Controller;
                }
                finally { base.Renderer.End(); }
            }
        }

        public override void HandleMatchComm(int Index, int Team, List<byte> Content, string? Display, bool teamOnly)
        {
            if (Team != this.Team || Index == this.Index || Index < 0 || Content == null || Content.Count > 64) return;
            claims.Receive(Index, Encoding.ASCII.GetString(Content.ToArray()), Game.Time);
        }
        protected bool HasTeammateEarlierShot(float mySliceTime, float grace = 0.04f) => claims.EarlierThan(Index, mySliceTime, Game.Time, grace);
        private void UpdateShotClaimState()
        {
            float slice = Action is Shot shot && shot.Slice != null ? shot.Slice.Time :
                Action is IPossessionAction possession ? possession.ClaimTime : float.NaN;
            if (float.IsFinite(slice))
            {
                // Refresh unchanged long-lived claims, with a bounded heartbeat rate.
                if (!shotClaimActive || Game.Time - lastClaimSent >= 0.35f)
                {
                    base.SendMatchComm(Index, Team,
                        new List<byte>(Encoding.ASCII.GetBytes(ShotClaimLedger.Encode(slice))), null, teamOnly: true);
                    shotClaimActive = true;
                    lastClaimSent = Game.Time;
                }
            }
            else if (shotClaimActive)
            {
                base.SendMatchComm(Index, Team, new List<byte>(Encoding.ASCII.GetBytes("RELEASE_SHOT")), null, teamOnly: true);
                shotClaimActive = false;
            }
        }
        public abstract void Run();
        internal BallPrediction GetBallPrediction() => new(base.BallPrediction);
    }
}
