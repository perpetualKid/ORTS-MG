using System.Drawing;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Drawing
{
    internal abstract class BaseDraw
    {
        internal abstract void Draw(Graphics g, Size dimensions, double scale, RectangleF viewPort);
    }
}
