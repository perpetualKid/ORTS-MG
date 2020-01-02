using System;
using System.Drawing;
using System.Windows.Forms;

using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    public partial class DispatcherViewControl : UserControl
    {
        private Simulator simulator;

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
                    content.UpdateScale(1/.9);
                    break;
                case Keys.PageUp:
                    content.UpdateScale(.9);
                    break;
                case Keys.Left:
                    content.UpdateLocation(new PointF(1, 0));
                    break;
                case Keys.Right:
                    content.UpdateLocation(new PointF(-1, 0));
                    break;
                case Keys.Up:
                    content.UpdateLocation(new PointF(0, -1));
                    break;
                case Keys.Down:
                    content.UpdateLocation(new PointF(0, 1));
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

        private int viewVersion;
        internal void Draw(RenderFrame currentFrame)
        {
            if (currentFrame.ViewVersion != viewVersion)
            {
                pbDispatcherView.Image = currentFrame.Image;
                pbDispatcherView.Invalidate();
                viewVersion = currentFrame.ViewVersion;
            }
        }

        private void PictureBoxDispatcherView_SizeChanged(object sender, EventArgs e)
        {
            content?.UpdateSize(pbDispatcherView.Size);
        }
    }
}
