using RedUtils;
using RedUtils.Math;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RedUtils.Math
{
    public static class Vec3Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AngleBetween(this Vec3 vector1, Vec3 vector2)
        {
            float dotProduct = vector1.Dot(vector2);
            float magnitudes = vector1.Length() * vector2.Length();
            return magnitudes > 0 ? MathF.Acos(dotProduct / magnitudes) : 0f;
        }
    }
}

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

    public enum Playstyle
    {
        Offensive,
        Defensive,
        Control
    }

    public class Stardust : RUBot
    {
        private Playstyle currentPlaystyle = Playstyle.Control;

        public Stardust(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex) { }

        public override void Run()
        {
            //Renderer.Text2D(Action != null ? Action.ToString() : "", new Vec3(10, 10), 4, Color.White);
            //if (Action != null) { return; }

            if (IsKickoff && Action == null)
            {
                HandleKickoff();
            }
            else if (Action == null || (Action is Shadow) && Action.Interruptible)
            {
                HandleGameplay();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleKickoff()
        {
            bool goingForKickoff = true; // by default, go for kickoff
            foreach (Car teammate in Teammates)
            {
                // if any teammates are closer to the ball, then don't go for kickoff
                goingForKickoff = goingForKickoff && Me.Location.Dist(Ball.Location) <= teammate.Location.Dist(Ball.Location);
            }

            // If not going for kickoff, position based on the role
            if (!goingForKickoff)
            {
                if (IsSecondClosestKickoff())
                {
                    Action = new Drive(Me, GetSecondKickoffPosition());
                }
                else
                {
                    Action = new Drive(Me, GetThirdKickoffPosition());
                }
            }
            else
            {
                Action = new Kickoff();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vec3 GetSecondKickoffPosition()
        {
            // Position the second-closest bot slightly behind and to the side of the kickoff bot
            Car kickoffBot = Teammates.Concat(new[] { Me }).OrderByDescending(c => c.Boost).First();
            return kickoffBot.Location - new Vec3(500, 500 * Field.Side(Team), 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vec3 GetThirdKickoffPosition()
        {
            // Position the third bot further back and closer to the defensive half
            return new Vec3(0, 3000 * Field.Side(Team), 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleGameplay()
        {
            DeterminePlaystyle();

            Shot shot = FindShot(ImprovedShotCheck, new Target(TheirGoal));

            if (shot != null && GetClosestTeammate().IsGrounded)
            {
                Action = shot;
            }
            else
            {
                HandleRotations();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DeterminePlaystyle()
        {
            int teamScore = Team == 0 ? Game.BlueScore : Game.OrangeScore;
            int opponentScore = Team == 0 ? Game.OrangeScore : Game.BlueScore;
            int scoreDifference = opponentScore - teamScore;

            // Adjust the playstyle based on the score difference
            if (scoreDifference >= 2)
            {
                currentPlaystyle = Playstyle.Defensive;
            }
            else if (scoreDifference <= -2)
            {
                currentPlaystyle = Playstyle.Offensive;
            }
            else
            {
                currentPlaystyle = Playstyle.Control;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleRotations()
        {
            switch (currentPlaystyle)
            {
                case Playstyle.Offensive:
                    Console.WriteLine("We're LOSING! KEEP PUSHING!!");
                    HandleOffensiveRotation();
                    break;
                case Playstyle.Defensive:
                    Console.WriteLine("We're WINNING! LET'S DEFENDE (but don't forget to attack)");
                    HandleDefensiveRotation();
                    break;
                case Playstyle.Control:
                    HandleControlRotation();
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleOffensiveRotation()
        {
            if (IsClosest(Me, true) && GetClosestTeammate().IsGrounded)
            {
                Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));
                Action = shot ?? Action ?? null;
            }
            else if (IsSecondClosest(Me))
            {
                Action = new Drive(Me, GetOffensiveSupportPosition());
            }
            else
            {
                Boost boost = GetBestBoost();
                if (boost != null)
                    Action = new Drive(Me, boost.Location);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vec3 GetOffensiveSupportPosition()
        {
            float distanceBehindBall = Math.Clamp(Ball.Velocity.Length() / 1500.0f, 500.0f, 1000.0f);
            Vec3 supportPosition = new Vec3(Ball.Location.x, Ball.Location.y + distanceBehindBall * Field.Side(Team), 0);

            foreach (Car teammate in Teammates)
            {
                if ((teammate.Location - supportPosition).Length() < 300.0f)
                {
                    supportPosition += new Vec3(300, 300 * Field.Side(Team), 0);
                }
            }

            return supportPosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleDefensiveRotation()
        {
            if (IsLastOneBack())
            {
                //Action = new Drive(Me, GetDefensivePosition());
                Action = new ParkAt(Me, OurGoal.Location);
            }
            else if (IsSecondClosest(Me))
            {
                Action = new Drive(Me, GetDefensiveSupportPosition());
            }
            else
            {
                Boost boost = GetBestBoost();
                if (boost != null)
                    Action = new Drive(Me, boost.Location, allowDodges: false);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vec3 GetDefensiveSupportPosition()
        {
            Vec3 goalPosition = OurGoal.Location;
            Vec3 supportPosition = new Vec3(goalPosition.x, goalPosition.y - 1000 * Field.Side(Team), goalPosition.z);

            foreach (Car teammate in Teammates)
            {
                if ((teammate.Location - supportPosition).Length() < 500.0f)
                {
                    supportPosition += new Vec3(500, 500 * Field.Side(Team), 0);
                }
            }

            return supportPosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleControlRotation()
        {
            if (IsClosest(Me, true) && GetClosestTeammate().IsGrounded)
            {
                Shot shot = FindShot(ImprovedShotCheck, new Target(TheirGoal, true));
                Action = shot ?? Action ?? null;
            }
            else if (IsSecondClosest(Me))
            {
                //Action = new Drive(Me, GetControlSupportPosition());
                Action = new Shadow(Me);
            }
            else
            {
                Boost boost = GetBestBoost();
                if (boost != null)
                    Action = new Drive(Me, boost.Location, allowDodges: false);
            }
        }

        private Vec3 GetControlSupportPosition()
        {
            float distanceBehindBall = Math.Clamp(Ball.Velocity.Length() / 1000.0f, 800.0f, 1200.0f);
            Vec3 supportPosition = new Vec3(Ball.Location.x, Ball.Location.y + distanceBehindBall * Field.Side(Team), 0);

            foreach (Car teammate in Teammates)
            {
                if ((teammate.Location - supportPosition).Length() < 400.0f)
                {
                    supportPosition += new Vec3(400, 400 * Field.Side(Team), 0);
                }
            }

            return supportPosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Shot ImprovedShotCheck(BallSlice slice, Target target)
        {
            if (slice != null && slice.Time > Game.Time && target.Fits(slice.Location))
            {
                float timeRemaining = slice.Time - Game.Time;
                Ball ballAfterHit = slice.ToBall();
                Vec3 carFinVel = ((slice.Location - Me.Location) / timeRemaining).Cap(0, Car.MaxSpeed);
                ballAfterHit.velocity = carFinVel + slice.Velocity.Flatten(carFinVel.Normalize()) * 0.75f;
                Vec3 shotTarget = target.Clamp(ballAfterHit);

                float shotScore = EvaluateShotQuality(slice, shotTarget);

                if (shotScore > 0.5f)
                {
                    AerialShot aerialShot = new AerialShot(Me, slice, shotTarget);
                    if (aerialShot.IsValid(Me))
                        return aerialShot;

                    GroundShot groundShot = new GroundShot(Me, slice, shotTarget);
                    if (groundShot.IsValid(Me))
                        return groundShot;

                    JumpShot jumpShot = new JumpShot(Me, slice, shotTarget);
                    if (jumpShot.IsValid(Me))
                        return jumpShot;

                    DoubleJumpShot doubleJumpShot = new DoubleJumpShot(Me, slice, shotTarget);
                    if (doubleJumpShot.IsValid(Me))
                        return doubleJumpShot;
                }
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float EvaluateShotQuality(BallSlice slice, Vec3 shotTarget)
        {
            float distanceToGoal = (shotTarget - TheirGoal.Location).Length();
            float angleToGoal = (shotTarget - slice.Location).AngleBetween(TheirGoal.Location - slice.Location);
            float shotPower = slice.Velocity.Length();

            // Consider teammate positioning for potential passes or rebounds
            float teammateSupportScore = 0.0f;
            foreach (Car teammate in Teammates)
            {
                float teammateDistance = (teammate.Location - shotTarget).Length();
                if (teammateDistance < 2000.0f)
                {
                    teammateSupportScore += 1.0f - (teammateDistance / 2000.0f);
                }
            }

            // Combine factors into a final score
            float score = 0.5f - (distanceToGoal / 10000.0f) - (angleToGoal / MathF.PI) + (shotPower / 3000.0f) + (teammateSupportScore / Teammates.Count);

            return Math.Clamp(score, 0.0f, 1.0f);
        }

        private Vec3 GetSupportPosition()
        {
            // Position the support bot slightly behind and to the side of the ball
            // Adjust the distance based on the ball's speed and teammate positions
            float ballSpeed = Ball.Velocity.Length();
            float distanceBehindBall = Math.Clamp(ballSpeed / 1000.0f, 500.0f, 1500.0f);

            // Adjust the position based on teammate locations
            Vec3 supportPosition = new Vec3(Ball.Location.x, Ball.Location.y + distanceBehindBall * Field.Side(Team), 0);
            foreach (Car teammate in Teammates)
            {
                if ((teammate.Location - supportPosition).Length() < 500.0f)
                {
                    supportPosition += new Vec3(500, 500 * Field.Side(Team), 0);
                }
            }

            return supportPosition;
        }

        private Vec3 GetDefensivePosition()
        {
            // Use the Zone5Positioning method as a base
            Vec3 zone5Position = Zone5Positioning();

            // Adjust the position based on the ball's trajectory and opponent positions
            Vec3 ballTrajectory = Ball.Velocity.Normalize();
            Vec3 adjustedPosition = zone5Position;

            foreach (Car opponent in Opponents)
            {
                if ((opponent.Location - Ball.Location).Length() < 2000.0f)
                {
                    Vec3 opponentToGoal = (TheirGoal.Location - opponent.Location).Normalize();
                    float angleToGoal = Vec3Extensions.AngleBetween(opponentToGoal, ballTrajectory);

                    if (angleToGoal < MathF.PI / 4.0f)
                    {
                        adjustedPosition += (TheirGoal.Location - opponent.Location).Normalize() * 500.0f;
                    }
                }
            }

            return adjustedPosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private new Boost GetBestBoost()
        {
            Boost bestBoost = null;
            float bestScore = float.PositiveInfinity;

            List<Boost> availableBoosts = Field.Boosts.FindAll(boost =>
                boost.IsLarge &&
                boost.IsActive &&
                (boost.Location.Dist(OurGoal.Location)) < Ball.Location.Dist(OurGoal.Location) &&
                (GetClosestOpponent().Location.Dist(Ball.Location)) > (Me.Location.Dist(Ball.Location)) &&
                GetClosestOpponent().Location.Length() > 1000
            );

            foreach (var boost in availableBoosts)
            {
                Vec3 boostToBall = (Ball.Location - boost.Location).Normalize();
                Vec3 botToBoost = (boost.Location - Me.Location).Normalize();
                Vec3 botDirection = Me.Forward;

                float distanceToBoost = (boost.Location.Dist(Me.Location));
                float distanceBoostToBall = (boost.Location.Dist(Ball.Location));
                float angle = (float)System.Math.Acos(botDirection.Dot(botToBoost));

                // Prioritize boost collection based on the bot's role
                float roleFactor = IsClosest(Me, true) ? 0.5f : 1.0f;

                float score = distanceToBoost + distanceBoostToBall + angle * 100 * roleFactor;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestBoost = boost;
                }
            }

            return bestBoost;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private new bool IsClosest(Car ownCar, bool teamOnly = false)
        {
            if (Teammates.Count == 0 && teamOnly)
            {
                return true;
            }

            if (teamOnly)
            {
                foreach (Car car in Cars.AllLivingCars)
                {
                    if (ownCar.Location.Dist(Ball.Location) > car.Location.Dist(Ball.Location) && car.Team == ownCar.Team)
                        return false;
                }

                return true;
            }
            else
            {
                foreach (Car car in Cars.AllLivingCars)
                {
                    if (ownCar.Location.Dist(Ball.Location) > car.Location.Dist(Ball.Location))
                        return false;
                }

                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsSecondClosest(Car ownCar)
        {
            foreach (Car car in Cars.AllLivingCars)
            {
                if (ownCar.Location.Dist(Ball.Location) > car.Location.Dist(Ball.Location) && car.Team == ownCar.Team)
                    return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanBlock(Car car, Vec3 location) => car.Location.Direction(location).Dot(Ball.Location.Direction(car.Location)) > 0.699999988079071;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsBack() => CanDefend(Me, OurGoal.Location) || MyBall(out _).HasPossesion;

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
                    minEta = eta;

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
            return MathF.Max((ball.velocity - (ball.location - car.Location) / eta).Length(), ((ball.location - car.Location) / eta).Length());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanDefend(Car car, Vec3 location)
        {
            if (CanBlock(car, location))
                return true;

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