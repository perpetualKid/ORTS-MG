using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

namespace Orts.Common.Xna
{
    public static class VectorExtension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Transform(in Vector3 position, in Matrix matrix, out Vector3 result)
        {
            result.X = (position.X * matrix.M11) + (position.Y * matrix.M21) + (position.Z * matrix.M31) + matrix.M41;
            result.Y = (position.X * matrix.M12) + (position.Y * matrix.M22) + (position.Z * matrix.M32) + matrix.M42;
            result.Z = (position.X * matrix.M13) + (position.Y * matrix.M23) + (position.Z * matrix.M33) + matrix.M43;
        }

        public static float LineSegmentDistanceSquare(this in Vector3 start, in Vector3 end1, in Vector3 end2)
        {
            float dx = end2.X - end1.X;
            float dy = end2.Y - end1.Y;
            float dz = end2.Z - end1.Z;
            float d = dx * dx + dy * dy + dz * dz;
            float n = dx * (start.X - end1.X) + dy * (start.Y - end1.Y) + dz * (start.Z - end1.Z);
            if (d == 0 || n < 0)
            {
                dx = end1.X - start.X;
                dy = end1.Y - start.Y;
                dz = end1.Z - start.Z;
            }
            else if (n > d)
            {
                dx = end2.X - start.X;
                dy = end2.Y - start.Y;
                dz = end2.Z - start.Z;
            }
            else
            {
                dx = end1.X + dx * n / d - start.X;
                dy = end1.Y + dy * n / d - start.Y;
                dz = end1.Z + dz * n / d - start.Z;
            }
            return dx * dx + dy * dy + dz * dz;
        }

        public static Vector3 ParseVector3(this string s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                string[] ax = s.Split(new char[] { ' ', ',', ':' });
                if (ax.Length == 3 && float.TryParse(ax[0], out float x) && float.TryParse(ax[1], out float y) && float.TryParse(ax[2], out float z))
                    return new Vector3(x, y, z);
            }
            return Vector3.Zero;
        }

        public static Vector4 ParseVector4(this string s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                string[] ax = s.Split(new char[] { ' ', ',', ':' });
                if (ax.Length == 4 && float.TryParse(ax[0], out float x) && float.TryParse(ax[1], out float y) && float.TryParse(ax[2], out float z) && float.TryParse(ax[3], out float w))
                    return new Vector4(x, y, z, w);
            }
            return Vector4.Zero;
        }

        public static Color ParseColor(this string s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                string[] ax = s.Split(new char[] { ' ', ',', ':' });
                if (ax.Length == 4 && float.TryParse(ax[0], out float a) && float.TryParse(ax[1], out float r) && float.TryParse(ax[2], out float g) && float.TryParse(ax[3], out float b))
                    return new Color(a, r, g, b);
                else if (ax.Length == 3 && float.TryParse(ax[0], out r) && float.TryParse(ax[1], out g) && float.TryParse(ax[2], out b))
                    return new Color(255, r, g, b);
            }
            return Color.Transparent;
        }

    }
}