using System;
using RedUtils;
using RedUtils.Math;

namespace Bot
{
    /// <summary>Independent feature switches support in-game ablation against the baseline.</summary>
    public sealed class StardustOptions
    {
        public bool GroundControl { get; init; } = Environment.GetEnvironmentVariable("STARDUST_GROUND_CONTROL") != "0";
        public bool AerialCarry { get; init; } = Environment.GetEnvironmentVariable("STARDUST_AERIAL_CARRY") != "0";
        public bool FlipResets { get; init; } = Environment.GetEnvironmentVariable("STARDUST_FLIP_RESETS") == "1";
        public bool Trace { get; init; } = Environment.GetEnvironmentVariable("STARDUST_TRACE") == "1";
    }

    /// <summary>Threat-first planning around persistent closed-loop mechanics; a 3.0 evaluation candidate.</summary>
    public class Stardust : RUBot
    {
        public StardustOptions Options { get; } = new();
        public TacticalFrame Situation { get; private set; } = new();
        public string Decision { get; private set; } = "startup";
        public bool Shooting { get; set; }
        private float nextPlan = float.NegativeInfinity;
        private bool defending;
        private Shot defensiveShot;
        public Stardust(string defaultAgentId = null) : base(defaultAgentId) { }

        public override void Run()
        {
            if (ClockReset) { nextPlan = float.NegativeInfinity; defending = false; defensiveShot = null; }
            Shooting = Action is Shot;
            if (IsKickoff)
            {
                if (Action != null) return;
                int rank = 0;
                foreach (Car car in LivingTeammates)
                    if (Tactics.KickoffBefore(car, Me, Ball.Location, Team)) rank++;
                if (rank == 0) { Action = new Kickoff(); SetDecision("kickoff / taker"); }
                else
                {
                    Vec3 target = rank == 1 ? new Vec3(0, Field.Side(Team) * 1200, 17) :
                        new Vec3(-MathF.Sign(Me.Location.x) * 750, Field.Side(Team) * 3500, 17);
                    DriveTo(target, rank == 1 ? 1300 : 1600, false);
                    SetDecision(rank == 1 ? "kickoff / cheat" : "kickoff / cover");
                }
                return;
            }

            float threat = Tactics.GoalThreat(Ball.Prediction.Slices, OurGoal.Location, Game.Time);
            bool emergency = float.IsFinite(threat);
            bool risingThreat = emergency && !defending;
            // Leave the rising-threat edge pending while a physically committed dodge finishes.
            if (Action != null && !Action.Interruptible) return;
            defending = emergency;
            if (Action is Shot oldShot && !oldShot.IsPredictionValid()) Action = null;
            if (risingThreat) { Action = null; nextPlan = float.NegativeInfinity; }
            if (Action == null) nextPlan = MathF.Min(nextPlan, Game.Time);
            if (Game.Time < nextPlan) return;
            nextPlan = Game.Time + 0.12f;
            Situation = Tactics.Evaluate(this);

            if (emergency)
            {
                // An attacking shot must not masquerade as an already planned defensive clear.
                if (!(Action is Shot) || !ReferenceEquals(Action, defensiveShot))
                {
                    defensiveShot = Tactics.SelectShot(this, true, Situation.OpponentEta, _ => false);
                    Action = defensiveShot;
                }
                if (Action == null) DriveTo(Tactics.ShadowTarget(Ball.Location, OurGoal.Location, true), 2300, false);
                SetDecision("defend / predicted goal");
                return;
            }

            if (Action is IPossessionAction) return;
            if (Action is Shot shot && shot.IsPredictionValid() && !HasTeammateEarlierShot(shot.Slice.Time)) return;
            if (!(Action is Drive)) Action = null;
            bool owner = Situation.FirstMan == Index;
            bool goalSide = Me.Location.y * Field.Side(Team) >= Ball.Location.y * Field.Side(Team) - 150;
            if (!Me.IsGrounded)
            {
                if (owner && Options.AerialCarry && AerialCarry.CanStart(Me, Ball.MainBall, Situation.OpponentEta))
                { Action = new AerialCarry(); SetDecision("mechanic / aerial carry"); return; }
                Shot aerial = owner ? Tactics.SelectShot(this, false, Situation.OpponentEta, HasClaim) : null;
                Action = aerial ?? (IAction)new Recover();
                SetDecision(aerial == null ? "recover / landing surface" : "attack / airborne intercept");
                return;
            }

            if (owner && goalSide)
            {
                if (Options.GroundControl && GroundDribble.CanStart(Me, Ball.MainBall, Situation.FreeTime))
                { Action = new GroundDribble(); SetDecision("mechanic / ground carry"); return; }
                if (Options.GroundControl && Situation.FreeTime > 0.7f && Ball.Location.z > 200 && GroundCatch.FindCatch(Me) != null)
                { Action = new GroundCatch(); SetDecision("mechanic / cushion catch"); return; }
                Shot attack = Tactics.SelectShot(this, false, Situation.OpponentEta, HasClaim);
                if (attack != null) { Action = attack; SetDecision("attack / economical intercept"); return; }
                if (Situation.FreeTime > 0.25f)
                {
                    Vec3 lane = ControlMath.FlatUnit(TheirGoal.Location - Ball.Location, Me.Forward);
                    DriveTo(Field.LimitToNearestSurface(Ball.Location - lane * 350), 1500, false);
                    SetDecision("possess / approach behind ball");
                    return;
                }
            }
            Vec3 support = Tactics.ShadowTarget(Ball.Location, OurGoal.Location, Situation.LastBack);
            if (TryBoostDetour(support)) return;
            DriveTo(support, Situation.LastBack ? 1800 : 2100, true);
            SetDecision(owner ? "defend / shadow challenge" : "support / goal-side cover");
        }

