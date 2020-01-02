using System;
using System.ComponentModel;
using System.Drawing;

using Orts.Common;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Drawing
{
    internal class ScaleRuler: BaseDraw
    {
        private static readonly Pen rulerPen = new Pen(Color.Gray, 1);
        private static readonly Point rulerPositionDelta = new Point(15, 20);
        private static readonly Font markerFont = new Font(FontFamily.GenericSansSerif, 8);
        const int markerLength = 3;

        private static readonly float[] imperialRulerData = new float[] { 0f, 0.9144f, 1.8288f, 4.572f, 9.144f, 18.288f, 45.72f, 91.44f, 182.88f, 356.76f, 731.52f, 1609.344f, 3218.688f, 8046.72f, 16093.44f, 32186.88f,  80467.2f};

        private readonly bool metric;

        private enum MetricRuler
        {
            [Description("0m")]     m0_0    = 0,
            [Description("1m")]     m0_1    = 1,
            [Description("2m")]     m0_2    = 2,
            [Description("5m")]     m0_5    = 5,
            [Description("10m")]    m0_10   = 10,
            [Description("20m")]    m0_20   = 20,
            [Description("50m")]    m0_50   = 50,
            [Description("100m")]   m0_100  = 100,
            [Description("200m")]   m0_200  = 200,
            [Description("500m")]   m0_500  = 500,
            [Description("1km")]    m1_000  = 1000,
            [Description("2km")]    m2_000  = 2000,
            [Description("5km")]    m5_500  = 5000,
            [Description("10km")]   m10_000 = 10000,
            [Description("20km")]   m20_000 = 20000,
            [Description("50km")]   m50_000 = 50000,
            [Description("100km")]  m100_000 = 100000,
        }

        private enum ImperialRuler
        {
            [Description("0yd")]    i0_0 = 0,
            [Description("1yd")]    i0_1 = 1,
            [Description("2yd")]    i0_2 = 2,
            [Description("5yd")]    i0_5 = 3,
            [Description("10yd")]   i0_10 = 4,
            [Description("20yd")]   i0_20 = 5,
            [Description("50yd")]   i0_50 = 6,
            [Description("100yd")]  i0_100 = 7,
            [Description("200yd")]  i0_200 = 8,
            [Description("400yd")]  i0_400 = 9,
            [Description("800yd")]  i0_800 = 10,
            [Description("1mi")]    i1_000 = 11,
            [Description("2mi")]    i2_500 = 12,
            [Description("5mi")]    i5_000 = 13,
            [Description("10mi")]   i10_000 = 14,
            [Description("20mi")]   i20_000 = 15,
            [Description("50mi")]   i50_000 = 16,
        }

        public ScaleRuler(bool metric)
        {
            this.metric = metric;
        }

        internal override void Draw(Graphics g, Size dimensions, double scale, RectangleF viewPort)
        {
            //max size (length) of the ruler. if less than 50px available, don't draw
            int maxLength = Math.Min(200, dimensions.Width - rulerPositionDelta.X * 2);
            if (maxLength < 50)
                return;
            MetricRuler metricRuler = MetricRuler.m100_000;
            ImperialRuler imperialRuler = ImperialRuler.i50_000;
            int rulerLength;
            if (metric)
            {
                while ((int)metricRuler * scale > maxLength && metricRuler != MetricRuler.m0_0)
                {
                    metricRuler = EnumExtension.Previous(metricRuler);
                }
                rulerLength = (int)((int)metricRuler * scale);
            }
            else
            {
                while (imperialRulerData[(int)imperialRuler] * scale > maxLength && imperialRuler != ImperialRuler.i0_0)
                {
                    imperialRuler = EnumExtension.Previous(imperialRuler);
                }
                rulerLength = (int)(imperialRulerData[(int)imperialRuler] * scale);
            }

            g.DrawLine(rulerPen, rulerPositionDelta.X, dimensions.Height - rulerPositionDelta.Y, rulerPositionDelta.X + rulerLength, dimensions.Height - rulerPositionDelta.Y);
            g.DrawLine(rulerPen, rulerPositionDelta.X, dimensions.Height - rulerPositionDelta.Y + markerLength, rulerPositionDelta.X, dimensions.Height - rulerPositionDelta.Y - markerLength);
            g.DrawLine(rulerPen, rulerPositionDelta.X + rulerLength, dimensions.Height - rulerPositionDelta.Y + markerLength, rulerPositionDelta.X + rulerLength, dimensions.Height - rulerPositionDelta.Y - markerLength);
            g.DrawLine(rulerPen, rulerPositionDelta.X + rulerLength *0.5f, dimensions.Height - rulerPositionDelta.Y + markerLength, rulerPositionDelta.X + rulerLength * 0.5f, dimensions.Height - rulerPositionDelta.Y - markerLength);
            //g.DrawLine(rulerPen, rulerPositionDelta.X + rulerLength * 0.25f, dimensions.Height - rulerPositionDelta.Y - markerLength, rulerPositionDelta.X + rulerLength * 0.25f, dimensions.Height - rulerPositionDelta.Y);
            //g.DrawLine(rulerPen, rulerPositionDelta.X + rulerLength * 0.75f, dimensions.Height - rulerPositionDelta.Y - markerLength, rulerPositionDelta.X + rulerLength * 0.75f, dimensions.Height - rulerPositionDelta.Y);

            if (metric)
            {
                SizeF textSize = g.MeasureString(MetricRuler.m0_0.GetDescription(), markerFont);
                g.DrawString(MetricRuler.m0_0.GetDescription(), markerFont, new SolidBrush(Color.Black), rulerPositionDelta.X - textSize.Width / 2, dimensions.Height - rulerPositionDelta.Y + 5);
                textSize = g.MeasureString(metricRuler.GetDescription(), markerFont);
                g.DrawString(metricRuler.GetDescription(), markerFont, new SolidBrush(Color.Black), rulerPositionDelta.X + rulerLength - textSize.Width / 2, dimensions.Height - rulerPositionDelta.Y + 5);
            }
            else
            {
                SizeF textSize = g.MeasureString(ImperialRuler.i0_0.GetDescription(), markerFont);
                g.DrawString(ImperialRuler.i0_0.GetDescription(), markerFont, new SolidBrush(Color.Black), rulerPositionDelta.X - textSize.Width / 2, dimensions.Height - rulerPositionDelta.Y + 5);
                textSize = g.MeasureString(imperialRuler.GetDescription(), markerFont);
                g.DrawString(imperialRuler.GetDescription(), markerFont, new SolidBrush(Color.Black), rulerPositionDelta.X + rulerLength - textSize.Width / 2, dimensions.Height - rulerPositionDelta.Y + 5);
            }
        }
    }
}
