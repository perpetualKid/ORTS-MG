using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;

namespace Orts.View.Window
{
    public class TestWindow: WindowBase
    {
        public TestWindow()
        {
            location = new Rectangle(30, 30, 200, 150);
            shader = new PopupWindowShader(graphicsDevice);
            InitializeBuffers();
        }
    }
}
