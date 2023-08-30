using RedUtils;
using RedUtils.Math;
using System;
/* 
* This is the main file. It contains your bot class. Feel free to change the name!
* An instance of this class will be created for each instance of your bot in the game.
* Your bot derives from the "RedUtilsBot" class, contained in the Bot file inside the RedUtils project.
* The run function listed below runs every tick, and should contain the custom strategy code (made by you!)
* Right now though, it has a default ball chase strategy. Feel free to read up and use anything you like for your own strategy.
*/
namespace Bot
{
    public class PossesionProperties
    {
        private bool hasPossesion;
        private BallSlice intercept;
        private float freeTime;
        private float eta;
        private float power;

        public bool HasPossesion { get => HasPossesion1; set => HasPossesion1 = value; }
        public BallSlice Intercept { get => Intercept1; set => Intercept1 = value; }
        public float FreeTime { get => FreeTime1; set => FreeTime1 = value; }
        public float Eta { get => Eta1; set => Eta1 = value; }
        public float Power { get => Power1; set => Power1 = value; }
        public bool HasPossesion1 { get => hasPossesion; set => hasPossesion = value; }
        public BallSlice Intercept1 { get => intercept; set => intercept = value; }
        public float FreeTime1 { get => freeTime; set => freeTime = value; }
        public float Eta1 { get => eta; set => eta = value; }
        public float Power1 { get => power; set => power = value; }
        public bool HasPossesion2 { get => hasPossesion; set => hasPossesion = value; }
        public BallSlice Intercept2 { get => intercept; set => intercept = value; }
        public float FreeTime2 { get => freeTime; set => freeTime = value; }
        public float Eta2 { get => eta; set => eta = value; }
        public float Power2 { get => power; set => power = value; }
    }

    // Your bot class! :D
    public class Stardust : RUBot
    {
        // We want the constructor for our Bot to extend from RUBot, but feel free to add some other initialization in here as well.
        public Stardust(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex) { }
        private bool shooting;

        public bool Shooting { get => Shooting1; set => Shooting1 = value; }
        public bool Shooting1 { get => shooting; set => shooting = value; }
        public bool Shooting2 { get => shooting; set => shooting = value; }

        // Runs every tick. Should be used to find an Action to execute
        public override void Run()
        {
            // Prints out the current action to the screen, so we know what our bot is doing
            // Renderer.Text2D(Action != null ? Action.ToString() : "", new Vec3(10, 10), 3, Color.White);

            if (Action != null)
            {
                return;
            }

            // Kickoff
            if (IsKickoff && IsClosestKickoff(Me) && Action == null)
            {
                Action = new Kickoff();
                return;
            }
            else if (IsKickoff && IsSecondClosestKickoff() && Action == null)
            {
                return;
            }

            // Rotation and positioning
            if (ShouldRotate() && Action == null)
            {
                Vec3 DesiredZone = Zone5Positioning();

                Action = new Drive(Me, DesiredZone, interruptible: false, wasteBoost: true);
                return;
            }

            if (IsSecondClosest() && AreNoBotsBack() && Action == null)
            {
                Action = new Drive(Me, OurGoal.Location, wasteBoost: true);
                return;
            }

            // Boost grabbing
            if (Me.Boost < 30 && IsSecondClosest() && !ShouldDefend() && GetClosestOpponent().Location.Dist(Ball.Location) >= 700 && Action == null)
            {
                Action = new GetBoost(Me);
                return;
            }

            // Attack
            if (ShouldAttack() && IsClosest(Me, true) && (Action is Drive || Action is GetBoost) && Action.Interruptible)
            {
                // search for the first avaliable shot using DefaultShotCheck
                Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));

                // if a shot is found, go for the shot. Otherwise pass to the closest teammate
                Action = shot ?? Action ?? null;
            }

            else if (ShouldAttack() && IsSecondClosest() && (Ball.LatestTouch.Team != Team) && Action == null)
            {
                Action = new GetBoost(Me);
                return;
            }

