using System;
using System.Drawing;
using RedUtils.Math;

namespace RedUtils
{
    /// <summary>Legacy surface-aware driving and travel mechanics, with corrected alignment and acceleration estimates.</summary>
    public class Drive : IAction
    {
        public bool Finished { get; private set; }
        public bool Interruptible { get; private set; }
        public Vec3 Target;
        public float TargetSpeed;
        public bool Backwards;
        public bool AllowDodges;
        public bool WasteBoost;
        public IAction Action;
        private float timeOnGround;
        public float TimeRemaining { get; private set; }

        public Drive(Car car, Vec3 target, float targetSpeed = Car.MaxSpeed, bool allowDodges = true, bool wasteBoost = false)
        {
            Interruptible = true;
            Target = target;
            TargetSpeed = targetSpeed;
            float forwardsEta = GetEta(car, target, false, false);
            float backwardsEta = GetEta(car, target, true, false);
            Backwards = backwardsEta + 0.5f < forwardsEta && car.Forward.Dot(car.Velocity) < 500 &&
                car.Forward.FlatAngle(car.Location.Direction(target), car.Up) > MathF.PI * 0.6f;
            AllowDodges = allowDodges;
            WasteBoost = wasteBoost;
        }

        public void Run(RUBot bot)
        {
            TimeRemaining = Distance(bot.Me) / MathF.Max(MathF.Abs(TargetSpeed), 1);
            Surface targetSurface = Field.NearestSurface(Target);
            if (Action == null)
            {
                if (bot.Me.IsGrounded) timeOnGround += bot.DeltaTime;
                Surface nextSurface = targetSurface;
                Surface mySurface = Field.NearestSurface(bot.Me.Location);
                float carSpeed = bot.Me.Velocity.Length();
                float forwardSpeed = bot.Me.Velocity.Dot(bot.Me.Forward);
                Vec3 finalTarget = Field.LimitToNearestSurface(Target);
                if (mySurface.Normal.Dot(targetSurface.Normal) < 0.95f)
                {
                    nextSurface = FindNextSurface(Field.LimitToNearestSurface(bot.Me.Location), finalTarget);
                    finalTarget = nextSurface.Limit(finalTarget);
                    Vec3 closestSurfacePoint = mySurface.Limit(finalTarget);
                    finalTarget = closestSurfacePoint - nextSurface.Normal.FlatNorm(mySurface.Normal) * MathF.Max(closestSurfacePoint.Dist(finalTarget) - 75, 0);
                }
                if (mySurface.Key != targetSurface.Key) finalTarget = FindTargetAroundCorner(bot, finalTarget, nextSurface);
                float turnRadius = TurnRadius(MathF.Abs(forwardSpeed));
                Vec3 nearestTurnCenter = mySurface.Limit(bot.Me.Location) + bot.Me.Right.FlatNorm(mySurface.Normal) * MathF.Sign(bot.Me.Right.Dot(finalTarget - bot.Me.Location)) * turnRadius;
                float landingTime = bot.Me.PredictLandingTime();
                if (Field.DistanceBetweenPoints(nearestTurnCenter, Target) > turnRadius - 40 && bot.Me.IsGrounded)
                    bot.Throttle(TargetSpeed, Backwards);
                else if (!bot.Me.IsGrounded)
                {
                    TimeRemaining = float.IsNaN(TimeRemaining) ? 0.01f : TimeRemaining;
                    bot.Throttle(Distance(bot.Me) / MathF.Max(TimeRemaining - landingTime, 0.01f));
                }
                else bot.Throttle(MathF.Max(SpeedFromTurnRadius(TurnRadius(bot.Me, Target)), 400), Backwards);

                float angleToTarget;
                if (bot.Me.IsGrounded || bot.Me.Velocity.FlatLen() < 500)
                {
                    float[] angles = bot.AimAt(finalTarget, backwards: Backwards);
                    // AimAt returns [pitch, yaw, roll]. Ground steering and thrust need absolute yaw.
                    angleToTarget = bot.Me.IsGrounded ? MathF.Abs(angles[1]) : bot.Me.Forward.Angle(finalTarget - bot.Me.Location);
                }
                else
                {
                    Vec3 landingNormal = Field.FindLandingSurface(bot.Me).Normal;
                    Vec3 targetDirection = Utils.Lerp(Utils.Cap(landingTime * 1.5f - 0.6f, 0, 0.75f), bot.Me.Velocity.FlatNorm(landingNormal), -Vec3.Up);
                    bot.AimAt(bot.Me.Location + targetDirection, landingNormal);
                    angleToTarget = bot.Me.Forward.Angle(targetDirection);
                }
                bot.Controller.Boost = bot.Controller.Boost && (angleToTarget < 0.35f || (angleToTarget < 0.85f && !bot.Me.IsGrounded)) &&
                    !Backwards && (WasteBoost || (TargetSpeed > 1800 && forwardSpeed > 1200));
                bot.Controller.Handbrake = (angleToTarget > 2.2f ||
                    (Field.DistanceBetweenPoints(nearestTurnCenter, Target) < turnRadius - 40 && SpeedFromTurnRadius(TurnRadius(bot.Me, Target)) < 350)) &&
                    mySurface.Normal.Dot(Vec3.Up) > 0.9f && bot.Me.Velocity.Normalize().Dot(bot.Me.Forward) > 0.9f;
                bot.Renderer.Line3D(finalTarget, finalTarget + Field.NearestSurface(finalTarget).Normal * 200, Color.LimeGreen);
                Vec3 predictedLocation = bot.Me.LocationAfterDodge();
                float timeLeft = bot.Me.Location.FlatDist(finalTarget) / MathF.Max(carSpeed + 500, 1410);
                float speedFlipTimeLeft = bot.Me.Location.FlatDist(finalTarget) / MathF.Max(carSpeed + 500 + MathF.Min(bot.Me.Boost, 40) * Car.BoostAccel / 2, 1410);
                if (AllowDodges && Field.InField(predictedLocation, 50) && carSpeed < 2000 && bot.Me.Location.z < 600 &&
                    Game.Gravity.z < -500 && MathF.Abs(bot.Me.Velocity.Dot(bot.Me.Up)) < 100)
                {
                    if (forwardSpeed > 0)
                    {
                        if (TargetSpeed > 100 + forwardSpeed)
                        {
                            if (bot.Me.Location.z < 200 && bot.Me.IsGrounded && carSpeed > 850 &&
                                bot.Me.Forward.FlatAngle(bot.Me.Location.Direction(finalTarget)) < 0.12f && timeOnGround > 0.15f)
                            {
                                Dodge dodge = new Dodge(bot.Me.Location.FlatDirection(Target));
                                if (speedFlipTimeLeft > SpeedFlip.Duration && Field.InField(predictedLocation, 500))
                                    Action = new SpeedFlip(bot.Me.Location.FlatDirection(Target));
                                else if (timeLeft > dodge.Duration * 0.9f) Action = dodge;
                            }
                            else if (bot.Me.Location.z > 100 && !bot.Me.HasDoubleJumped && (!bot.Me.IsGrounded || bot.Me.Velocity.Dot(Vec3.Up) < 200))
                            {
                                Wavedash wavedash = new Wavedash(bot.Me.Location.FlatDirection(Target));
                                if (timeLeft > wavedash.Duration) Action = wavedash;
                            }
                        }
                    }
                    else if (bot.Me.Location.z < 200 && bot.Me.IsGrounded && carSpeed > 800 && Backwards &&
                        (-bot.Me.Forward).FlatAngle(bot.Me.Location.Direction(finalTarget)) < 0.1f && timeOnGround > 0.2f)
                    {
                        if (timeLeft > HalfFlip.Duration) Action = new HalfFlip();
                    }
                }
            }
            else if (Action.Finished)
            { Action = null; Backwards = false; timeOnGround = 0; }
            else
            {
                Action.Run(bot);
                if (Action is SpeedFlip) bot.Throttle(TargetSpeed + 700, Backwards);
            }
            bot.Renderer.Line3D(Field.LimitToNearestSurface(Target), Field.LimitToNearestSurface(Target) + targetSurface.Normal * 200, Color.LimeGreen);
            Interruptible = Action == null || Action.Interruptible;
            if (Field.LimitToNearestSurface(bot.Me.Location).Dist(Field.LimitToNearestSurface(Target)) < 100) Finished = true;
        }

