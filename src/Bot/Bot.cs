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

        public Stardust(string defaultAgentId = null) : base(defaultAgentId) { }

        public override void Run()
        {
            if (Teammates.Count >= 1)
            {
                // Kickoff
                if (IsKickoff && IsClosestKickoff(Me) && Action == null)
                {
                    Action = new Kickoff();
                }
                else if (IsKickoff && IsSecondClosestKickoff() && Action == null)
                {
                    // Second man: cheat up to center then rotate to back boost to be ready
                    Vec3 cheatPos = new Vec3(0, Field.Side(Team) * -500, 0);
                    Action = new Drive(Me, cheatPos, 1500f, allowDodges: true, wasteBoost: false);
                    return; // Keep it simple during kickoff frame
                }

                // Prioritize boost grabbing when low and safe before generic rotation
                if (Me.Boost < 30 && !IsLastOneBack() && !ShouldDefend() && Action == null)
                {
                    Boost targetBoost = GetBestBoost();
                    if (targetBoost != null)
                    {
                        Action = new GetBoost(Me, targetBoost.Index);
                    }
                }

                // Rotation and positioning
                if (ShouldRotate() && Action == null)
                {
                    Vec3 desiredZone = Zone5Positioning();
                    Action = new Drive(Me, desiredZone);
                }

                // Attack
                if ((ShouldAttack() && IsClosest(Me, true) && Action == null) || (Ball.LatestTouch != null && Ball.LatestTouch.Team == Me.Team && Action == null))
                {
                    Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));
                    if (shot != null)
                    {
                        Action = shot;
                    }
                    else
                    {
                        // Safer fallback: arrive to a strong shooting lane instead of QuickShot
                        Vec3 lane = new Vec3(Utils.Cap(Ball.Location.x, -1500, 1500), Ball.Location.y - 800 * Field.Side(Team), 0);
                        Action = new Arrive(Me, Field.LimitToNearestSurface(lane), (TheirGoal.Location - Ball.Location).Flatten());
                    }
                }

                if ((ShouldAttack() && IsSecondClosest() && GetClosestTeammate().IsGrounded && Action == null) || (Ball.LatestTouch != null && Ball.LatestTouch.Team == Me.Team && Action == null))
                {
                    // Second man: do not also take the shot to avoid double commits; support instead
                    Action = GetSupportingAction();
                }

                if ((ShouldDefend() && IsClosest(Me, true) && Action == null) || (Ball.LatestTouch != null && Ball.LatestTouch.Team != Me.Team && Action == null))
                {
                    Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal, true));
                    if (shot != null)
                    {
                        Action = shot;
                    }
                    else
                    {
                        // Integrate shadowing behavior when no clear shot/clear is found
                        Action = new Shadow(Me);
                    }
                }

                if ((ShouldDefend() && IsSecondClosest() && GetClosestTeammate().IsGrounded && Action == null) || (Ball.LatestTouch != null && Ball.LatestTouch.Team == Me.Team && Action == null))
                {
                    // Second man on defense: hold supporting defensive position
                    Action = GetSupportingDefenseAction();
                }
            }
            else
            {
                // Kickoff
                if (IsKickoff && Action == null)
                {
                    Action = new Kickoff();
                }

                // Rotation and positioning
                if (ShouldRotate() && Action == null)
                {
                    Vec3 desiredZone = Zone5Positioning();
                    Action = new Drive(Me, desiredZone);
                }

                // Boost grabbing (1v1): prioritize when not defending or last back
                if (Me.Boost < 36 && IsClosest(Me) && !ShouldDefend() && !IsLastOneBack() && Action == null)
                {
                    Boost targetBoost = GetBestBoost();
                    if (targetBoost != null)
                    {
                        Action = new GetBoost(Me, targetBoost.Index);
                    }
                }

                // Attack
                if (ShouldAttack() && Action == null)
                {
                    Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));
                    if (shot != null)
                    {
                        Action = shot;
                    }
                    else
                    {
                        // Safer fallback in 1v1: arrive to ball on shooting lane
                        Vec3 dir = (TheirGoal.Location - Ball.Location).Flatten();
                        Vec3 lane = Field.LimitToNearestSurface(Ball.Location - dir.Normalize() * 900);
                        Action = new Arrive(Me, lane, dir);
                    }
                }

                //Defend
                if (ShouldDefend() && Action == null)
                {
                    Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));
                    if (shot != null)
                    {
                        Action = shot;
                    }
                    else
                    {
                        Action = new Shadow(Me);
                    }
                }
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
                ? new GetBoost(Me, interruptible: false)
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
        private static Vec3 CalculateSupportPosition(Vec3 teammateLocation, Vec3 ballLocation, Vec3 goalLocation)
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
        public static bool CanBlock(Car car, Vec3 location)
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
        private static float CalculatePower(Car car, BallSlice intercept, float eta)
        {
            Ball ball = intercept != null ? intercept.ToBall() : Ball.MainBall;
            return MathF.Max((ball.velocity - ((ball.location - car.Location) / eta)).Length(), ((ball.location - car.Location) / eta).Length());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanDefend(Car car, Vec3 location)
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