            if (IsBack() && ShouldDefend() && IsClosest(Me, true) && Action == null)
            {
                Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal, true));

                Action = shot ?? Action ?? null;
            }

            // Defence
            // if (ShouldDefend() && IsClosest(Me, true) && (Action is Drive || Action is GetBoost) && Action.Interruptible)
            // {
            //     Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal.Crossbar, GetClosestTeammate().Location));

            //     Action = shot ?? Action ?? null;
            // }

            else if (IsSecondClosest() && (Ball.LatestTouch.Team == Team) && Action == null)
            {
                // Vec3 location3 = new Vec3((float)(800 * -MathF.Sign(Ball.Location.x)), (float)(4900 * Field.Side(this.Team)));

                Action = new ParkAt(Me, OurGoal.Location);
                return;
            }

            else if (Action == null)
            {
                Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));

                Action = shot ?? Action ?? null;
            }
        }

        public bool CanBlock(Car car, Vec3 location) => (double)car.Location.Direction(location).Dot(Ball.Location.Direction(car.Location)) > 0.699999988079071;

        public bool IsBack() => CanDefend(Me, OurGoal.Location) || this.MyBall(out PossesionProperties _).HasPossesion;

        private PossesionProperties MyBall(out PossesionProperties theirPossesion)
        {
            float num1 = 6f;
            float num2 = 0.0f;
            BallSlice ballSlice = (BallSlice)null;
            float num3 = 6f;
            foreach (Car allLivingCar in Cars.AllLivingCars)
            {
                Car car = allLivingCar;
                BallSlice intercept = Ball.Prediction.Find((Predicate<BallSlice>)(slice => (double)Drive.GetEta(car, slice.Location) < (double)slice.Time - (double)Game.Time));
                Vec3 vec3 = intercept != null ? intercept.Location : Ball.Location;
                float eta = intercept != null ? intercept.Time - Game.Time : (6.0 + (double)car.Location.Dist(vec3) < 500.0 ? 0.0f : car.Forward.Angle(car.Location.Direction(vec3)));
                if ((double)eta < (double)num3)
                    num3 = eta;
                if ((double)eta < (double)num1 && car.Team != this.Team && (double)car.Location.Direction(this.TheirGoal.Location).Dot(vec3.Direction(car.Location)) > 0.0)
                {
                    num1 = eta;
                    ballSlice = intercept;
                    num2 = this.CalculatePower(car, intercept, eta);
                }
            }
            BallSlice intercept1 = Ball.Prediction.Find((Predicate<BallSlice>)(slice => (double)Drive.GetEta(this.Me, slice.Location) < (double)slice.Time - (double)Game.Time));
            Vec3 vec3_1 = intercept1 != null ? intercept1.Location : Ball.Location;
            float eta1 = intercept1 != null ? intercept1.Time - Game.Time : 6f;
            bool flag = (double)this.Me.Location.Direction(this.OurGoal.Location).Dot(vec3_1.Direction(this.Me.Location)) > 0.0;
            theirPossesion = new PossesionProperties()
            {
                FreeTime = num3 - num1,
                HasPossesion = (double)num3 > (double)num1 - 0.20000000298023224,
                Intercept = ballSlice,
                Eta = num1,
                Power = num2
            };
            return new PossesionProperties()
            {
                FreeTime = num3 - eta1,
                HasPossesion = (double)num3 > (double)eta1 - 0.20000000298023224 & flag,
                Intercept = intercept1,
                Eta = eta1,
                Power = this.CalculatePower(this.Me, intercept1, eta1)
            };
        }

        private float CalculatePower(Car car, BallSlice intercept, float eta)
        {
            Ball ball = intercept != null ? intercept.ToBall() : Ball.MainBall;
            return MathF.Max((ball.velocity - (ball.location - car.Location) / eta).Length(), ((ball.location - car.Location) / eta).Length());
        }

        public bool CanDefend(Car car, Vec3 location)
        {
            if (this.CanBlock(car, location))
                return true;
            double eta = (double)Drive.GetEta(car, location);
            Vec3 vec3 = Ball.Location;
            double num1 = (double)vec3.Dist(location);
            vec3 = Ball.Velocity;
            double num2 = (double)MathF.Max(vec3.Dot(Ball.Location.Direction(location)), 1500f);
            double num3 = num1 / num2;
            return eta < num3;
        }
    }
}