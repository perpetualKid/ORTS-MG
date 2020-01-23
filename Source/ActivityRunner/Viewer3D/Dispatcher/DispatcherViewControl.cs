using System;
using System.Drawing;
using System.Windows.Forms;

using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    public partial class DispatcherViewControl : UserControl
    {
        private Simulator simulator;

        private readonly DispatcherContent content;
        private Point pbCenter;

        public DispatcherViewControl()
        {
            InitializeComponent();
            pbDispatcherView.MouseWheel += PictureBoxDispatcherView_MouseWheel;
            pbDispatcherView.PreviewKeyDown += PbDispatcherView_PreviewKeyDown;
        }

        private void PbDispatcherView_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.PageDown:
                    content.UpdateScaleAt(pbCenter, -1);
                    break;
                case Keys.PageUp:
                    content.UpdateScaleAt(pbCenter, 1);
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

        private void PictureBoxDispatcherView_MouseWheel(object sender, MouseEventArgs e)
        {
            content.UpdateScaleAt(e.Location, Math.Sign(e.Delta));
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
            pbCenter = new Point(pbDispatcherView.Width / 2, pbDispatcherView.Height / 2);
            if (pbDispatcherView.Size != Size.Empty)
                content?.UpdateSize(pbDispatcherView.Size);
        }

        private void DispatcherViewControl_MouseMove(object sender, MouseEventArgs e)
        {
#if DEBUG
            //debug only
            toolStripPosition.Text = $"{(e.X) / content.Scale + content.DisplayPort.X}x{(pbDispatcherView.Height - e.Y) / content.Scale + content.DisplayPort.Y}";
#endif
        }

        private void PictureBoxDispatcherView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            content.ResetView();
        }

        private void PictureBoxDispatcherView_MouseEnter(object sender, EventArgs e)
        {
            //ensure to set the focus to make MouseWheel event work
            pbDispatcherView.Focus();
        }
    }
}