        private bool HasClaim(float sliceTime) => HasTeammateEarlierShot(sliceTime);
        private bool TryBoostDetour(Vec3 destination)
        {
            if (Ball.Location.y * Field.Side(Team) > 2500) return false;
            Boost pad = RoutePlanner.SelectBoost(Me, Field.Boosts, Ball.Location, destination, Team, Situation.OpponentEta);
            if (pad == null) return false;
            DriveTo(pad.Location, 1800, false);
            SetDecision(pad.IsLarge ? "support / on-route large boost" : "support / small-pad route");
            return true;
        }
        private void DriveTo(Vec3 destination, float speed, bool allowDodges)
        {
            if (!ControlMath.Finite(destination)) destination = OurGoal.Location;
            if (Action is Drive drive)
            {
                drive.Target = destination;
                drive.TargetSpeed = speed;
                drive.AllowDodges = allowDodges;
                drive.WasteBoost = false;
            }
            else Action = new Drive(Me, destination, speed, allowDodges, wasteBoost: false);
        }
        private void SetDecision(string decision)
        {
            if (Decision == decision) return;
            Decision = decision;
            if (Options.Trace) Console.WriteLine(FormattableString.Invariant(
                $"stardust t={Game.Time:F3} car={Index} decision={Decision} eta={Situation.MyEta:F2} opponent={Situation.OpponentEta:F2}"));
        }
        // Retained for compatibility with the original Shadow action.
        public bool IsBack() => CanDefend(Me, OurGoal.Location) || Situation.FirstMan == Index;
        public static bool CanBlock(Car car, Vec3 location) =>
            ControlMath.Unit(location - car.Location, Vec3.Up).Dot(ControlMath.Unit(car.Location - Ball.Location, Vec3.Up)) > 0.7f;
        public static bool CanDefend(Car car, Vec3 location)
        {
            if (CanBlock(car, location)) return true;
            float eta = Drive.GetEta(car, location);
            float speed = MathF.Max(Ball.Velocity.Dot(ControlMath.Unit(location - Ball.Location, Vec3.Up)), 1500);
            return float.IsFinite(eta) && eta < Ball.Location.Dist(location) / speed;
        }
    }
}
