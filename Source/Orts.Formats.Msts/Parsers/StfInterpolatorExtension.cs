using System;
using System.Collections.Generic;

using Orts.Common.Calc;

namespace Orts.Formats.Msts.Parsers
{
    public static class StfInterpolatorExtension
    {
        public static Interpolator CreateInterpolator(this STFReader stf)
        {
            if (null == stf)
                throw new ArgumentNullException(nameof(stf));

            List<double> list = new List<double>();
            stf.MustMatchBlockStart();
            while (!stf.EndOfBlock())
                list.Add(stf.ReadFloat(STFReader.Units.Any, null));
            if (list.Count % 2 == 1)
                STFException.TraceWarning(stf, "Ignoring extra odd value in Interpolator list.");
            int n = list.Count / 2;
            if (n < 2)
                STFException.TraceWarning(stf, "Interpolator must have at least two value pairs.");
            double[] xArray = new double[n];
            double[] yArray = new double[n];
            for (int i = 0; i < n; i++)
            {
                xArray[i] = list[2 * i];
                yArray[i] = list[2 * i + 1];
                if (i > 0 && xArray[i - 1] >= xArray[i])
                    STFException.TraceWarning(stf, "Interpolator x values must be increasing.");
            }
            return new Interpolator(xArray, yArray);
        }

        public static Interpolator2D CreateInterpolator2D(this STFReader stf)
        {
            if (null == stf)
                throw new ArgumentNullException(nameof(stf));

            List<double> xlist = new List<double>();
            List<Interpolator> ilist = new List<Interpolator>();
            stf.MustMatchBlockStart();
            while (!stf.EndOfBlock())
            {
                xlist.Add(stf.ReadFloat(STFReader.Units.Any, null));
                ilist.Add(stf.CreateInterpolator());
            }
            stf.SkipRestOfBlock();
            int n = xlist.Count;
            if (n < 2)
                STFException.TraceWarning(stf, "Interpolator must have at least two x values.");
            double[] xArray = new double[n];
            Interpolator[] yArray = new Interpolator[n];
            for (int i = 0; i < n; i++)
            {
                xArray[i] = xlist[i];
                yArray[i] = ilist[i];
                if (i > 0 && xArray[i - 1] >= xArray[i])
                    STFException.TraceWarning(stf, " Interpolator x values must be increasing.");
            }
            return new Interpolator2D(xArray, yArray);
        }

        public static Interpolator2D CreateInterpolator2D(this STFReader stf, bool tab)
        {
            if (null == stf)
                throw new ArgumentNullException(nameof(stf));

            List<double> xlist = new List<double>();
            List<Interpolator> ilist = new List<Interpolator>();

            bool errorFound = false;
            if (tab)
            {
                stf.MustMatchBlockStart();
                int numOfRows = stf.ReadInt(0);
                if (numOfRows < 2)
                {
                    STFException.TraceWarning(stf, "Interpolator must have at least two rows.");
                    errorFound = true;
                }
                int numOfColumns = stf.ReadInt(0);
                string header = stf.ReadString();
                if ("throttle".Equals(header, System.StringComparison.OrdinalIgnoreCase))
                {
                    stf.MustMatchBlockStart();
                    int numOfThrottleValues = 0;
                    while (!stf.EndOfBlock())
                    {
                        xlist.Add(stf.ReadFloat(STFReader.Units.None, 0f));
                        ilist.Add(new Interpolator(numOfRows));
                        numOfThrottleValues++;
                    }
                    if (numOfThrottleValues != (numOfColumns - 1))
                    {
                        STFException.TraceWarning(stf, "Interpolator throttle vs. num of columns mismatch.");
                        errorFound = true;
                    }

                    if (numOfColumns < 3)
                    {
                        STFException.TraceWarning(stf, "Interpolator must have at least three columns.");
                        errorFound = true;
                    }

                    int numofData = 0;
                    string tableLabel = stf.ReadString();
                    if ("table".Equals(tableLabel, System.StringComparison.OrdinalIgnoreCase))
                    {
                        stf.MustMatchBlockStart();
                        for (int i = 0; i < numOfRows; i++)
                        {
                            float x = stf.ReadFloat(STFReader.Units.SpeedDefaultMPH, 0);
                            numofData++;
                            for (int j = 0; j < numOfColumns - 1; j++)
                            {
                                if (j >= ilist.Count)
                                {
                                    STFException.TraceWarning(stf, "Interpolator throttle vs. num of columns mismatch. (missing some throttle values)");
                                    errorFound = true;
                                }
                                ilist[j][x] = stf.ReadFloat(STFReader.Units.Force, 0);
                                numofData++;
                            }
                        }
                        stf.SkipRestOfBlock();
                    }
                    else
                    {
                        STFException.TraceWarning(stf, "Interpolator didn't find a table to load.");
                        errorFound = true;
                    }
                    //check the table for inconsistencies

                    foreach (Interpolator checkMe in ilist)
                    {
                        if (checkMe.Size != numOfRows)
                        {
                            STFException.TraceWarning(stf, "Interpolator has found a mismatch between num of rows declared and num of rows given.");
                            errorFound = true;
                        }
                        double dx = (checkMe.MaxX() - checkMe.MinX()) * 0.1f;
                        if (dx <= 0f)
                        {
                            STFException.TraceWarning(stf, "Interpolator has found X data error - x values must be increasing. (Possible row number mismatch)");
                            errorFound = true;
                        }
                        else
                        {
                            for (double x = checkMe.MinX(); x <= checkMe.MaxX(); x += dx)
                            {
                                if (double.IsNaN(checkMe[x]))
                                {
                                    STFException.TraceWarning(stf, "Interpolator has found X data error - x values must be increasing. (Possible row number mismatch)");
                                    errorFound = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (numofData != (numOfRows * numOfColumns))
                    {
                        STFException.TraceWarning(stf, "Interpolator has found a mismatch: num of data doesn't fit the header information.");
                        errorFound = true;
                    }
                }
                else
                {
                    STFException.TraceWarning(stf, "Interpolator must have a 'throttle' header row.");
                    errorFound = true;
                }
                stf.SkipRestOfBlock();
            }
            else
            {
                stf.MustMatchBlockStart();
                while (!stf.EndOfBlock())
                {
                    xlist.Add(stf.ReadFloat(STFReader.Units.Any, null));
                    ilist.Add(stf.CreateInterpolator());
                }
            }


            int n = xlist.Count;
            if (n < 2)
            {
                STFException.TraceWarning(stf, "Interpolator must have at least two x values.");
                errorFound = true;
            }
            double[] xArray = new double[n];
            Interpolator[] yArray = new Interpolator[n];
            for (int i = 0; i < n; i++)
            {
                xArray[i] = xlist[i];
                yArray[i] = ilist[i];
                if (i > 0 && xArray[i - 1] >= xArray[i])
                    STFException.TraceWarning(stf, "Interpolator x values must be increasing.");
            }
            if (errorFound)
            {
                STFException.TraceWarning(stf, "Errors found in the Interpolator definition!!! The Interpolator will not work correctly!");
            }

            Interpolator2D result = new Interpolator2D(xArray, yArray);
            return result;
        }
    }
}
