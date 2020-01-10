using System.Drawing;

using Microsoft.Xna.Framework;

using Orts.Common.Position;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Widgets
{
    internal abstract class WidgetBase
    {
        protected const float trackWidth = 1.435f;
        protected const float trackCenter = trackWidth / 2;// standard track width
        protected const float itemSize = 6f;

        protected static DispatcherContent content;

        public static void SetContent(DispatcherContent content)
        {
            WidgetBase.content = content;
        }

        protected static Vector2 VectorFromLocation(in WorldLocation location)
        {
            return new Vector2((float)(location.TileX * WorldLocation.TileSize + location.Location.X), (float)(location.TileZ * WorldLocation.TileSize + location.Location.Z));
        }

        protected static PointF PointFFromLocation(in WorldLocation location)
        {
            return new PointF((float)(location.TileX * WorldLocation.TileSize + location.Location.X), (float)(location.TileZ * WorldLocation.TileSize + location.Location.Z));
        }

        internal abstract void Normalize(PointF origin);

        internal abstract void Draw(Graphics g);

    }

    internal abstract class PointWidget : WidgetBase
    {
        public PointF Location { get; protected set; }


        internal override void Normalize(PointF origin)
        {
            Location = new PointF(Location.X - origin.X, -(Location.Y - origin.Y));
        }

        /// <summary>
        /// Generates a rectangle representing a dot being drawn.
        /// </summary>
        /// <param name="p">Center point of the dot, in pixels.</param>
        /// <param name="size">Size of the dot's diameter, in pixels</param>
        /// <returns></returns>
        protected static RectangleF GetRect(PointF p)
        {
            return new RectangleF(p.X - itemSize / 2f, p.Y - itemSize / 2f, itemSize, itemSize);
        }

        protected bool CheckVisibility()
        {
            return !(Location.X < content.DisplayPort.X || (Location.X > content.DisplayPort.X + content.DisplayPort.Width) ||
                -Location.Y < content.DisplayPort.Y || (-Location.Y > content.DisplayPort.Y + content.DisplayPort.Height));
        }
    }
}
