using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    public partial class DispatcherViewControl : UserControl
    {
        private Simulator simulator;

        private readonly DispatcherContent content;
        private bool panning;
        private Point pbCenter;
        private PointF panStart;
        private PointF offsetPanStart;
        private DateTime updateTimestamp;


        public DispatcherViewControl()
        {
            InitializeComponent();
//            typeof(Panel).InvokeMember("DoubleBuffered", BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic, null, pnlDispatcherView, new object[] { true });
            pbDispatcherView.MouseWheel += PictureBoxDispatcherView_MouseWheel;
            pbDispatcherView.PreviewKeyDown += PbDispatcherView_PreviewKeyDown;
            pbDispatcherView.MouseDown += PbDispatcherView_MouseDown;
            pbDispatcherView.MouseUp += PbDispatcherView_MouseUp;
            pbDispatcherView.MouseMove += PbDispatcherView_MouseMove;
        }

        private void PbDispatcherView_MouseMove(object sender, MouseEventArgs e)
        {
            if (panning && DateTime.UtcNow > updateTimestamp)
            {
                if (((offsetPanStart.X - (panStart.X - e.X) / content.Scale) > (pbDispatcherView.Width - 10) / content.Scale) || ((offsetPanStart.X + content.Size.Width - (panStart.X - e.X) / content.Scale) < 10)
                    || ((offsetPanStart.Y - (panStart.Y - e.Y) / content.Scale) > (pbDispatcherView.Height- 10) / content.Scale) || ((offsetPanStart.Y + content.Size.Height - (panStart.Y - e.Y) / content.Scale) < 10))
                {
                    panning = false;
                    return;
                }
                PointF offset = new PointF((float)(offsetPanStart.X - (panStart.X - e.X) / content.Scale), (float)(offsetPanStart.Y - (panStart.Y - e.Y) / content.Scale));
                content.UpdateLocationAbsolute(offset);
                updateTimestamp = updateTimestamp.AddMilliseconds(200);
            }
        }

        private void PbDispatcherView_MouseUp(object sender, MouseEventArgs e)
        {
            panning = false;
        }

        private void PbDispatcherView_MouseDown(object sender, MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                panning = true;
                panStart = e.Location;
                offsetPanStart = content.Offset;
            }
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
            toolStripSize.Text = $"{pbDispatcherView.Width / content.Scale:N2}x{pbDispatcherView.Height / content.Scale:N2}, Scale {content.Scale}";
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
