using System;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public sealed class StardustOptions
    {
        public bool GroundControl { get; init; } = Environment.GetEnvironmentVariable("STARDUST_GROUND_CONTROL") == "1";
        public bool AerialCarry { get; init; } = Environment.GetEnvironmentVariable("STARDUST_AERIAL_CARRY") == "1";
        // Experimental possession must not replace baseline shots without explicit opt-in.
        public bool FlipResets { get; init; } = Environment.GetEnvironmentVariable("STARDUST_FLIP_RESETS") == "1";
        public bool Trace { get; init; } = Environment.GetEnvironmentVariable("STARDUST_TRACE") == "1";
    }

    public class Stardust : RUBot
    {
        public StardustOptions Options { get; } = new();
        public TacticalFrame Situation { get; private set; } = new();
        public MechanicsTelemetry Mechanics { get; private set; } = new();
        public string Decision { get; private set; } = "startup";
        public bool Shooting { get; set; }
        public float NextResetAttempt { get; private set; } = float.NegativeInfinity;
        private float nextPlan = float.NegativeInfinity, nextShotSearch = float.NegativeInfinity;
        private bool wasDanger, wasEmergency, wasKickoff;
        private int boostPad = -1;
        private Shot defensiveShot;
        public Stardust(string defaultAgentId = null) : base(defaultAgentId)
        {
            Console.WriteLine($"Stardust tempo recovery: groundCarry={Options.GroundControl}; aerialCarry={Options.AerialCarry}; resets={(Options.FlipResets ? "experimental" : "off")}; STARDUST_TRACE=1 logs mechanic outcomes.");
        }

        public bool CanAttemptReset() => ResetPolicy.Allowed(Me, Ball.MainBall, Situation, Team,
            Options.FlipResets, Game.Time, NextResetAttempt, LivingTeammates.Count == 0);
        public void BeginReset()
        {
            NextResetAttempt = Game.Time + 4;
            Mechanics.Record("attempt", Options.Trace, Index);
        }

        public override void Run()
        {
            if (ClockReset)
            {
                nextPlan = nextShotSearch = NextResetAttempt = float.NegativeInfinity;
                wasDanger = wasEmergency = wasKickoff = false; defensiveShot = null; boostPad = -1;
                Mechanics = new MechanicsTelemetry();
            }
            Shooting = Action is Shot;
            if (IsKickoff)
            {
                wasKickoff = true;
                if (Action is Kickoff) return;
                int rank = 0;
                foreach (Car car in LivingTeammates)
                    if (Tactics.KickoffBefore(car, Me, Ball.Location, Team)) rank++;
                if (rank == 0) { Action = new Kickoff(); SetDecision("kickoff / taker"); }
                else
                {
                    Position(new Vec3(rank == 1 ? 0 : -MathF.Sign(Me.Location.x) * 1000,
                        Field.Side(Team) * (rank == 1 ? 1800 : 3500), 17), false);
                    SetDecision(rank == 1 ? "kickoff / spaced cheat" : "kickoff / cover");
                }
                return;
            }
            if (wasKickoff)
            {
                wasKickoff = false;
                if (Action is Positioning) Action = null;
                nextPlan = nextShotSearch = float.NegativeInfinity;
            }

            float threatTime = Tactics.GoalThreat(Ball.Prediction.Slices, OurGoal.Location, Game.Time, 3);
            // Use the ballistic fallback only when a framework trajectory is unavailable.
            if (Ball.Prediction.Length < 2) threatTime = Tactics.FastGoalThreat(Ball.MainBall, OurGoal.Location);
            float liveOpponentEta = 6;
            foreach (Car car in LivingOpponents)
                liveOpponentEta = MathF.Min(liveOpponentEta, TeamPlanning.EstimateEta(car, Ball.Location));
            bool emergency = float.IsFinite(threatTime);
            bool pressure = Tactics.UnderPressure(Ball.MainBall, OurGoal.Location, liveOpponentEta);
            bool danger = emergency || pressure;
            bool risingDanger = (danger && !wasDanger) || (emergency && !wasEmergency);
            wasEmergency = emergency;
            wasDanger = danger;
            bool due = Game.Time >= nextPlan || Action == null || risingDanger;
            if (due)
            {
                Situation = Tactics.Evaluate(this);
                nextPlan = Game.Time + (danger ? 0.05f : 0.08f);
            }
            // Keep pressure current even during a long possession action.
            Situation.OpponentEta = MathF.Min(Situation.OpponentEta, liveOpponentEta);
            if (risingDanger) { nextShotSearch = float.NegativeInfinity; boostPad = -1; }
            if (Action != null && !Action.Interruptible) return; // Cannot undo a dodge already in progress.
            if (Action is Shot stale && !stale.IsPredictionValid()) Action = null;
            bool owner = Situation.FirstMan == Index;
            bool goalSide = Me.Location.y * Field.Side(Team) >= Ball.Location.y * Field.Side(Team) - 150;

            if (emergency)
            {
                bool keepSave = owner && ReferenceEquals(Action, defensiveShot) && Action is Shot save &&
                    save.IsPredictionValid() && save.Slice.Time - Game.Time < threatTime;
                if (keepSave) return;
                if (owner && (due || risingDanger) && Game.Time >= nextShotSearch)
                {
                    nextShotSearch = Game.Time + 0.05f;
                    defensiveShot = Tactics.SelectShot(this, true, Situation.OpponentEta, _ => false);
                    Action = defensiveShot;
                    if (Action != null) { SetDecision("defend / intercept before goal line"); return; }
                }
                if (Action is IPossessionAction || Action is Shot || Action is GetBoost) Action = null;
                if (!Me.IsGrounded) { Action = new Recover(); SetDecision("defend / recover for next save"); return; }
                if (owner && goalSide && Ball.Location.z < 240 && Me.Location.FlatDist(Ball.Location) < 1500)
                {
                    CloseGap(true);
                    SetDecision("defend / immediate contact challenge");
                    return;
                }
                Vec3 cover = owner ? Tactics.ShadowTarget(Ball.Location, OurGoal.Location, true) : Situation.SupportTarget;
                if (Situation.Role == TeamRole.Anchor)
                {
                    Ball predicted = Ball.Prediction.TrySample(Game.Time + MathF.Min(threatTime, 0.5f), out Ball sample) ? sample : Ball.MainBall;
                    cover = new Vec3(System.Math.Clamp(predicted.location.x * 0.6f, -550, 550), Field.Side(Team) * 4450, 17);
                }
                Position(cover, true);
                SetDecision("defend / goal-mouth cover");
                return;
            }
            // Pressure changes cadence/ownership, not every action into retreat.
            if (pressure && Action is IPossessionAction) Action = null;
            if (!due && Action != null) return;
            if (Action is GetBoost refill)
            {
                // Keep the same GetBoost (and its Drive/flip state) across replans.
                Boost safe = RoutePlanner.SelectRefuel(Me, new[] { refill.ChosenBoost }, Ball.MainBall,
                    Team, LivingTeammates, LivingOpponents, threatTime);
                if (!refill.Finished && safe != null && Me.Location.FlatDist(Ball.Location) > 600) return;
                Action = null;
            }
            if (Action is IPossessionAction && owner) return;
            if (Action is Shot shot && owner && shot.IsPredictionValid() && !HasTeammateEarlierShot(shot.Slice.Time)) return;
            if (Action is IPossessionAction || Action is Shot) Action = null;

            if (Me.IsGrounded && Me.Boost < 35 && Me.Location.FlatDist(Ball.Location) > 1500)
            {
                Boost full = RoutePlanner.SelectRefuel(Me, Field.Boosts, Ball.MainBall, Team,
                    LivingTeammates, LivingOpponents, threatTime);
                if (full != null)
                {
                    Action = new GetBoost(Me, full.Index);
                    SetDecision("boost / committed full-pad refill");
                    return;
                }
            }

            if (!Me.IsGrounded)
            {
                if (owner && Options.AerialCarry && (Situation.HasCover || LivingTeammates.Count == 0) &&
                    AerialCarry.CanStart(Me, Ball.MainBall, Situation.OpponentEta))
                { Action = new AerialCarry(); SetDecision("mechanic / velocity-matched aerial carry"); return; }
                if (owner && Game.Time >= nextShotSearch)
                {
                    nextShotSearch = Game.Time + 0.08f;
                    Shot aerial = Tactics.SelectShot(this, false, Situation.OpponentEta, HasClaim);
                    if (aerial != null) { Action = aerial; SetDecision("attack / airborne intercept"); return; }
                }
                Action = new Recover(); SetDecision("recover / landing surface"); return;
            }

            if (owner && goalSide)
            {
                bool room = Situation.FreeTime > 0.9f && (Situation.HasCover || LivingTeammates.Count == 0);
                bool finish = Ball.Location.FlatDist(TheirGoal.Location) < 2600;
                // Near the opponent's goal, take a feasible finish before starting a new dribble.
                if (finish && Game.Time >= nextShotSearch)
                {
                    nextShotSearch = Game.Time + 0.08f;
                    Shot finishShot = Tactics.SelectShot(this, false, Situation.OpponentEta, HasClaim);
                    if (finishShot != null) { Action = finishShot; SetDecision("attack / interior goal target"); return; }
                }
                if (room && Options.GroundControl && GroundDribble.CanStart(Me, Ball.MainBall, Situation.FreeTime))
                { Action = new GroundDribble(); SetDecision("mechanic / ground carry"); return; }
                if (room && Options.GroundControl && Situation.FreeTime > 1.2f && Ball.Location.z > 200 && GroundCatch.FindCatch(Me) != null)
                { Action = new GroundCatch(); SetDecision("mechanic / cushion catch"); return; }
                if (Me.Boost < 18 && Situation.HasCover && Situation.FreeTime < 0 && TryBoostDetour(Situation.SupportTarget, false)) return;
                if (Game.Time >= nextShotSearch)
                {
                    nextShotSearch = Game.Time + 0.08f;
                    Shot attack = Tactics.SelectShot(this, pressure, Situation.OpponentEta, HasClaim);
                    if (attack != null) { Action = attack; SetDecision(pressure ? "defend / challenge and clear" : "attack / original shot execution"); return; }
                }
                // A failed scoring solution is not a command to stop 700 units behind
                // an opponent forever. Close toward a contact, then replan the shot.
                CloseGap(pressure);
                SetDecision(pressure ? "defend / pressure challenge" : "attack / close to contact");
                return;
            }
            Vec3 support = TeamPlanning.Separate(Me, Situation.SupportTarget, Cars.AllLivingCars, Team,
                Situation.Role == TeamRole.Anchor);
            if (TryBoostDetour(support, false)) return;
            Position(support, false);
            SetDecision(Situation.Role == TeamRole.Anchor ? "support / dedicated anchor" : "support / wide second man");
        }

        private void CloseGap(bool urgent)
        {
            float lookAhead = System.Math.Clamp(Me.Location.FlatDist(Ball.Location) / 4000, 0.05f, 0.25f);
            Vec3 next = Ball.Location + Ball.Velocity * lookAhead;
            Vec3 awayFromGoal = ControlMath.FlatUnit(TheirGoal.Location - next, Me.Forward);
            Position(next - awayFromGoal * 90, urgent, stop: false);
        }

        private bool HasClaim(float sliceTime) => HasTeammateEarlierShot(sliceTime);
        private bool TryBoostDetour(Vec3 destination, bool pressured)
        {
            Boost pad = RoutePlanner.SelectBoost(Me, Field.Boosts, Ball.Location, destination, Team, Situation.OpponentEta,
                lastBack: Situation.LastBack, pressured: pressured, teammates: LivingTeammates, preferred: boostPad);
            if (pad == null) { boostPad = -1; return false; }
            boostPad = pad.Index;
            Position(pad.Location, false, stop: false);
            SetDecision(pad.IsLarge ? "boost / covered large pad" : "boost / small-pad route");
            return true;
        }
        private void Position(Vec3 destination, bool urgent, bool stop = true)
        {
            var action = Action as Positioning ?? new Positioning(destination);
            action.Target = Navigation.FieldTarget(destination);
            action.Urgent = urgent;
            action.Stop = stop;
            action.Reserve = Situation.LastBack ? 35 : 25;
            Action = action;
        }
        private void SetDecision(string decision)
        {
            if (Decision == decision) return;
            Decision = decision;
            if (Options.Trace) Console.WriteLine(FormattableString.Invariant(
                $"stardust t={Game.Time:F3} car={Index} decision={Decision} role={Situation.Role} owner={Situation.FirstMan} boost={Me.Boost:F0} eta={Situation.MyEta:F2} opponent={Situation.OpponentEta:F2}"));
        }
        public bool IsBack() => CanDefend(Me, OurGoal.Location) || Situation.FirstMan == Index;
        public static bool CanBlock(Car car, Vec3 location) =>
            ControlMath.Unit(location - car.Location, Vec3.Up).Dot(ControlMath.Unit(car.Location - Ball.Location, Vec3.Up)) > 0.7f;
        public static bool CanDefend(Car car, Vec3 location)
        {
            if (CanBlock(car, location)) return true;
            float eta = TeamPlanning.EstimateEta(car, location);
            float speed = MathF.Max(Ball.Velocity.Dot(ControlMath.Unit(location - Ball.Location, Vec3.Up)), 1500);
            return eta < Ball.Location.Dist(location) / speed;
        }
    }
}
