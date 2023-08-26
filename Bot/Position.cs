using RedUtils;
using RedUtils.Math;

public class ParkAt : IAction
{
    public Arrive arriveAction;
    private bool directionSet;

    public bool Finished { get; internal set; }

    public bool Interruptible { get; internal set; }

    public ParkAt(Car car, Vec3 location)
    {
        this.Finished = false;
        this.Interruptible = true;
        this.arriveAction = new Arrive(car, location, location.FlatDirection(Ball.Location));
    }

    public ParkAt(Car car, Vec3 location, Vec3 direction)
    {
        this.Finished = false;
        this.Interruptible = true;
        this.directionSet = true;
        this.arriveAction = new Arrive(car, location, direction);
    }

    public void Run(RUBot bot)
    {
        this.Interruptible = this.arriveAction.Interruptible;
        if (!this.directionSet)
            this.arriveAction.Direction = this.arriveAction.Target.FlatDirection(Ball.Location);
        this.arriveAction.Run(bot);
        if ((double)this.arriveAction.Target.Dist(bot.Me.Location) >= 400.0)
            return;
        double num = (double)bot.Throttle(0.0f);
        bot.AimAt(bot.Me.Location + this.arriveAction.Direction);
    }
}