        public float Distance(Car car) => GetDistance(car, Target, Backwards);
        public float Eta(Car car) => GetEta(car, Target, Backwards, AllowDodges);
        private static Surface FindNextSurface(Vec3 start, Vec3 target)
        {
            Vec3 middle = Field.LimitToNearestSurface((start + target) / 2);
            for (float f = 0; f < 2; f += 0.25f)
            {
                Vec3 nextPos = Field.LimitToNearestSurface(start + (middle - start) * Utils.Cap(f, 0, 1) + (target - middle) * Utils.Cap(f - 1, 0, 1));
                if (Field.NearestSurface(nextPos).Normal.Dot(Field.NearestSurface(start).Normal) < 0.95f) return Field.NearestSurface(nextPos);
            }
            return Field.NearestSurface(target);
        }
        private static Vec3 FindTargetAroundCorner(RUBot bot, Vec3 finalTarget, Surface nextSurface)
        {
            Surface mySurface = Field.NearestSurface(bot.Me.Location);
            if (mySurface.Key == "Ground")
            {
                Goal goal = Field.Side(bot.Team) == MathF.Sign(finalTarget.y) ? bot.OurGoal : bot.TheirGoal;
                Vec3 enterLeftDirection = bot.Me.Location.Direction(goal.LeftPost - new Vec3(MathF.Sign(goal.LeftPost.x) * 100, MathF.Sign(goal.LeftPost.y) * 50));
                Vec3 enterRightDirection = bot.Me.Location.Direction(goal.RightPost - new Vec3(MathF.Sign(goal.RightPost.x) * 100, MathF.Sign(goal.RightPost.y) * 50));
                Vec3 exitLeftDirection = bot.Me.Location.Direction(goal.LeftPost + new Vec3(MathF.Sign(goal.LeftPost.x) * 60, -MathF.Sign(goal.LeftPost.y) * 50));
                Vec3 exitRightDirection = bot.Me.Location.Direction(goal.RightPost + new Vec3(MathF.Sign(goal.RightPost.x) * 60, -MathF.Sign(goal.RightPost.y) * 50));
                Vec3 targetDirection = nextSurface.Key.Contains("Goal Ground") ?
                    bot.Me.Location.Direction(finalTarget).Clamp(enterLeftDirection, enterRightDirection, mySurface.Normal) :
                    bot.Me.Location.Direction(finalTarget).Clamp(exitRightDirection, exitLeftDirection, mySurface.Normal);
                return bot.Me.Location + targetDirection.Rescale(bot.Me.Location.Dist(finalTarget));
            }
            else if (mySurface.Key.Contains("Goal Ground"))
            {
                Goal goal = Field.Side(bot.Team) == MathF.Sign(bot.Me.Location.y) ? bot.OurGoal : bot.TheirGoal;
                Vec3 leftDirection = bot.Me.Location.Direction(goal.LeftPost - new Vec3(MathF.Sign(goal.LeftPost.x) * 100, MathF.Sign(goal.LeftPost.y) * 50));
                Vec3 rightDirection = bot.Me.Location.Direction(goal.RightPost - new Vec3(MathF.Sign(goal.RightPost.x) * 100, MathF.Sign(goal.RightPost.y) * 50));
                Vec3 targetDirection = bot.Me.Location.Direction(finalTarget).Clamp(rightDirection, leftDirection, mySurface.Normal);
                return bot.Me.Location + targetDirection.Rescale(bot.Me.Location.Dist(finalTarget));
            }
            else if (mySurface.Key.Contains("Backboard") || mySurface.Key.Contains("Backwall"))
            {
                Goal goal = Field.Side(bot.Team) == MathF.Sign(bot.Me.Location.y) ? bot.OurGoal : bot.TheirGoal;
                Vec3 leftDirection, rightDirection;
                if (mySurface.Key.Contains("Left Backwall"))
                {
                    leftDirection = bot.Me.Location.Direction(goal.TopRightCorner + new Vec3(MathF.Sign(goal.TopRightCorner.x) * 50, 0, 50));
                    rightDirection = bot.Me.Location.Direction(goal.BottomRightCorner + new Vec3(MathF.Sign(goal.BottomRightCorner.x) * 50, 0, -50));
                }
                else if (mySurface.Key.Contains("Right Backwall"))
                {
                    leftDirection = bot.Me.Location.Direction(goal.BottomLeftCorner + new Vec3(MathF.Sign(goal.BottomLeftCorner.x) * 50, 0, -50));
                    rightDirection = bot.Me.Location.Direction(goal.TopLeftCorner + new Vec3(MathF.Sign(goal.TopLeftCorner.x) * 50, 0, 50));
                }
                else
                {
                    leftDirection = bot.Me.Location.Direction(goal.TopLeftCorner + new Vec3(MathF.Sign(goal.TopLeftCorner.x) * 50, 0, 50));
                    rightDirection = bot.Me.Location.Direction(goal.TopRightCorner + new Vec3(MathF.Sign(goal.TopRightCorner.x) * 50, 0, 50));
                }
                Vec3 targetDirection = bot.Me.Location.Direction(finalTarget).Clamp(leftDirection, rightDirection, mySurface.Normal);
                return bot.Me.Location + targetDirection.Rescale(bot.Me.Location.Dist(finalTarget));
            }
            return finalTarget;
        }

