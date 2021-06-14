// COPYRIGHT 2010, 2011, 2012 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Diagnostics;
using System.IO;

namespace Orts.Common.Calc
{
    /// <summary>
    /// Interpolated table lookup
    /// Supports linear or cubic spline interpolation
    /// </summary>
    public class Interpolator
    {
        private readonly double[] xArray;  // must be in increasing order
        private readonly double[] yArray;
        private double[] y2Array;
        private int size;       // number of values populated
        private int prevIndex;  // used to speed up repeated evaluations with similar x values

        public Interpolator(int n)
        {
            xArray = new double[n];
            yArray = new double[n];
        }

        public Interpolator(double[] x, double[] y)
        {
            xArray = x;
            yArray = y;
            size = xArray.Length;
        }

        public Interpolator(Interpolator other)
        {
            if (null == other)
                throw new ArgumentNullException(nameof(other));
            xArray = other.xArray;
            yArray = other.yArray;
            y2Array = other.y2Array;
            size = other.size;
        }

        /// <summary>
        /// creates a new Interpolator instance where the x and y arrays are swapped to allow lookups the other way around
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static Interpolator InverseInterpolator(Interpolator source)
        {
            if (null == source)
                throw new ArgumentNullException(nameof(source));

            return new Interpolator(source.yArray, source.xArray);
        }

#pragma warning disable CA1043 // Use Integral Or String Argument For Indexers
        public double this[double x]
#pragma warning restore CA1043 // Use Integral Or String Argument For Indexers
        {
            get
            {
                if (x < xArray[prevIndex] || x > xArray[prevIndex + 1])
                {
                    if (x < xArray[1])
                        prevIndex = 0;
                    else if (x > xArray[size - 2])
                        prevIndex = size - 2;
                    else
                    {
                        int i = 0;
                        int j = size - 1;
                        while (j - i > 1)
                        {
                            int k = (i + j) / 2;
                            if (xArray[k] > x)
                                j = k;
                            else
                                i = k;
                        }
                        prevIndex = i;
                    }
                }
                double d = xArray[prevIndex + 1] - xArray[prevIndex];
                double a = (xArray[prevIndex + 1] - x) / d;
                double b = (x - xArray[prevIndex]) / d;
                double y = a * yArray[prevIndex] + b * yArray[prevIndex + 1];
                if (y2Array != null && a >= 0 && b >= 0)
                    y += ((a * a * a - a) * y2Array[prevIndex] + (b * b * b - b) * y2Array[prevIndex + 1]) * d * d / 6;
                return y;
            }
            set
            {
                xArray[size] = x;
                yArray[size] = value;
                size++;
            }
        }

        public double MinX() { return xArray[0]; }

        public double MaxX() { return xArray[size - 1]; }

        public double MaxY() { return MaxY(out _); }

        public double MaxY(out double x)
        {
            int maxi = 0;
            for (int i = 1; i < size; i++)
                if (yArray[maxi] < yArray[i])
                    maxi = i;
            x = xArray[maxi];
            return yArray[maxi];
        }

        public bool CheckForNegativeValue()
        {
            for (int i = 1; i < size; i++)
            {
                if (yArray[i] < 0)
                    return true;
            }
            return false;
        }

        public void ScaleX(double factor)
        {
            for (int i = 0; i < size; i++)
                xArray[i] *= factor;
        }

        public void ScaleY(double factor)
        {
            for (int i = 0; i < size; i++)
                yArray[i] *= factor;
            if (y2Array != null)
            {
                for (int i = 0; i < size; i++)
                    y2Array[i] *= factor;
            }
        }

        public void ComputeSpline()
        {
            ComputeSpline(null, null);
        }

        public void ComputeSpline(double? yp1, double? yp2)
        {
            y2Array = new double[size];
            double[] u = new double[size];
            if (yp1 == null)
            {
                y2Array[0] = 0;
                u[0] = 0;
            }
            else
            {
                y2Array[0] = -.5;
                double d = xArray[1] - xArray[0];
                u[0] = 3 / d * ((yArray[1] - yArray[0]) / d - yp1.Value);
            }
            for (int i = 1; i < size - 1; i++)
            {
                double sig = (xArray[i] - xArray[i - 1]) / (xArray[i + 1] - xArray[i - 1]);
                double p = sig * y2Array[i - 1] + 2;
                y2Array[i] = (sig - 1) / p;
                u[i] = (6 * ((yArray[i + 1] - yArray[i]) / (xArray[i + 1] - xArray[i]) -
                    (yArray[i] - yArray[i - 1]) / (xArray[i] - xArray[i - 1])) / (xArray[i + 1] - xArray[i - 1]) -
                    sig * u[i - 1]) / p;
            }
            if (yp2 == null)
            {
                y2Array[size - 1] = 0;
            }
            else
            {
                double d = xArray[size - 1] - xArray[size - 2];
                y2Array[size - 1] = (3 / d * (yp2.Value - (yArray[size - 1] - yArray[size - 2]) / d) - .5 * u[size - 2]) / (.5 * y2Array[size - 2] + 1);
            }
            for (int i = size - 2; i >= 0; i--)
                y2Array[i] = y2Array[i] * y2Array[i + 1] + u[i];
        }

