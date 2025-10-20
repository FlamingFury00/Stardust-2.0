using RLBot.Flat;

namespace RLBot.GameState;

public readonly struct DesiredMatchInfoBuilder(DesiredMatchInfoT matchInfo)
{
    /// <summary>
    /// Set the world gravity z.  
    /// </summary>
    public DesiredMatchInfoBuilder GravityZ(float value)
    {
        matchInfo.WorldGravityZ = new FloatT { Val = value };
        return this;
    }

    /// <summary>
    /// Set the game speed modifier.
    /// </summary>
    public DesiredMatchInfoBuilder GameSpeed(float value)
    {
        matchInfo.GameSpeed = new FloatT { Val = value };
        return this;
    }
}
