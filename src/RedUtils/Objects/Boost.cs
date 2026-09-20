using RedUtils.Math;
using RLBot.Flat;

namespace RedUtils
{
	/// <summary>A large or small boost pad</summary>
	public class Boost
	{
		public readonly int Index;
		public readonly Vec3 Location;
		public readonly bool IsLarge;

		public bool IsActive { get; private set; }
		/// <summary>How much time until it activates. 0 if it is already activated</summary>
		public float TimeUntilActive { get; private set; }

		/// <summary>Initializes a new empty boost pad object</summary>
		public Boost(int index)
		{
			Index = index;
			Location = Vec3.Zero;
			IsLarge = false;
			IsActive = true;
			TimeUntilActive = 0;
		}

        /// <summary>Initializes a new boost pad object with data from the packet</summary>
        public Boost(int index, BoostPadT boostPad)
        {
            Index = index;
            Location = new Vec3(boostPad.Location);
            IsLarge = boostPad.IsFullBoost;
            IsActive = true;
            TimeUntilActive = 0;
        }

        /// <summary>Updates the boost pad with info from the packet</summary>
        public void Update(BoostPadStateT boost)
        {
            IsActive = boost.IsActive;
            // RLBot v5 reports seconds elapsed since pickup, not seconds remaining.
            float respawnTime = IsLarge ? 10f : 4f;
            TimeUntilActive = IsActive ? 0 : System.MathF.Max(0, respawnTime - boost.Timer);
        }
    }
}
