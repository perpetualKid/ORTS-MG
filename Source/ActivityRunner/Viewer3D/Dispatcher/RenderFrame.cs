using Orts.ActivityRunner.Viewer3D.Dispatcher.Drawing;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    internal class RenderFrame
    {
        public Image Image { get; private set; }

        #region shared fields
        private static Image sharedContentImage = new Bitmap(1, 1);
        private static int sharedContentUpdate;
        private static volatile int sharedViewVersion;

        private static readonly Pen sharedGrayPen = new Pen(Color.Gray);
        private readonly ScaleRuler ruler;
        #endregion

        public int ViewVersion { get; private set; }

        private DispatcherContent content;

        public bool FinishedUpdate { get; private set; }

        private readonly Pen redPen = new Pen(Color.Red);
        private readonly Pen greenPen = new Pen(Color.Green);
        private readonly Pen orangePen = new Pen(Color.Orange);
        private readonly Pen trainPen = new Pen(Color.DarkGreen);
        private readonly Pen pathPen = new Pen(Color.DeepPink);

        static RenderFrame()
        {
            sharedContentImage.Tag = 0L;
        }

        public RenderFrame(DispatcherContent content)
        {
            sharedContentUpdate = 0;
            sharedViewVersion = 0;
            this.content = content;
            ruler = new ScaleRuler(content.MetricUnits);
        }

        public bool Update()
        {
            if (content.ViewVersion != sharedViewVersion && (Interlocked.CompareExchange(ref sharedContentUpdate, 1, 0) == 0))
            {
                Task.Run(async () =>
                {
                    Debug.Assert(content.Size != Size.Empty);
                    await PrepareStaticImage(content, content.Size, content.Scale, content.ViewPort).ConfigureAwait(false);
                    sharedViewVersion = content.ViewVersion;
                    sharedContentUpdate = 0;
                });
            }
            if (ViewVersion != sharedViewVersion)
            {
                Image?.Dispose();
                ViewVersion = sharedViewVersion;
                Image = (Image)sharedContentImage?.Clone();
                return true;
            }
            return false;
            //draw dynamic content

            //signal done
        }

        private Task PrepareStaticImage(DispatcherContent content, Size dimensions, double scale, RectangleF viewPort)
        {
            PointF viewPoint = new PointF(viewPort.X + viewPort.Width / 2f, viewPort.Y + viewPort.Height / 2f);
            Bitmap result = new Bitmap(dimensions.Width, dimensions.Height);
            Graphics g = Graphics.FromImage(result);
            g.Clear(Color.White);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            //draw anything here which has fixed location and/or does not need to scale
            //ruler
            ruler.Draw(g, dimensions, scale, viewPort);

            //
            g.TranslateTransform(-viewPoint.X, -viewPoint.Y, System.Drawing.Drawing2D.MatrixOrder.Append);
            g.ScaleTransform((float)scale, (float)-scale, System.Drawing.Drawing2D.MatrixOrder.Append);
            g.TranslateTransform(dimensions.Width / 2f, dimensions.Height / 2f, System.Drawing.Drawing2D.MatrixOrder.Append);

            sharedGrayPen.Width = 0.2f;

            foreach (var segment in content.TrackSegments)
            {
                //TODO check if out of visible bounds
                if (segment.IsCurved)
                {
                    g.DrawCurve(sharedGrayPen, segment.CurvePoints);
                }
                else
                {
                    g.DrawLine(sharedGrayPen, segment.CurvePoints[0], segment.CurvePoints[2]);
                }
            }
            sharedContentImage = result;
            return Task.CompletedTask;
        }
    }
}
