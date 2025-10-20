using RLBot.Flat;
using Vector3 = System.Numerics.Vector3;

namespace RLBot.GameState;

public readonly struct DesiredBallStateBuilder
{
    private readonly DesiredBallStateT _ballState;

    public DesiredBallStateBuilder(DesiredBallStateT ballState)
    {
        _ballState = ballState;
        ballState.Physics ??= new DesiredPhysicsT();
    }
    
    /// <summary>
    /// Set the desired location of the ball.
    /// </summary>
    public DesiredBallStateBuilder Location(Vector3 location)
    {
        _ballState.Physics.Location = new Vector3PartialT()
        {
            X = new FloatT { Val = location.X },
            Y = new FloatT { Val = location.Y },
            Z = new FloatT { Val = location.Z }
        };
        return this;
    }
    
    /// <summary>
    /// Set the desired location of the ball.
    /// </summary>
    public DesiredBallStateBuilder Location(Flat.Vector3 location)
    {
        _ballState.Physics.Location = new Vector3PartialT()
        {
            X = new FloatT { Val = location.X },
            Y = new FloatT { Val = location.Y },
            Z = new FloatT { Val = location.Z }
        };
        return this;
    }

    /// <summary>
    /// Set the X value of the desired location of the ball.
    /// </summary>
    public DesiredBallStateBuilder LocationX(float value)
    {
        _ballState.Physics.Location ??= new Vector3PartialT();
        _ballState.Physics.Location.X = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Y value of the desired location of the ball.
    /// </summary>
    public DesiredBallStateBuilder LocationY(float value)
    {
        _ballState.Physics.Location ??= new Vector3PartialT();
        _ballState.Physics.Location.Y = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Z value of the desired location of the ball.
    /// </summary>
    public DesiredBallStateBuilder LocationZ(float value)
    {
        _ballState.Physics.Location ??= new Vector3PartialT();
        _ballState.Physics.Location.Z = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the desired velocity of the ball.
    /// </summary>
    public DesiredBallStateBuilder Velocity(Vector3 velocity)
    {
        _ballState.Physics.Velocity = new Vector3PartialT()
        {
            X = new FloatT { Val = velocity.X },
            Y = new FloatT { Val = velocity.Y },
            Z = new FloatT { Val = velocity.Z }
        };
        return this;
    }

    /// <summary>
    /// Set the X value of the desired velocity of the ball.
    /// </summary>
    public DesiredBallStateBuilder VelocityX(float value)
    {
        _ballState.Physics.Velocity ??= new Vector3PartialT();
        _ballState.Physics.Velocity.X = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Y value of the desired velocity of the ball.
    /// </summary>
    public DesiredBallStateBuilder VelocityY(float value)
    {
        _ballState.Physics.Velocity ??= new Vector3PartialT();
        _ballState.Physics.Velocity.Y = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Z value of the desired velocity of the ball.
    /// </summary>
    public DesiredBallStateBuilder VelocityZ(float value)
    {
        _ballState.Physics.Velocity ??= new Vector3PartialT();
        _ballState.Physics.Velocity.Z = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the desired angular velocity of the ball.
    /// </summary>
    public DesiredBallStateBuilder AngularVelocity(Vector3 angVel)
    {
        _ballState.Physics.AngularVelocity = new Vector3PartialT()
        {
            X = new FloatT { Val = angVel.X },
            Y = new FloatT { Val = angVel.Y },
            Z = new FloatT { Val = angVel.Z }
        };
        return this;
    }

    /// <summary>
    /// Set the X value of the desired angular velocity of the ball.
    /// </summary>
    public DesiredBallStateBuilder AngularVelocityX(float value)
    {
        _ballState.Physics.AngularVelocity ??= new Vector3PartialT();
        _ballState.Physics.AngularVelocity.X = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Y value of the desired angular velocity of the ball.
    /// </summary>
    public DesiredBallStateBuilder AngularVelocityY(float value)
    {
        _ballState.Physics.AngularVelocity ??= new Vector3PartialT();
        _ballState.Physics.AngularVelocity.Y = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Z value of the desired angular velocity of the ball.
    /// </summary>
    public DesiredBallStateBuilder AngularVelocityZ(float value)
    {
        _ballState.Physics.AngularVelocity ??= new Vector3PartialT();
        _ballState.Physics.AngularVelocity.Z = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the desired rotation of the ball.
    /// </summary>
    public DesiredBallStateBuilder Rotation(Vector3 rotation)
    {
        _ballState.Physics.Rotation = new RotatorPartialT()
        {
            Pitch = new FloatT { Val = rotation.X },
            Yaw = new FloatT { Val = rotation.Y },
            Roll = new FloatT { Val = rotation.Z }
        };
        return this;
    }
    
    /// <summary>
    /// Set the desired rotation of the ball.
    /// </summary>
    public DesiredBallStateBuilder Rotation(RotatorT rotation)
    {
        _ballState.Physics.Rotation = new RotatorPartialT()
        {
            Pitch = new FloatT { Val = rotation.Pitch },
            Yaw = new FloatT { Val = rotation.Yaw },
            Roll = new FloatT { Val = rotation.Roll }
        };
        return this;
    }

    /// <summary>
    /// Set the pitch value of the desired rotation of the ball.
    /// </summary>
    public DesiredBallStateBuilder RotationPitch(float value)
    {
        _ballState.Physics.Rotation ??= new RotatorPartialT();
        _ballState.Physics.Rotation.Pitch = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the yaw value of the desired rotation of the ball.
    /// </summary>
    public DesiredBallStateBuilder RotationYaw(float value)
    {
        _ballState.Physics.Rotation ??= new RotatorPartialT();
        _ballState.Physics.Rotation.Yaw = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the roll value of the desired rotation of the ball.
    /// </summary>
    public DesiredBallStateBuilder RotationRoll(float value)
    {
        _ballState.Physics.Rotation ??= new RotatorPartialT();
        _ballState.Physics.Rotation.Roll = new FloatT { Val = value };
        return this;
    }
}
