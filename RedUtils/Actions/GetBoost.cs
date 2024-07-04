using RedUtils.Objects;

namespace RedUtils.Actions
{
    public class GetBoost : IAction
    {
        public int BoostIndex;
        public Drive DriveAction;
        public Boost ChosenBoost;
        public float Eta;
        private readonly bool _initiallyInterruptible = true;

        public bool Finished { get; set; }

        public bool Interruptible { get; set; }

        public GetBoost(Car car)
        {
            Finished = false;
            Interruptible = true;
            float num = 99f;
            foreach (Boost boost in Field.Boosts)
            {
                if (boost.IsLarge)
                {
                    float eta = Drive.GetEta(car, boost.Location);
                    if ((double)eta < (double)num && (boost.IsActive || (double)boost.TimeUntilActive < (double)eta))
                    {
                        num = eta;
                        BoostIndex = boost.Index;
                    }
                }
            }
            ChosenBoost = Field.Boosts[BoostIndex];
            DriveAction = new Drive(car, ChosenBoost.Location, 2300f, true, true);
        }

        public GetBoost(Car car, bool interruptible)
        {
            Finished = false;
            Interruptible = interruptible;
            _initiallyInterruptible = interruptible;
            float num = 99f;
            foreach (Boost boost in Field.Boosts)
            {
                if (boost.IsLarge)
                {
                    float eta = Drive.GetEta(car, boost.Location);
                    if ((double)eta < (double)num && (boost.IsActive || (double)boost.TimeUntilActive < (double)eta))
                    {
                        num = eta;
                        BoostIndex = boost.Index;
                    }
                }
            }
            ChosenBoost = Field.Boosts[BoostIndex];
            DriveAction = new Drive(car, ChosenBoost.Location, 2300f, true, true);
        }

        public GetBoost(Car car, int boostIndex)
        {
            Finished = false;
            Interruptible = true;
            BoostIndex = boostIndex;
            ChosenBoost = Field.Boosts[BoostIndex];
            DriveAction = new Drive(car, ChosenBoost.Location, 2300f, true, true);
        }

        public GetBoost(Car car, int boostIndex, bool interruptible)
        {
            Finished = false;
            Interruptible = interruptible;
            _initiallyInterruptible = interruptible;
            BoostIndex = boostIndex;
            ChosenBoost = Field.Boosts[BoostIndex];
            DriveAction = new Drive(car, ChosenBoost.Location, 2300f, true, true);
        }

        public void Run(RUBot bot)
        {
            DriveAction.Run(bot);
            ChosenBoost = Field.Boosts[BoostIndex];
            Eta = Drive.GetEta(bot.Me, ChosenBoost.Location);
            Interruptible = _initiallyInterruptible && DriveAction.Interruptible;
            Finished = (!ChosenBoost.IsActive && ((double)ChosenBoost.TimeUntilActive > (double)Drive.GetEta(bot.Me, ChosenBoost.Location) || DriveAction.Finished)) || bot.Me.Boost > 90;
        }
    }
}
