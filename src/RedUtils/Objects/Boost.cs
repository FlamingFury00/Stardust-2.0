using System;
using RedUtils.Math;
using RLBot.Flat;

namespace RedUtils
{
    /// <summary>A large or small boost pad. The packet timer is elapsed since pickup.</summary>
    public class Boost
    {
        public readonly int Index;
        public readonly Vec3 Location;
        public readonly bool IsLarge;
        public bool IsActive { get; private set; }
        public float TimeUntilActive { get; private set; }
        public Boost(int index)
        {
            Index = index; Location = Vec3.Zero; IsLarge = false; IsActive = true; TimeUntilActive = 0;
        }
        public Boost(int index, BoostPadT boostPad)
        {
            Index = index; Location = new Vec3(boostPad.Location); IsLarge = boostPad.IsFullBoost;
            IsActive = true; TimeUntilActive = 0;
        }
        public void Update(BoostPadStateT boost)
        {
            IsActive = boost.IsActive;
            float elapsed = float.IsFinite(boost.Timer) ? MathF.Max(0, boost.Timer) : 0;
            TimeUntilActive = IsActive ? 0 : MathF.Max(0, (IsLarge ? 10 : 4) - elapsed);
        }
    }
}
