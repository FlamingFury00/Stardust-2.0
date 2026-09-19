using RedUtils.Math;
using RLBot.Flat;

namespace RedUtils
{
    /// <summary>A timestamped ball state, from either the framework or a deterministic test fixture.</summary>
    public class BallSlice
    {
        public readonly Vec3 Location, Velocity, AngularVelocity;
        public readonly float Time;
        public BallSlice(float time, Vec3 location, Vec3 velocity, Vec3? angularVelocity = null)
        {
            Time = time;
            Location = location;
            Velocity = velocity;
            AngularVelocity = angularVelocity ?? Vec3.Zero;
        }
        public BallSlice(PredictionSliceT slice) : this(slice.GameSeconds, new Vec3(slice.Physics.Location),
            new Vec3(slice.Physics.Velocity), new Vec3(slice.Physics.AngularVelocity)) { }
        public Ball ToBall() => new Ball(Location, Velocity, AngularVelocity);
    }
}
