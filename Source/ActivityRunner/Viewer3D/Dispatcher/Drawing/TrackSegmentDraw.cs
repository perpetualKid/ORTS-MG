using System.Drawing;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Drawing
{
    internal class TrackSegmentDraw : BaseDraw
    {
        private static readonly Pen trackSegmentPen = new Pen(Color.Gray);

        internal TrackSegmentDraw(DispatcherContent content): 
            base(content)
        { }

        internal override void Draw(Graphics g, Size dimensions, double scale)
        {
            trackSegmentPen.Width = 1.345f;// track width

            foreach (var segment in content.TrackSegments)
            {
                //skip segments which are outside bounds. 
                if ((segment.CurvePoints[0].X < content.DisplayPort.X && segment.CurvePoints[2].X < content.DisplayPort.X) ||
                    (segment.CurvePoints[0].X > content.DisplayPort.X + content.DisplayPort.Width && segment.CurvePoints[2].X > content.DisplayPort.X + content.DisplayPort.Width) ||
                    (segment.CurvePoints[0].Y < content.DisplayPort.Y && segment.CurvePoints[2].Y < content.DisplayPort.Y) ||
                    (segment.CurvePoints[0].Y > content.DisplayPort.Y + content.DisplayPort.Height && segment.CurvePoints[2].Y > content.DisplayPort.Y + content.DisplayPort.Height))
                    continue;
                if (segment.IsCurved)
                    {
                        g.DrawCurve(trackSegmentPen, segment.CurvePoints);
                    }
                    else
                    {
                        g.DrawLine(trackSegmentPen, segment.CurvePoints[0], segment.CurvePoints[2]);
                    }
            }
        }
    }
}
