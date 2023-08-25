using System;
using System.Threading;
using System.Drawing;
using RedUtils;
using RedUtils.Math;
using System.Numerics;
/* 
* This is the main file. It contains your bot class. Feel free to change the name!
* An instance of this class will be created for each instance of your bot in the game.
* Your bot derives from the "RedUtilsBot" class, contained in the Bot file inside the RedUtils project.
* The run function listed below runs every tick, and should contain the custom strategy code (made by you!)
* Right now though, it has a default ball chase strategy. Feel free to read up and use anything you like for your own strategy.
*/
namespace Bot
{
    // Your bot class! :D
    public class RedBot : RUBot
    {
        // We want the constructor for our Bot to extend from RUBot, but feel free to add some other initialization in here as well.
        public RedBot(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex) { }
        public bool Shooting;

        // Runs every tick. Should be used to find an Action to execute
        public override void Run()
        {
            // Prints out the current action to the screen, so we know what our bot is doing
            Renderer.Text2D(Action != null ? Action.ToString() : "", new Vec3(10, 10), 3, Color.White);

            // Kickoff
            if (IsKickoff && IsClosestKickoff(Me) && Action == null)
            {
                Action = new Kickoff();
            }
            if (IsKickoff && IsSecondClosestKickoff() && Action == null)
            {
                Action = new GetBoost(Me, interruptible: false);
            }

            // Rotation and positioning
            if (ShouldRotate() && Action == null)
            {
                Vec3 DesiredZone = Zone5Positioning();

                Action = new Drive(Me, DesiredZone);
            }

            if (IsInFrontOfBall() && AreNoBotsBack() && Action == null)
            {
                Action = new Drive(Me, OurGoal.Location, wasteBoost: true);
            }

            // Boost grabbing
            if (Me.Boost < 30 && IsSecondClosest() && Action == null)
            {
                Action = new GetBoost(Me);
            }

            else if (ShouldAttack() && IsClosest(Me, true) && Action == null)
            {
                // search for the first avaliable shot using DefaultShotCheck
                Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));

                // if a shot is found, go for the shot. Otherwise pass to the closest teammate
                Action = shot ?? Action ?? new QuickShot(Me, Teammates[0].Location);
            }

            else if (ShouldAttack() && IsSecondClosest() && Action == null)
            {
                Action = Me.Boost < 50 ? new GetBoost(Me) : new Drive(Me, OurGoal.Location);
            }

            else if (ShouldDefend() && IsClosest(Me, true) && Action == null || (Action is Drive && Action.Interruptible))
            {
                Shot shot = FindShot(DefaultShotCheck, new Target(TheirGoal));

                Action = shot ?? Action ?? new QuickShot(Me, Teammates[0].Location);
            }

            else if (ShouldDefend() && IsSecondClosest() && Action == null || (Action is Drive && Action.Interruptible))
            {
                Action = Me.Boost < 50 ? new QuickShot(Me, TheirGoal.Location) : new Drive(Me, Opponents[0].Location);
            }
        }
    }
}
