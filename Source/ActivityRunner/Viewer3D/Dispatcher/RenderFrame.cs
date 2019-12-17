using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    internal class RenderFrame
    {
        public Image Image { get; private set; }

        private static Image staticContentImage = new Bitmap(1, 1);
        private static Size staticContentSize;
        private long imageTag;

        public RectangleF ViewPort { get; private set; }

        private DispatcherContent content;

        public bool FinishedUpdate { get; private set; }

        private readonly Pen redPen = new Pen(Color.Red);
        private readonly Pen greenPen = new Pen(Color.Green);
        private readonly Pen orangePen = new Pen(Color.Orange);
        private readonly Pen trainPen = new Pen(Color.DarkGreen);
        private readonly Pen pathPen = new Pen(Color.DeepPink);
        private static readonly Pen staticGrayPen = new Pen(Color.Gray);

        private AutoResetEvent preRenderEvent = new AutoResetEvent(true);

        private double scale;
        private RectangleF viewPort;

        static RenderFrame()
        {
            staticContentImage.Tag = 0L;
        }

        public RenderFrame(DispatcherContent content)
        {
            staticContentSize = Size.Empty;
            this.content = content;
        }

        public bool Update()
        {
            if ((content.Size != Size.Empty && content.Size != staticContentSize))
            {
                if (preRenderEvent.WaitOne(0))
                {
                    staticContentSize = content.Size;
                    Task.Run(async () =>
                    {
                        await PrepareStaticImage(content, content.Size, content.Scale, content.ViewPoint).ConfigureAwait(false);
                        preRenderEvent.Set();
                    });
                }
            }
            viewPort = content.ViewPort;
            if (((long)staticContentImage.Tag) != imageTag)
            {
                Image?.Dispose();
                Image = (Image)staticContentImage?.Clone();
                imageTag = (long)staticContentImage.Tag;
                return true;
            }
            return false;
            //draw dynamic content

            //signal done
        }

        private static Task PrepareStaticImage(DispatcherContent content, Size dimensions, double scale, PointF viewPoint)
        {
            Bitmap result = new Bitmap(dimensions.Width, dimensions.Height);
            Graphics g = Graphics.FromImage(result);
            g.Clear(Color.White);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TranslateTransform(-viewPoint.X, -viewPoint.Y, System.Drawing.Drawing2D.MatrixOrder.Append);
            g.ScaleTransform((float)scale, (float)-scale, System.Drawing.Drawing2D.MatrixOrder.Append);
            g.TranslateTransform(dimensions.Width / 2f, dimensions.Height / 2f, System.Drawing.Drawing2D.MatrixOrder.Append);

            staticGrayPen.Width = 0.2f;
            foreach (var segment in content.TrackSegments)
            {
                //TODO check if out of visible bounds
                if (segment.IsCurved)
                {
                    g.DrawCurve(staticGrayPen, segment.CurvePoints);
                }
                else
                {
                    g.DrawLine(staticGrayPen, segment.CurvePoints[0], segment.CurvePoints[2]);
                }
            }
            result.Tag = DateTime.UtcNow.Ticks;
            staticContentImage = result;
            return Task.CompletedTask;
        }
    }
}
