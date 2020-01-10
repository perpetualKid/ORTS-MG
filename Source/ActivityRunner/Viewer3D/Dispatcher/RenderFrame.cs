
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

using Orts.ActivityRunner.Viewer3D.Dispatcher.Widgets;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    internal class RenderFrame
    {
        public Image Image { get; private set; }

        #region shared fields
        private static Image sharedContentImage = new Bitmap(1, 1);
        private static int sharedContentUpdate;
        private static volatile int sharedViewVersion;
        #endregion

        public int ViewVersion { get; private set; }

        private DispatcherContent content;

        public bool FinishedUpdate { get; private set; }

        public RenderFrame(DispatcherContent content)
        {
            sharedContentUpdate = 0;
            sharedViewVersion = 0;
            this.content = content;
        }

        public bool Update()
        {
            if (content.ViewVersion != sharedViewVersion && (Interlocked.CompareExchange(ref sharedContentUpdate, 1, 0) == 0))
            {
                Task.Run(async () =>
                {
                    Debug.Assert(content.WindowSize != Size.Empty);
                    await PrepareStaticImage(content, content.WindowSize, content.Scale, content.DisplayPort).ConfigureAwait(false);
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

        private Task PrepareStaticImage(DispatcherContent content, Size windowSize, double scale, RectangleF viewPort)
        {
            PointF viewPoint = new PointF(viewPort.X + viewPort.Width / 2f, viewPort.Y + viewPort.Height / 2f);
            Bitmap result = new Bitmap(windowSize.Width, windowSize.Height);
            Graphics g = Graphics.FromImage(result);
            g.Clear(Color.White);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            //draw anything here which has fixed location and/or does not need to scale
            //ruler
            //rulerDraw.Draw(g, windowSize, scale);
            content.scaleRuler.Draw(g);

            //
            g.TranslateTransform(-viewPoint.X, viewPoint.Y, System.Drawing.Drawing2D.MatrixOrder.Append);
            g.ScaleTransform((float)scale, (float)scale, System.Drawing.Drawing2D.MatrixOrder.Append);
            g.TranslateTransform(windowSize.Width / 2f, windowSize.Height / 2f, System.Drawing.Drawing2D.MatrixOrder.Append);
            foreach (TrackSegment segment in content.TrackSegments)
                segment.Draw(g);
            foreach (SwitchWidget trackSwitch in content.Switches)
                trackSwitch.Draw(g);
            foreach (SignalWidget signal in content.Signals)
                signal.Draw(g);
            foreach (SidingWidget siding in content.Sidings)
                siding.Draw(g);
            sharedContentImage = result;
            return Task.CompletedTask;
        }
    }
}
