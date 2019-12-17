using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Orts.Common.Position;
using Orts.Formats.Msts.Models;
using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    public partial class DispatcherViewControl : UserControl
    {
        private Simulator simulator;

        private RectangleF viewPort;
        private PointF viewPoint;

        private DispatcherContent content;

        public DispatcherViewControl()
        {
            InitializeComponent();
        }

        internal DispatcherViewControl(DispatcherContent content): this()
        {
            this.content = content;
        }

        internal void Initialize(Simulator simulator, DispatcherContent content)
        {
            this.simulator = simulator;
            PictureBoxDispatcherView_SizeChanged(this, new EventArgs());
        }

        private void DispatcherViewControl_KeyUp(object sender, KeyEventArgs e)
        {
            switch(e.KeyCode)
            {
                case Keys.PageDown:
                    content.UpdateScale(.9);
                    break;
                case Keys.PageUp:
                    content.UpdateScale(1/.9);
                    break;
                case Keys.Left:
                    viewPoint.X += (float)(viewPort.Width / content.Scale / 40);
                    break;
                case Keys.Right:
                    viewPoint.X -= (float)(viewPort.Width / content.Scale / 40);
                    break;
                case Keys.Up:
                    viewPoint.Y += (float)(viewPort.Height / content.Scale / 40);
                    break;
                case Keys.Down:
                    viewPoint.Y -= (float)(viewPort.Height / content.Scale / 40);
                    break;
            }
        }

        public void UpdateStatusbarVisibility(bool show)
        {
            statusStripDispatcher.SizingGrip = show;
        }

        public void Update(double fps)
        {
            toolStripFPS.Text = $"{fps:F1} FPS";
            if (null != content)
            toolStripSize.Text = $"{pbDispatcherView.Width / content.Scale:N2}x{pbDispatcherView.Height / content.Scale:N2}";
        }

        internal void Draw(RenderFrame currentFrame)
        {
            pbDispatcherView.Image = currentFrame.Image;
            pbDispatcherView.Invalidate();
        }

        private void PictureBoxDispatcherView_SizeChanged(object sender, EventArgs e)
        {
            content?.UpdateSize(pbDispatcherView.Size);
        }
    }
}
