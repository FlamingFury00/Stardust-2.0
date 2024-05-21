using RedUtils;
using RedUtils.Math;
using System;
using System.Runtime.CompilerServices;

namespace Bot
{
    public class PossesionProperties
    {
        public bool HasPossesion { get; set; }
        public BallSlice Intercept { get; set; }
        public float FreeTime { get; set; }
        public float Eta { get; set; }
        public float Power { get; set; }
    }

    public class Stardust : RUBot
    {
        public bool Shooting { get; set; }

        public Stardust(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex) { }

        public override void Run()
        {
            // Kickoff
            if (IsKickoff && IsClosestKickoff(Me) && Action == null)
            {
                Action = new Kickoff();
            }
            else if (IsKickoff && IsSecondClosestKickoff() && Action == null)
            {
                return;
            }

            // Rotation and positioning
            if (ShouldRotate() && Action == null)
            {
                Vec3 desiredZone = Zone5Positioning();
                Action = new Drive(Me, desiredZone);
            }

            // Boost grabbing
            if (Me.Boost < 30 && IsSecondClosest() && Action == null)
            {
                Boost targetBoost = GetBestBoost();
                if (targetBoost != null)
                {
                    Action = new Drive(Me, targetBoost.Location);
                }
            }

            // Attack
            if ((ShouldAttack() && IsClosest(Me, true) && Action == null) || (Ball.LatestTouch != null && Ball.LatestTouch.Team == Me.Team && Action == null))
            {
                Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));
                Action = shot ?? GetOffensiveAction();
            }

            if ((ShouldAttack() && IsSecondClosest() && GetClosestTeammate().IsGrounded && Action == null) || (Ball.LatestTouch != null && Ball.LatestTouch.Team == Me.Team && Action == null))
            {
                Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));
                Action = shot ?? GetSupportingAction();
            }

            if ((ShouldDefend() && IsClosest(Me, true) && Action == null) || (Ball.LatestTouch != null && Ball.LatestTouch.Team == Me.Team && Action == null))
            {
                Shot shot = FindShot(DefaultShotCheck, new Target(OurGoal, true));
                Action = shot ?? GetDefensiveAction();
            }

            if ((ShouldDefend() && IsSecondClosest() && GetClosestTeammate().IsGrounded && Action == null) || (Ball.LatestTouch != null && Ball.LatestTouch.Team == Me.Team && Action == null))
            {
                Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));
                Action = shot ?? GetSupportingDefenseAction();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IAction GetOffensiveAction()
        {
            if (IsInFrontOfBall() && AreNoBotsBack())
            {
                Vec3 location3 = new(800 * -MathF.Sign(Ball.Location.x), 4900 * Field.Side(Team));
                return new Drive(Me, location3, wasteBoost: true);
            }

            return GetClosestOpponent().Location.Dist(Ball.Location) > GetClosestTeammate().Location.Dist(Ball.Location)
                ? new GetBoost(Me)
                : (IAction)null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IAction GetSupportingAction()
        {
            if (AreNoBotsBack())
            {
                Vec3 location3 = new(800 * -MathF.Sign(Ball.Location.x), 4900 * Field.Side(Team));
                return new Drive(Me, location3);
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IAction GetDefensiveAction()
        {
            return CanDefend(Me, OurGoal.Location) ? new Drive(Me, OurGoal.Location) : (IAction)null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IAction GetSupportingDefenseAction()
        {
            if (CanDefend(Me, OurGoal.Location))
            {
                return new Drive(Me, OurGoal.Location);
            }

            Car closestTeammate = GetClosestTeammate();
            if (closestTeammate != null && CanDefend(closestTeammate, OurGoal.Location))
            {
                Vec3 supportPosition = CalculateSupportPosition(closestTeammate.Location, Ball.Location, OurGoal.Location);
                return new Drive(Me, supportPosition);
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vec3 CalculateSupportPosition(Vec3 teammateLocation, Vec3 ballLocation, Vec3 goalLocation)
        {
            Vec3 direction = goalLocation - teammateLocation;
            Vec3 perpendicular = direction.Normalize().Cross(Vec3.Up);
            Vec3 supportPosition = teammateLocation + (perpendicular * 1000);

            if (supportPosition.Dist(ballLocation) < 500)
            {
                supportPosition = teammateLocation - (perpendicular * 1000);
            }

            return supportPosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanBlock(Car car, Vec3 location)
        {
            return car.Location.Direction(location).Dot(Ball.Location.Direction(car.Location)) > 0.699999988079071;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsBack()
        {
            return CanDefend(Me, OurGoal.Location) || MyBall(out _).HasPossesion;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PossesionProperties MyBall(out PossesionProperties theirPossesion)
        {
            float minOpponentEta = 6f;
            float opponentPower = 0.0f;
            BallSlice opponentIntercept = null;
            float minEta = 6f;

            foreach (Car car in Cars.AllLivingCars)
            {
                BallSlice intercept = Ball.Prediction.Find(slice => Drive.GetEta(car, slice.Location) < slice.Time - Game.Time);
                Vec3 interceptLocation = intercept != null ? intercept.Location : Ball.Location;
                float eta = intercept != null ? intercept.Time - Game.Time : (car.Location.Dist(interceptLocation) < 500.0 ? 0.0f : car.Forward.Angle(car.Location.Direction(interceptLocation)));

                if (eta < minEta)
                {
                    minEta = eta;
                }

                if (eta < minOpponentEta && car.Team != Team && car.Location.Direction(TheirGoal.Location).Dot(interceptLocation.Direction(car.Location)) > 0.0)
                {
                    minOpponentEta = eta;
                    opponentIntercept = intercept;
                    opponentPower = CalculatePower(car, intercept, eta);
                }
            }

            BallSlice myIntercept = Ball.Prediction.Find(slice => Drive.GetEta(Me, slice.Location) < slice.Time - Game.Time);
            Vec3 myInterceptLocation = myIntercept != null ? myIntercept.Location : Ball.Location;
            float myEta = myIntercept != null ? myIntercept.Time - Game.Time : 6f;
            bool isFacingGoal = Me.Location.Direction(OurGoal.Location).Dot(myInterceptLocation.Direction(Me.Location)) > 0.0;

            theirPossesion = new PossesionProperties
            {
                FreeTime = minEta - minOpponentEta,
                HasPossesion = minEta > minOpponentEta - 0.2f,
                Intercept = opponentIntercept,
                Eta = minOpponentEta,
                Power = opponentPower
            };

            return new PossesionProperties
            {
                FreeTime = minEta - myEta,
                HasPossesion = minEta > myEta - 0.2f && isFacingGoal,
                Intercept = myIntercept,
                Eta = myEta,
                Power = CalculatePower(Me, myIntercept, myEta)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculatePower(Car car, BallSlice intercept, float eta)
        {
            Ball ball = intercept != null ? intercept.ToBall() : Ball.MainBall;
            return MathF.Max((ball.velocity - ((ball.location - car.Location) / eta)).Length(), ((ball.location - car.Location) / eta).Length());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanDefend(Car car, Vec3 location)
        {
            if (CanBlock(car, location))
            {
                return true;
            }

            double eta = Drive.GetEta(car, location);
            Vec3 ballLocation = Ball.Location;
            double distanceToLocation = ballLocation.Dist(location);
            Vec3 ballVelocity = Ball.Velocity;
            double maxBallSpeed = MathF.Max(ballVelocity.Dot(Ball.Location.Direction(location)), 1500f);
            double timeToReachLocation = distanceToLocation / maxBallSpeed;

            return eta < timeToReachLocation;
        }
    }
}