        // restore game state
        public Interpolator(BinaryReader inf)
        {
            if (null == inf)
                throw new ArgumentNullException(nameof(inf));

            size = inf.ReadInt32();
            xArray = new double[size];
            yArray = new double[size];
            for (int i = 0; i < size; i++)
            {
                xArray[i] = inf.ReadDouble();
                yArray[i] = inf.ReadDouble();
            }
            if (inf.ReadBoolean())
            {
                y2Array = new double[size];
                for (int i = 0; i < size; i++)
                    y2Array[i] = inf.ReadDouble();
            }
        }

        // save game state
        public void Save(BinaryWriter outf)
        {
            if (null == outf)
                throw new ArgumentNullException(nameof(outf));

            outf.Write(size);
            for (int i = 0; i < size; i++)
            {
                outf.Write(xArray[i]);
                outf.Write(yArray[i]);
            }
            outf.Write(y2Array != null);
            if (y2Array != null)
                for (int i = 0; i < size; i++)
                    outf.Write(y2Array[i]);
        }

        public void Test(string label, int n)
        {
            double dx = (MaxX() - MinX()) / (n - 1);
            for (int i = 0; i < n; i++)
            {
                double x = MinX() + i * dx;
                double y = this[x];
                Trace.WriteLine($"{label} {x} {y}");
            }
        }

        public int Size => (xArray.Length == yArray.Length) ? size : -1;

        public bool CheckForConsistentIncrease(double step)
        {
            bool result = false;
            double value = yArray[0];
            for (int i = 1; i < size; i++)
            {
                if (yArray[i] <= value)
                {
                    step = value + step;
                    yArray[i] = step;
                    result = true;
                }
            }
            return result;
        }

    }

    /// <summary>
    /// two dimensional Interpolated table lookup - Generic
    /// </summary>
    public class Interpolator2D
    {
        private readonly double[] xArray;  // must be in increasing order
        private readonly Interpolator[] yArray;
        private int size;       // number of values populated
        private int prevIndex;  // used to speed up repeated evaluations with similar x values

        public Interpolator2D(int n)
        {
            xArray = new double[n];
            yArray = new Interpolator[n];
        }

        public Interpolator2D(double[] x, Interpolator[] y)
        {
            xArray = x;
            yArray = y;
            size = xArray.Length;
        }

        public Interpolator2D(Interpolator2D other)
        {
            if (null == other)
                throw new ArgumentNullException(nameof(other));

            xArray = other.xArray;
            size = other.size;
            yArray = new Interpolator[size];
            for (int i = 0; i < size; i++)
                yArray[i] = new Interpolator(other.yArray[i]);
        }

        public double Get(double x, double y)
        {
            if (x < xArray[prevIndex] || x > xArray[prevIndex + 1])
            {
                if (x < xArray[1])
                    prevIndex = 0;
                else if (x > xArray[size - 2])
                    prevIndex = size - 2;
                else
                {
                    int i = 0;
                    int j = size - 1;
                    while (j - i > 1)
                    {
                        int k = (i + j) / 2;
                        if (xArray[k] > x)
                            j = k;
                        else
                            i = k;
                    }
                    prevIndex = i;
                }
            }
            double d = xArray[prevIndex + 1] - xArray[prevIndex];
            double a = (xArray[prevIndex + 1] - x) / d;
            double b = (x - xArray[prevIndex]) / d;
            double z = 0;
            if (a != 0)
                z += a * yArray[prevIndex][y];
            if (b != 0)
                z += b * yArray[prevIndex + 1][y];
            return z;
        }

#pragma warning disable CA1043 // Use Integral Or String Argument For Indexers
#pragma warning disable CA1044 // Properties should not be write only
        public Interpolator this[double x]
#pragma warning restore CA1044 // Properties should not be write only
#pragma warning restore CA1043 // Use Integral Or String Argument For Indexers
        {
            set
            {
                xArray[size] = x;
                yArray[size] = value;
                size++;
            }
        }
        public double MinX() { return xArray[0]; }

        public double MaxX() { return xArray[size - 1]; }

        public void ScaleX(double factor)
        {
            for (int i = 0; i < size; i++)
                xArray[i] *= factor;
        }

        public bool HasNegativeValues { get; private set; }

        public void CheckForNegativeValues()
        {
            for (int i = 0; i < size; i++)
            {
                int size = yArray[i].Size;
                for (int j = 0; j < size; j++)
                {
                    if (yArray[i].CheckForNegativeValue())
                    {
                        HasNegativeValues = true;
                        return;
                    }
                }
            }
        }
    }

}
