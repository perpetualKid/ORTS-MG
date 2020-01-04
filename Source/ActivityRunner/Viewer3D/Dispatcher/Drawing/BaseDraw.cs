using System.Drawing;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Drawing
{
    internal abstract class BaseDraw
    {
        protected readonly DispatcherContent content;

        internal BaseDraw(DispatcherContent content)
        {
            this.content = content;
        }

        internal abstract void Draw(Graphics g, Size windowSize, double scale);
    }
}
