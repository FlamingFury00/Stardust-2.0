using RedUtils;
using RedUtils.Math;
using RedUtils.Objects;
using System;

namespace Bot
{
    public class Shadow : IAction
    {
        private readonly Drive _driveAction;

        public bool Finished { get; internal set; }

        public bool Interruptible { get; internal set; }

        public Vec3 TargetLocation => _driveAction.Target;

        public Shadow(Car car)
        {
            Finished = false;
            Interruptible = true;
            Vec3 location = Field.Goals[car.Team].Location;
            _driveAction = new Drive(car, CalculateTargetPos(car, location), 2300f, true, false)
            {
                Backwards = false
            };
        }

        public void Run(RUBot bot)
        {
            _driveAction.Target = CalculateTargetPos(bot.Me, bot.OurGoal.Location);
            Interruptible = _driveAction.Interruptible;
            _driveAction.WasteBoost = !(bot as Stardust).IsBack();
            _driveAction.Run(bot);
        }

        private Vec3 CalculateTargetPos(Car car, Vec3 ourGoal)
        {
            int index = (int)MathF.Min((float)((double)car.Location.Dist(Ball.Location) / 1410.0 * 60.0), 359f);
            Vec3 vec3_1 = Ball.Prediction.Length > index ? Ball.Prediction[index].Location : Ball.Location;
            if (_driveAction != null)
            {
                Vec3 vec3_2 = car.LocationAfterDodge();
                _driveAction.AllowDodges = (double)car.Location.Direction(ourGoal).Dot(car.Location.Direction(vec3_1)) > 0.0 || (double)vec3_2.Dist(ourGoal) > 1500.0;
                _driveAction.WasteBoost = (double)car.Location.Direction(ourGoal).Dot(car.Location.Direction(vec3_1)) > 0.0;
            }
            return Field.LimitToNearestSurface((vec3_1 + (car.Location.Direction(ourGoal) * car.Location.Dist(vec3_1) * 0.8f)).Flatten());
        }
    }
}