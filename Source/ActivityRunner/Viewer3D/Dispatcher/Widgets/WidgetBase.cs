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

        internal abstract void Normalize(in RectangleF bounds);

        internal abstract void Draw(Graphics g);

    }

    internal abstract class PointWidget : WidgetBase
    {
        public PointF Location { get; protected set; }


        internal override void Normalize(in RectangleF bounds)
        {
            Location = new PointF(Location.X - bounds.Location.X, bounds.Size.Height - (Location.Y - bounds.Location.Y));
        }

        /// <summary>
        /// Generates a rectangle representing a dot being drawn.
        /// </summary>
        /// <param name="p">Center point of the dot, in pixels.</param>
        /// <param name="size">Size of the dot's diameter, in pixels</param>
        /// <returns></returns>
        protected static RectangleF CenterRectangle(PointF center)
        {
            return CenterRectangle(center, itemSize);
        }

        /// <summary>
        /// Generates a rectangle representing a dot being drawn.
        /// </summary>
        /// <param name="p">Center point of the dot, in pixels.</param>
        /// <param name="size">Size of the dot's diameter, in pixels</param>
        /// <returns></returns>
        protected static RectangleF CenterRectangle(PointF center, float size)
        {
            return new RectangleF(center.X - size / 2f, center.Y - size/ 2f, size, size);
        }
    }
}
