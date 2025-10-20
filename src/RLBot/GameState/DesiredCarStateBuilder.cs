using RLBot.Flat;
using Vector3 = System.Numerics.Vector3;

namespace RLBot.GameState;

public readonly struct DesiredCarStateBuilder
{
    private readonly DesiredCarStateT _carState;

    public DesiredCarStateBuilder(DesiredCarStateT carState)
    {
        _carState = carState;
        carState.Physics ??= new DesiredPhysicsT();
    }

    /// <summary>
    /// Set the desired boost amount. 
    /// </summary>
    public DesiredCarStateBuilder Boost(float value)
    {
        _carState.BoostAmount = new FloatT() { Val = value };
        return this;
    }

    /// <summary>
    /// Set the desired location of the car.
    /// </summary>
    public DesiredCarStateBuilder Location(Vector3 location)
    {
        _carState.Physics.Location = new Vector3PartialT()
        {
            X = new FloatT { Val = location.X },
            Y = new FloatT { Val = location.Y },
            Z = new FloatT { Val = location.Z }
        };
        return this;
    }
    
    /// <summary>
    /// Set the desired location of the car.
    /// </summary>
    public DesiredCarStateBuilder Location(Flat.Vector3 location)
    {
        _carState.Physics.Location = new Vector3PartialT()
        {
            X = new FloatT { Val = location.X },
            Y = new FloatT { Val = location.Y },
            Z = new FloatT { Val = location.Z }
        };
        return this;
    }

    /// <summary>
    /// Set the X value of the desired location of the car.
    /// </summary>
    public DesiredCarStateBuilder LocationX(float value)
    {
        _carState.Physics.Location ??= new Vector3PartialT();
        _carState.Physics.Location.X = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Y value of the desired location of the car.
    /// </summary>
    public DesiredCarStateBuilder LocationY(float value)
    {
        _carState.Physics.Location ??= new Vector3PartialT();
        _carState.Physics.Location.Y = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Z value of the desired location of the car.
    /// </summary>
    public DesiredCarStateBuilder LocationZ(float value)
    {
        _carState.Physics.Location ??= new Vector3PartialT();
        _carState.Physics.Location.Z = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the desired velocity of the car.
    /// </summary>
    public DesiredCarStateBuilder Velocity(Vector3 velocity)
    {
        _carState.Physics.Velocity = new Vector3PartialT()
        {
            X = new FloatT { Val = velocity.X },
            Y = new FloatT { Val = velocity.Y },
            Z = new FloatT { Val = velocity.Z }
        };
        return this;
    }

    /// <summary>
    /// Set the X value of the desired velocity of the car.
    /// </summary>
    public DesiredCarStateBuilder VelocityX(float value)
    {
        _carState.Physics.Velocity ??= new Vector3PartialT();
        _carState.Physics.Velocity.X = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Y value of the desired velocity of the car.
    /// </summary>
    public DesiredCarStateBuilder VelocityY(float value)
    {
        _carState.Physics.Velocity ??= new Vector3PartialT();
        _carState.Physics.Velocity.Y = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Z value of the desired velocity of the car.
    /// </summary>
    public DesiredCarStateBuilder VelocityZ(float value)
    {
        _carState.Physics.Velocity ??= new Vector3PartialT();
        _carState.Physics.Velocity.Z = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the desired angular velocity of the car.
    /// </summary>
    public DesiredCarStateBuilder AngularVelocity(Vector3 angVel)
    {
        _carState.Physics.AngularVelocity = new Vector3PartialT()
        {
            X = new FloatT { Val = angVel.X },
            Y = new FloatT { Val = angVel.Y },
            Z = new FloatT { Val = angVel.Z }
        };
        return this;
    }

    /// <summary>
    /// Set the X value of the desired angular velocity of the car.
    /// </summary>
    public DesiredCarStateBuilder AngularVelocityX(float value)
    {
        _carState.Physics.AngularVelocity ??= new Vector3PartialT();
        _carState.Physics.AngularVelocity.X = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Y value of the desired angular velocity of the car.
    /// </summary>
    public DesiredCarStateBuilder AngularVelocityY(float value)
    {
        _carState.Physics.AngularVelocity ??= new Vector3PartialT();
        _carState.Physics.AngularVelocity.Y = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the Z value of the desired angular velocity of the car.
    /// </summary>
    public DesiredCarStateBuilder AngularVelocityZ(float value)
    {
        _carState.Physics.AngularVelocity ??= new Vector3PartialT();
        _carState.Physics.AngularVelocity.Z = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the desired rotation of the car.
    /// </summary>
    public DesiredCarStateBuilder Rotation(Vector3 rotation)
    {
        _carState.Physics.Rotation = new RotatorPartialT()
        {
            Pitch = new FloatT { Val = rotation.X },
            Yaw = new FloatT { Val = rotation.Y },
            Roll = new FloatT { Val = rotation.Z }
        };
        return this;
    }
    
    /// <summary>
    /// Set the desired rotation of the car.
    /// </summary>
    public DesiredCarStateBuilder Rotation(RotatorT rotation)
    {
        _carState.Physics.Rotation = new RotatorPartialT()
        {
            Pitch = new FloatT { Val = rotation.Pitch },
            Yaw = new FloatT { Val = rotation.Yaw },
            Roll = new FloatT { Val = rotation.Roll }
        };
        return this;
    }

    /// <summary>
    /// Set the pitch value of the desired rotation of the car.
    /// </summary>
    public DesiredCarStateBuilder RotationPitch(float value)
    {
        _carState.Physics.Rotation ??= new RotatorPartialT();
        _carState.Physics.Rotation.Pitch = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the yaw value of the desired rotation of the car.
    /// </summary>
    public DesiredCarStateBuilder RotationYaw(float value)
    {
        _carState.Physics.Rotation ??= new RotatorPartialT();
        _carState.Physics.Rotation.Yaw = new FloatT { Val = value };
        return this;
    }
    
    /// <summary>
    /// Set the roll value of the desired rotation of the car.
    /// </summary>
    public DesiredCarStateBuilder RotationRoll(float value)
    {
        _carState.Physics.Rotation ??= new RotatorPartialT();
        _carState.Physics.Rotation.Roll = new FloatT { Val = value };
        return this;
    }
}
