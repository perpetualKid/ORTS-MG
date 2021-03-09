﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Models.Simplified;

namespace Orts.TrackViewer
{
    public partial class GameWindow : Game
    {
        public void ChangeScreenMode()
        {
            SetScreenMode(currentScreenMode.Next());
        }

        public void CloseWindow()
        {
            if (MessageBox.Show("Title", "Text", MessageBoxButtons.OKCancel) == DialogResult.OK)
                windowForm.Close();
        }

        public void ExitApplication()
        {
            if (MessageBox.Show("Title", "Text", MessageBoxButtons.OKCancel) == DialogResult.OK)
                Exit();
        }

        public void MouseMove(Point position, Vector2 delta)
        {
        }

        public void MouseWheel(Point position, int delta)
        {
            contentArea?.UpdateScaleAt(position.ToVector2(), System.Math.Sign(delta));
        }

        public void MouseDragging(Point position, Vector2 delta)
        {
            contentArea?.UpdatePosition(delta);
        }

        public void MouseButtonUp(Point position)
        {
            System.Diagnostics.Debug.WriteLine($"Up {Window.Title} - {position}");
        }

        public void MouseButtonDown(Point position)
        {
            System.Diagnostics.Debug.WriteLine($"Down {Window.Title} - {position}");
        }

    }
}