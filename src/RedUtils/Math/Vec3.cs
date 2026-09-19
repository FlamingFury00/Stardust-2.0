using RLBot.Flat;
using System;

namespace RedUtils.Math
{
    /// <summary>Represents a vector in three dimensions.</summary>
    public struct Vec3
    {
        public float this[int index]
        {
            get => index switch { 0 => x, 1 => y, 2 => z, _ => float.NaN };
            set
            {
                switch (index) { case 0: x = value; break; case 1: y = value; break; case 2: z = value; break; }
            }
        }
        public float x, y, z;
        public static Vec3 Zero => new(0, 0, 0);
        public static Vec3 Up => new(0, 0, 1);
        public static Vec3 Z => new(0, 0, 1);
        public static Vec3 Y => new(0, 1, 0);
        public static Vec3 X => new(1, 0, 0);

        public Vec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vec3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public Vec3(float[] values)
        {
            x = values.Length > 0 ? values[0] : 0;
            y = values.Length > 1 ? values[1] : 0;
            z = values.Length > 2 ? values[2] : 0;
        }
        public Vec3(Vec3 vector) { x = vector.x; y = vector.y; z = vector.z; }
        public Vec3(Vector3T vector) { x = vector.X; y = vector.Y; z = vector.Z; }
        public Vec3(RotatorT rotator) { x = rotator.Pitch; y = rotator.Yaw; z = rotator.Roll; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3 operator -(Vec3 v) => new(-v.x, -v.y, -v.z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3 operator *(Vec3 a, Vec3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vec3 operator *(Vec3 v, float a) => new(v.x * a, v.y * a, v.z * a);
        public static Vec3 operator *(float a, Vec3 v) => new(a * v.x, a * v.y, a * v.z);
        public static Vec3 operator /(Vec3 a, Vec3 b) => new(a.x / b.x, a.y / b.y, a.z / b.z);
        public static Vec3 operator /(Vec3 v, float a) => new(v.x / a, v.y / a, v.z / a);
        public static Vec3 Abs(Vec3 v) => new(MathF.Abs(v.x), MathF.Abs(v.y), MathF.Abs(v.z));
        public float Dot(Vec3 v) => x * v.x + y * v.y + z * v.z;
        public Vec3 Dot(Mat3x3 m) => m.Forward * x + m.Right * y + m.Up * z;
        public Vec3 Cross() => new(y, -x, 0);
        public Vec3 Cross(Vec3 v) => new(y * v.z - z * v.y, z * v.x - x * v.z, x * v.y - y * v.x);
        public float Length() => MathF.Sqrt(x * x + y * y + z * z);
        public Vec3 Normalize()
        {
            float length = Length();
            return length > 0 ? this / length : this;
        }
        public Vec3 FlatNorm() => Flatten().Normalize();
        public Vec3 FlatNorm(Vec3 up) => Flatten(up).Normalize();
        public Vec3 Flatten() => new(x, y, 0);
        public Vec3 Flatten(Vec3 up)
        {
            up = up.Normalize();
            return this - up * Dot(up);
        }
        public Vec3 Direction(Vec3 v) => (v - this).Normalize();
        public Vec3 FlatDirection(Vec3 v) => (v - this).FlatNorm();
        public Vec3 FlatDirection(Vec3 v, Vec3 up) => (v - this).FlatNorm(up);
        public float Dist(Vec3 v) => (v - this).Length();
        public float FlatDist(Vec3 v) => (v - this).Flatten().Length();
        public float FlatDist(Vec3 v, Vec3 up) => (v - this).Flatten(up).Length();
        public float FlatLen() => Flatten().Length();
        public float FlatLen(Vec3 up) => Flatten(up).Length();
        public float Angle(Vec3 v) => MathF.Acos(System.Math.Clamp(MathF.Round(Normalize().Dot(v.Normalize()), 4), -1, 1));

        /// <summary>Angle between the projections. Normalize AFTER projection; height must not create a yaw error.</summary>
        public float FlatAngle(Vec3 v) => Flatten().Angle(v.Flatten());
        public float FlatAngle(Vec3 v, Vec3 up) => Flatten(up).Angle(v.Flatten(up));
        public Vec3 Rescale(float length) => Normalize() * length;
        public Vec3 Cap(float min, float max) => Rescale(Utils.Cap(Length(), min, max));
        public Vec3 Clamp(Vec3 start, Vec3 end)
        {
            Vec3 v = Normalize();
            bool right = v.Dot(end.Cross(-Up)) < 0;
            bool left = v.Dot(start.Cross(-Up)) > 0;
            bool between = end.Dot(start.Cross(-Up)) > 0 ? left && right : left || right;
            if (between) return this;
            if (start.Dot(v) < end.Dot(v)) return end.Flatten().Normalize() * Flatten().Length() + Up * z;
            return start.Flatten().Normalize() * Flatten().Length() + Up * z;
        }
        public Vec3 Clamp(Vec3 start, Vec3 end, Vec3 up)
        {
            Vec3 v = Normalize();
            bool right = v.Dot(end.Cross(-up)) < 0;
            bool left = v.Dot(start.Cross(-up)) > 0;
            bool between = end.Dot(start.Cross(-up)) > 0 ? left && right : left || right;
            if (between) return this;
            if (start.Dot(v) < end.Dot(v)) return end.FlatNorm(up) * Flatten(up).Length() + up * Dot(up);
            return start.FlatNorm(up) * Flatten(up).Length() + up * Dot(up);
        }
        public Vec3 Rotate(float angle) => Flatten(Up) * MathF.Cos(angle) +
            Flatten(Up).Cross(Up).Rescale(Flatten(Up).Length()) * MathF.Sin(angle) + Up * Dot(Up);
        public Vec3 Rotate(float angle, Vec3 rotationAxis) => Flatten(rotationAxis) * MathF.Cos(angle) +
            Flatten(rotationAxis).Cross(rotationAxis).Rescale(Flatten(rotationAxis).Length()) * MathF.Sin(angle) + rotationAxis * Dot(rotationAxis);
        public override string ToString() => $"({x}, {y}, {z})";
    }
}
