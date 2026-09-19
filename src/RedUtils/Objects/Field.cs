using RedUtils.Math;
using RLBot.Flat;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RedUtils
{
	/// <summary>Contains basic info on the field and its drivable surfaces.</summary>
	public static class Field
	{
		public const float Length = 10240;
		public const float Width = 8192;
		public const float Height = 1950;
		public const float CornerIntersection = 8064;
		public const float CornerLength = 1629;
		public const float CornerWidth = 1152;
		public static Goal[] Goals { get; private set; }
		public static Goal BlueGoal => Goals[0];
		public static Goal OrangeGoal => Goals[1];
		public static List<Boost> Boosts { get; private set; }
		public static Dictionary<string,Surface> Surfaces { get; private set; }
		public static Dictionary<string, Surface> DrivableSurfaces { get; private set; }

		static Field()
		{
			Goals = new Goal[] { new Goal(0), new Goal(1) };
			Boosts = new List<Boost>();
			Surfaces = new Dictionary<string, Surface>
			{
				{ "Ground", new Surface("Ground", new Vec3(0, 0, 0), Vec3.Up, new Vec3(Width, Length), Vec3.X, Vec3.Y) },
				{ "Ceiling", new Surface("Ceiling", new Vec3(0, 0, Height), -Vec3.Up, new Vec3(Width, Length), Vec3.X, Vec3.Y) },
				{ "Orange Backboard", new Surface("Orange Backboard", new Vec3(0, Length / 2, Height / 2 + Goal.Height / 2), new Vec3(0, -1, 0), new Vec3(Goal.Width, Height - Goal.Height), Vec3.X, Vec3.Z) },
				{ "Orange Right Backwall", new Surface("Orange Right Backwall", new Vec3(Width / 4 + Goal.Width / 2 - CornerWidth + 200, Length / 2, Height / 2), new Vec3(0, -1, 0), new Vec3(Width / 2 - Goal.Width / 2 - CornerWidth, Height), Vec3.X, Vec3.Z) },
				{ "Orange Left Backwall", new Surface("Orange Left Backwall", new Vec3(-Width / 4 - Goal.Width / 2 + CornerWidth - 200, Length / 2, Height / 2), new Vec3(0, -1, 0), new Vec3(Width / 2 - Goal.Width / 2 - CornerWidth, Height), Vec3.X, Vec3.Z) },
				{ "Blue Backboard", new Surface("Blue Backboard", new Vec3(0, -Length / 2, Height / 2 + Goal.Height / 2), new Vec3(0, 1, 0), new Vec3(Goal.Width, Height - Goal.Height), Vec3.X, Vec3.Z) },
				{ "Blue Right Backwall", new Surface("Blue Right Backwall", new Vec3(-Width / 4 - Goal.Width / 2 + CornerWidth - 200, -Length / 2, Height / 2), new Vec3(0, 1, 0), new Vec3(Width / 2 - Goal.Width / 2 - CornerWidth, Height), Vec3.X, Vec3.Z) },
				{ "Blue Left Backwall", new Surface("Blue Left Backwall", new Vec3(Width / 4 + Goal.Width / 2 - CornerWidth + 200, -Length / 2, Height / 2), new Vec3(0, 1, 0), new Vec3(Width / 2 - Goal.Width / 2 - CornerWidth, Height), Vec3.X, Vec3.Z) },
				{ "Right Orange Sidewall", new Surface("Right Orange Sidewall", new Vec3(Width / 2, 0, Height / 2), new Vec3(-1, 0, 0), new Vec3(Height, Length), Vec3.Z, Vec3.Y) },
				{ "Right Blue Sidewall", new Surface("Right Blue Sidewall", new Vec3(-Width / 2, 0, Height / 2), new Vec3(1, 0, 0), new Vec3(Height, Length), Vec3.Z, Vec3.Y) },
				{ "Right Orange Corner", new Surface("Right Orange Corner",new Vec3(Width / 2 - CornerWidth / 2, Length / 2 - CornerWidth / 2, Height / 2), new Vec3(-MathF.Sqrt(2) / 2, -MathF.Sqrt(2) / 2, 0), new Vec3(CornerLength, Height), new Vec3(-MathF.Sqrt(2) / 2, MathF.Sqrt(2) / 2, 0), Vec3.Z) },
				{ "Left Orange Corner", new Surface("Left Orange Corner", new Vec3(-Width / 2 + CornerWidth / 2, Length / 2 - CornerWidth / 2, Height / 2), new Vec3(MathF.Sqrt(2) / 2, -MathF.Sqrt(2) / 2, 0), new Vec3(CornerLength, Height), new Vec3(MathF.Sqrt(2) / 2, MathF.Sqrt(2) / 2, 0), Vec3.Z) },
				{ "Right Blue Corner", new Surface("Right Blue Corner", new Vec3(-Width / 2 + CornerWidth / 2, -Length / 2 + CornerWidth / 2, Height / 2), new Vec3(MathF.Sqrt(2) / 2, MathF.Sqrt(2) / 2, 0), new Vec3(CornerLength, Height), new Vec3(MathF.Sqrt(2) / 2, -MathF.Sqrt(2) / 2, 0), Vec3.Z) },
				{ "Left Blue Corner", new Surface("Left Blue Corner", new Vec3(Width / 2 - CornerWidth / 2, -Length / 2 + CornerWidth / 2, Height / 2), new Vec3(-MathF.Sqrt(2) / 2, MathF.Sqrt(2) / 2, 0), new Vec3(CornerLength, Height), new Vec3(-MathF.Sqrt(2) / 2, -MathF.Sqrt(2) / 2, 0), Vec3.Z) },
				{ "Orange Goal Ground", new Surface("Orange Goal Ground", new Vec3(0, Length / 2 + Goal.Depth / 2, 0), Vec3.Up, new Vec3(Goal.Width, Goal.Depth), Vec3.X, Vec3.Y) },
				{ "Blue Goal Ground", new Surface("Blue Goal Ground", new Vec3(0, -Length / 2 - Goal.Depth / 2, 0), Vec3.Up, new Vec3(Goal.Width, Goal.Depth), Vec3.X, Vec3.Y) }
			};
			DrivableSurfaces = new Dictionary<string, Surface>(Surfaces);
			DrivableSurfaces.Remove("Ceiling");
		}

		/// <summary>Rebuild shared pad data once per field-info update; never append duplicate pads.</summary>
		public static void Initialize(FieldInfoT fieldInfo)
		{
            Boosts.Clear();
            if (fieldInfo?.BoostPads == null) return;
            for (int i = 0; i < fieldInfo.BoostPads.Count; i++)
                Boosts.Add(fieldInfo.BoostPads[i] != null ? new Boost(i, fieldInfo.BoostPads[i]) : new Boost(i));
        }
        public static void Update(GamePacketT packet)
        {
            if (packet?.BoostPads == null) return;
            for (int i = 0; i < System.Math.Min(packet.BoostPads.Count, Boosts.Count); i++)
                Boosts[i].Update(packet.BoostPads[i]);
        }
        public static int Side(int team) => 2 * team - 1;
		public static bool InField(Vec3 pos, float radius)
		{
			Vec3 point = Vec3.Abs(pos);
			if (point.x > Width / 2 - radius) return false;
			if (point.y > Length / 2 + Goal.Depth - radius) return false;
			if ((point.x > Goal.Width / 2 - radius || point.z > Goal.Height - radius) && point.y > Length / 2 - radius) return false;
			if (point.x + point.y > CornerIntersection - radius) return false;
			return true;
		}
		public static bool InGoal(Vec3 pos, float radius)
		{
			Vec3 point = Vec3.Abs(pos);
			return point.x < Goal.Width / 2 + radius && point.y > Length / 2 - radius && point.y < Length / 2 + Goal.Depth + radius && point.z < Goal.Height + radius;
		}
		public static Surface NearestSurface(Vec3 pos)
		{
			Surface closestSurface = DrivableSurfaces.First().Value;
			foreach (Surface surface in DrivableSurfaces.Values)
				if (pos.Dist(closestSurface.Limit(pos)) > pos.Dist(surface.Limit(pos))) closestSurface = surface;
			return closestSurface;
		}
		public static Surface NearestSurface(Vec3 pos, Surface[] excludedSurfaces)
		{
			List<Surface> filteredSurfaces = new List<Surface>(Surfaces.Values);
			foreach (Surface surface in excludedSurfaces) filteredSurfaces.Remove(surface);
			Surface closestSurface = filteredSurfaces.First();
			foreach (Surface surface in filteredSurfaces)
				if (pos.Dist(closestSurface.Limit(pos)) > pos.Dist(surface.Limit(pos))) closestSurface = surface;
			return closestSurface;
		}
		public static Surface FindLandingSurface(Car car)
		{
			if (car.IsGrounded) return NearestSurface(car.Location, Array.Empty<Surface>());
			float groundLandingTime = Utils.Quadratic(Game.Gravity.z / 2, car.Velocity.z, car.Location.z - 15)[1];
			float ceilingLandingTime = Utils.Quadratic(Game.Gravity.z / 2, car.Velocity.z, car.Location.z + 15 - Height)[1];
			Surface landingSurface = ceilingLandingTime < 0 ? Surfaces["Ground"] : Surfaces["Ceiling"];
			float landingTime = ceilingLandingTime < 0 ? groundLandingTime : ceilingLandingTime;
			Vec3 groundLanding = car.PredictLocation(landingTime);
			if (!InField(groundLanding, 150))
			{
				foreach (Surface surface in DrivableSurfaces.Values)
				{
					if (surface.Key == "Ground") continue;
					float surfaceLandingTime = car.Location.Dist(surface.Limit(car.Location)) / car.Velocity.Dot(-surface.Normal);
					if (surfaceLandingTime > 0 && surfaceLandingTime < landingTime)
					{ landingTime = surfaceLandingTime; landingSurface = surface; }
				}
			}
			return landingSurface;
		}
		public static Vec3 LimitToNearestSurface(Vec3 pos) => NearestSurface(pos).Limit(pos);
		public static Vec3 LimitToNearestSurface(Vec3 pos, Surface[] excludedSurfaces) => NearestSurface(pos, excludedSurfaces).Limit(pos);
		public static float DistanceBetweenPoints(Vec3 pos1, Vec3 pos2)
		{
			Surface startSurface = NearestSurface(pos1);
			Vec3 startPos = startSurface.Limit(pos1);
			Surface targetSurface = NearestSurface(pos2);
			Vec3 targetPos = targetSurface.Limit(pos2);
			Surface middleSurface = NearestSurface((startPos + targetPos) / 2);
			Vec3 middlePos = middleSurface.Limit((startPos + targetPos) / 2);
			return startPos.Dist(startSurface.Limit(middlePos)) + startSurface.Limit(middlePos).Dist(middleSurface.Limit(targetPos)) + middleSurface.Limit(targetPos).Dist(targetPos);
		}
	}
	public class Surface
	{
		public string Key { get; private set; }
		public Vec3 Normal { get; private set; }
		public Vec3 Location { get; private set; }
		public Vec3 Size { get; private set; }
		public Vec3 Xdirection { get; private set; }
		public Vec3 Ydirection { get; private set; }
		public Surface(string key, Vec3 location, Vec3 normal, Vec3 size, Vec3 xDirection, Vec3 yDirection)
		{ Key = key; Location = location; Normal = normal; Size = size; Xdirection = xDirection; Ydirection = yDirection; }
		public Vec3 Limit(Vec3 pos)
		{
			Vec3 posToSurace = pos - Location;
			return Location + Xdirection * Utils.Cap(Xdirection.Dot(posToSurace), -Size.x / 2, Size.x / 2) + Ydirection * Utils.Cap(Ydirection.Dot(posToSurace), -Size.y / 2, Size.y / 2);
		}
	}
}
