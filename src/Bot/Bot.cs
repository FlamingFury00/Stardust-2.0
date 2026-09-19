using System;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    public sealed class StardustOptions
    {
        public bool GroundControl { get; init; } = Environment.GetEnvironmentVariable("STARDUST_GROUND_CONTROL") != "0";
        public bool AerialCarry { get; init; } = Environment.GetEnvironmentVariable("STARDUST_AERIAL_CARRY") != "0";
        public bool FlipResets { get; init; } = Environment.GetEnvironmentVariable("STARDUST_FLIP_RESETS") == "1";
        public bool Trace { get; init; } = Environment.GetEnvironmentVariable("STARDUST_TRACE") == "1";
    }
    public class Stardust : RUBot
    {
        public StardustOptions Options { get; } = new();
        public TacticalFrame Situation { get; private set; } = new();
        public string Decision { get; private set; } = "startup";
        public int CurrentDefender { get; private set; } = -1;
        public bool Shooting { get; set; }
        private float nextPlan = float.NegativeInfinity;
        private bool defending, pressured, announced;
        private Shot defensiveShot;
        private DefensiveRead read;
        private int boostTarget = -1;
        public Stardust(string defaultAgentId = null) : base(defaultAgentId) { }
        public override void Run()
        {
            if (!announced)
            {
                announced = true;
                if (Options.Trace) Console.WriteLine($"stardust options ground={Options.GroundControl} aerial={Options.AerialCarry} experimentalResets={Options.FlipResets}");
            }
            if (ClockReset)
            {
                nextPlan = float.NegativeInfinity; defending = false; pressured = false;
                defensiveShot = null; boostTarget = -1; CurrentDefender = -1;
            }
            Shooting = Action is Shot;
            if (IsKickoff)
            {
                if (Action != null) return;
                int rank = 0;
                foreach (Car car in LivingTeammates) if (Tactics.KickoffBefore(car, Me, Ball.Location, Team)) rank++;
                if (rank == 0) { Action = new Kickoff(); SetDecision("kickoff / taker"); }
                else
                {
                    Move(rank == 1 ? new Vec3(0, Field.Side(Team) * 1200, 17) :
                        new Vec3(-MathF.Sign(Me.Location.x) * 1000, Field.Side(Team) * 3400, 17), 1300);
                    SetDecision(rank == 1 ? "kickoff / cheat" : "kickoff / cover");
                }
                return;
            }
            read = Defense.Read(Ball.MainBall, Ball.Prediction.Slices, OurGoal.Location, Team, Cars.AllLivingCars, Game.Time);
            bool dangerChanged = read.Emergency != defending || read.Pressure != pressured;
            if (read.Emergency && !Defense.MayInterrupt(Action, Jump)) return;
            if (!read.Emergency && Action != null && !Action.Interruptible) return;
            if (dangerChanged)
            {
                nextPlan = float.NegativeInfinity;
                if (read.Emergency || (read.Pressure && Action is IPossessionAction)) Action = null;
            }
            defending = read.Emergency; pressured = read.Pressure;
            if (Action is Shot oldShot && (!oldShot.IsPredictionValid() ||
                (read.Emergency && !Defense.SaveStillTimely(oldShot, Game.Time, read.GoalTime)))) Action = null;
            if (Action == null) nextPlan = MathF.Min(nextPlan, Game.Time);
            // Only the elected saver's fallback tracks the shot lane. Backups keep their own lanes.
            if (read.Emergency && CurrentDefender == Index && Action is Navigate navigation && navigation.Urgent)
                navigation.Target = read.BlockTarget;
            if (Game.Time < nextPlan) return;
            nextPlan = Game.Time + (read.Emergency || read.Pressure ? 0.05f : 0.10f);
            Situation = Tactics.Evaluate(this);
            if (read.Emergency)
            {
                CurrentDefender = Defense.Defender(Cars.AllLivingCars, Team, read.BlockTarget);
                if (CurrentDefender == Index)
                {
                    if (!ReferenceEquals(Action, defensiveShot) || !Defense.SaveStillTimely(defensiveShot, Game.Time, read.GoalTime))
                        defensiveShot = Tactics.SelectShot(this, true, Situation.OpponentEta, _ => false);
                    Action = defensiveShot;
                    if (Action == null) Move(read.BlockTarget, 2300, true);
                    SetDecision(defensiveShot == null ? "defend / reactive goal-line block" : "defend / before-goal intercept");
                }
                else
                {
                    defensiveShot = null;
                    int backupRank = 0;
                    foreach (Car mate in LivingTeammates) if (mate.Index != CurrentDefender && mate.Index < Index) backupRank++;
                    float wing = MathF.Abs(Ball.Location.x) > 200 ? -MathF.Sign(Ball.Location.x) : -Field.Side(Team);
                    Vec3 backup = new(wing * (backupRank == 0 ? 1450 : -1900), Field.Side(Team) * (backupRank == 0 ? 4300 : 3050), 17);
                    Move(backup, 1800, true);
                    SetDecision("defend / separate backup lane");
                }
                return;
            }
            defensiveShot = null; CurrentDefender = -1;
            bool owner = Situation.FirstMan == Index;
            bool goalSide = Me.Location.y * Field.Side(Team) >= Ball.Location.y * Field.Side(Team) - 150;
            if (Action is IPossessionAction && !read.Pressure && (owner || Me.Location.Dist(Ball.Location) < 260)) return;
            if (Action is Shot shot && shot.IsPredictionValid() && !HasTeammateEarlierShot(shot.Slice.Time) &&
                (owner || shot.Slice.Time - Game.Time < 0.25f)) return;
            if (!(Action is Navigate)) Action = null;
            if (!Me.IsGrounded)
            {
                if (owner && CanAttemptReset()) { Action = new FlipReset(Jump); SetDecision("mechanic / reset approach"); return; }
                if (owner && !read.Pressure && Options.AerialCarry && (Situation.HasCover || Ball.Location.y * Field.Side(Team) < 0) &&
                    AerialCarry.CanStart(Me, Ball.MainBall, Situation.OpponentEta))
                { Action = new AerialCarry(); SetDecision("mechanic / velocity-matched carry"); return; }
                Shot airborne = owner ? Tactics.SelectShot(this, read.Pressure, Situation.OpponentEta, HasClaim) : null;
                Action = airborne ?? (IAction)new Recover();
                SetDecision(airborne == null ? "recover / preserve fuel" : "intercept / airborne"); return;
            }
            if (owner && goalSide)
            {
                Shot attack = Tactics.SelectShot(this, read.Pressure, Situation.OpponentEta, HasClaim);
                if (attack != null && (read.Pressure || ShotPlanner.OpenLane(attack, LivingOpponents)))
                { Action = attack; SetDecision(read.Pressure ? "defend / anticipatory clear" : "attack / open shooting lane"); return; }
                if (!read.Pressure)
                {
                    if (Me.Boost < 22 && Situation.FreeTime > 1 && Me.Location.FlatDist(Ball.Location) > 1000 && TryBoost(Situation.SupportTarget)) return;
                    if (Options.GroundControl && GroundDribble.CanStart(Me, Ball.MainBall, Situation.FreeTime))
                    { Action = new GroundDribble(); SetDecision("mechanic / ground carry"); return; }
                    if (Options.GroundControl && Situation.FreeTime > 0.9f && Ball.Location.z > 200 && GroundCatch.FindCatch(Me) != null)
                    { Action = new GroundCatch(); SetDecision("mechanic / cushion catch"); return; }
                }
                if (attack != null) { Action = attack; SetDecision("attack / contact-aligned intercept"); return; }
                if (!read.Pressure && Situation.FreeTime > 0.25f)
                {
                    Vec3 lane = ControlMath.FlatUnit(TheirGoal.Location - Ball.Location, Me.Forward);
                    Move(Ball.Location - lane * 350, 1410); SetDecision("possess / controlled approach"); return;
                }
            }
            Vec3 support = Situation.SupportTarget;
            if (Situation.Role == TeamRole.Cover && read.Pressure) support = read.BlockTarget;
            if (TryBoost(support)) return;
            Move(support, read.Pressure ? 2100 : 1410, read.Pressure);
            SetDecision(owner ? "defend / shadow" : $"support / {Situation.Role}");
        }
        public bool CanAttemptReset() => Options.FlipResets && !read.Emergency && !read.Pressure && Situation.OpponentEta > 1.2f && Me.Boost > 30 &&
            (Situation.HasCover || (Situation.TeamCount <= 1 && Ball.Location.y * Field.Side(Team) < -1500)) && FlipReset.CanStart(Me, Ball.MainBall, Jump);
        private bool HasClaim(float time) => HasTeammateEarlierShot(time);
        private bool TryBoost(Vec3 destination)
        {
            Boost pad = RoutePlanner.SelectBoost(Me, Field.Boosts, Ball.Location, destination, Team, Situation.OpponentEta,
                cars: Cars.AllLivingCars, pressure: read.Pressure, preferred: boostTarget);
            if (pad == null) { boostTarget = -1; return false; }
            boostTarget = pad.Index;
            Move(pad.Location, 1410);
            ((Navigate)Action).StopAtTarget = false;
            SetDecision(pad.IsLarge ? "boost / covered full pickup" : "boost / small-pad route"); return true;
        }
        private void Move(Vec3 target, float speed, bool urgent = false)
        {
            if (!ControlMath.Finite(target)) target = new Vec3(0, Field.Side(Team) * 4400, 17);
            if (Action is Navigate navigate)
            { navigate.Target = target; navigate.Speed = speed; navigate.Urgent = urgent; navigate.StopAtTarget = true; }
            else Action = new Navigate(target, speed, urgent);
        }
        private void SetDecision(string decision)
        {
            if (Decision == decision) return;
            Decision = decision;
            if (Options.Trace) Console.WriteLine(FormattableString.Invariant(
                $"stardust t={Game.Time:F3} car={Index} decision={Decision} role={Situation.Role} boost={Me.Boost:F0} eta={Situation.MyEta:F2} opponent={Situation.OpponentEta:F2}"));
        }
        public bool IsBack() => CanDefend(Me, OurGoal.Location) || Situation.FirstMan == Index;
        public static bool CanBlock(Car car, Vec3 location) => ControlMath.Unit(location - car.Location, Vec3.Up)
            .Dot(ControlMath.Unit(car.Location - Ball.Location, Vec3.Up)) > 0.7f;
        public static bool CanDefend(Car car, Vec3 location)
        {
            if (CanBlock(car, location)) return true;
            float eta = Drive.GetEta(car, location, false);
            float speed = MathF.Max(Ball.Velocity.Dot(ControlMath.Unit(location - Ball.Location, Vec3.Up)), 1500);
            return float.IsFinite(eta) && eta < Ball.Location.Dist(location) / speed;
        }
    }
}