        private static bool PreferReverse(Car car, Vec3 target) =>
            GetEta(car, target, true, false) + 0.5f < GetEta(car, target, false, false) &&
            car.Forward.Dot(car.Velocity) < 500 && car.Forward.FlatAngle(car.Location.Direction(target), car.Up) > MathF.PI * 0.6f;
        public static float GetDistance(Car car, Vec3 target) => GetDistance(car, target, PreferReverse(car, target));
        public static float GetDistance(Car car, Vec3 target, bool backwards) => GetDistance(car, target, backwards, out _, out _);
        public static float GetDistance(Car car, Vec3 target, bool backwards, out float angle, out float radius)
        {
            target = Field.LimitToNearestSurface(target);
            Vec3 carPos = car.PredictLandingPosition();
            Surface carSurface = Field.FindLandingSurface(car);
            Vec3 surfaceNormal = carSurface.Normal;
            Vec3 carForward = car.IsGrounded ? car.Forward : (car.Velocity.FlatLen() > 500 ? car.Velocity.FlatNorm(surfaceNormal) : car.Location.FlatDirection(target, surfaceNormal));
            Vec3 carRight = carForward.Cross(car.IsGrounded ? -car.Up : -surfaceNormal).Normalize();
            float currentSpeed = car.Velocity.Dot(carForward);
            angle = (backwards ? -carForward : carForward).FlatAngle(target - carPos, surfaceNormal);
            float turnSpeed = backwards ? SpeedAfterTurn(-currentSpeed, angle, 0.4f) : SpeedAfterTurn(currentSpeed, angle, 0.5f);
            radius = TurnRadius(turnSpeed);
            if (carSurface.Limit(carPos).Dist(target) < 0.01f) { angle = 0; return 0; }
            // Both endpoints belong on the driving surface, not one endpoint at the chassis center.
            if (angle < 0.0001f && Field.NearestSurface(target).Key == carSurface.Key)
            { angle = 0; return carSurface.Limit(carPos).Dist(target); }
            Vec3 nearestTurnCenter = carPos + carRight * MathF.Sign(carRight.Dot(target - carPos)) * radius;
            Vec3 limitedTurnCenter = carSurface.Limit(nearestTurnCenter);
            if (nearestTurnCenter.Dist(limitedTurnCenter) > 1)
            {
                Vec3 normal = limitedTurnCenter.Direction(Field.LimitToNearestSurface(nearestTurnCenter + carSurface.Normal * 500));
                nearestTurnCenter = limitedTurnCenter + normal * nearestTurnCenter.Dist(limitedTurnCenter);
            }
            float distance = Field.DistanceBetweenPoints(nearestTurnCenter, target);
            if (distance < radius)
            {
                radius = TurnRadius(car, target);
                nearestTurnCenter = carPos + carRight * MathF.Sign(carRight.Dot(target - carPos)) * radius;
                distance = Field.DistanceBetweenPoints(nearestTurnCenter, target);
            }
            angle = MathF.Abs((carPos - nearestTurnCenter).FlatAngle(target - nearestTurnCenter, surfaceNormal) -
                ((target - carPos).Dot(backwards ? -carForward : carForward) < 0 ? 2 * MathF.PI : 0));
            angle -= MathF.Acos(Utils.Cap(radius / MathF.Max(distance, 0.001f), 0, 1));
            angle = Utils.Cap(angle, 0, 2 * MathF.PI);
            return MathF.Sqrt(MathF.Max(MathF.Pow(distance, 2) - MathF.Pow(radius, 2), 0)) + radius * angle;
        }
        public static float GetEta(Car car, Vec3 target) => GetEta(car, target, PreferReverse(car, target), true);
        public static float GetEta(Car car, Vec3 target, bool allowDodges) => GetEta(car, target, PreferReverse(car, target), allowDodges);
        public static float GetEta(Car car, Vec3 target, bool backwards, bool allowDodges)
        {
            float distance = GetDistance(car, target, backwards, out float angle, out float radius);
            if (!float.IsFinite(distance) || !float.IsFinite(angle)) return float.PositiveInfinity;
            float turnDistance = angle * radius;
            distance = MathF.Max(0, distance - turnDistance);
            Vec3 surfaceNormal = car.IsGrounded ? Field.NearestSurface(car.Location).Normal : Field.FindLandingSurface(car).Normal;
            Vec3 carForward = car.IsGrounded ? car.Forward : (car.Velocity.FlatLen() > 500 ? car.Velocity.FlatNorm(surfaceNormal) : car.Location.FlatDirection(target, surfaceNormal));
            float currentSpeed = carForward.Dot(car.Velocity) * (backwards ? -1 : 1);
            float landingTime = car.PredictLandingTime();
            if (!float.IsFinite(landingTime) || landingTime < 0) return float.PositiveInfinity;
            float exitSpeed = angle < 0.001f ? currentSpeed : MathF.Min(SpeedFromTurnRadius(radius), SpeedAfterTurn(currentSpeed, angle, backwards ? 0.8f : 1));
            return landingTime + turnDistance / MathF.Max(SpeedFromTurnRadius(radius), 400) + DrivePhysics.TravelTime(distance, exitSpeed, car.Boost, backwards);
        }
        public static float TurnRadius(Car car, Vec3 target)
        {
            float distance = Field.DistanceBetweenPoints(car.Location, target);
            float sine = (car.Right * MathF.Sign(car.Right.Dot(target - car.Location))).Dot(car.Location.FlatDirection(target, car.Up));
            return MathF.Max(1, distance / 2) / MathF.Max(0.0001f, sine);
        }
        public static float TurnRadius(float speed)
        {
            speed = Utils.Cap(speed, 0.01f, Car.MaxSpeed);
            if (speed <= 500) return Utils.Lerp(speed / 500, 145, 251);
            if (speed <= 1000) return Utils.Lerp((speed - 500) / 500, 251, 425);
            if (speed <= 1500) return Utils.Lerp((speed - 1000) / 500, 425, 727);
            if (speed <= 1750) return Utils.Lerp((speed - 1500) / 250, 727, 909);
            return Utils.Lerp((speed - 1750) / 550, 909, 1136);
        }
        public static float SpeedFromTurnRadius(float radius)
        {
            radius = Utils.Cap(radius, 145, 1136);
            if (radius <= 251) return Utils.Lerp((radius - 145) / 106, 0, 500);
            if (radius <= 425) return Utils.Lerp((radius - 251) / 174, 500, 1000);
            if (radius <= 727) return Utils.Lerp((radius - 425) / 302, 1000, 1500);
            if (radius <= 909) return Utils.Lerp((radius - 727) / 182, 1500, 1750);
            return Utils.Lerp((radius - 909) / 227, 1750, Car.MaxSpeed);
        }
        public static float SpeedAfterTurn(float currentSpeed, float angle, float modifier = 1) =>
            Utils.Cap((1234 * (MathF.Exp(angle * 0.49f * modifier) * angle * 0.49f * modifier * (currentSpeed > 1234 ? 0.2f : 1)) + currentSpeed) /
                ((MathF.Exp(angle * 0.49f * modifier) * angle * 0.49f * modifier * (currentSpeed > 1234 ? 0.2f : 1)) + 1), 0, Car.MaxSpeed);
    }
}
