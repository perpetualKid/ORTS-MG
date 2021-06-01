// COPYRIGHT 2010, 2011, 2012, 2013, 2014, 2015 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

// This file is the responsibility of the 3D & Environment Team.

using Microsoft.Xna.Framework;
using Orts.Common;

namespace Orts.ActivityRunner.Viewer3D.Popups
{
    public class HUDScrollWindow : Window
    {
        private Label pageDown;
        private Label pageUp;
        private Label pageLeft;
        private Label pageRight;
        private Label nextLoco;
        private Label prevLoco;
        private Label screenMode;

        public HUDScrollWindow(WindowManager owner)
            : base(owner, Window.DecorationSize.X + owner.TextFontDefault.Height * 8, Window.DecorationSize.Y + owner.TextFontDefault.Height * 9 + ControlLayout.SeparatorSize * 2, Viewer.Catalog.GetString("HUD Scroll"))
        {
        }
        
        private void ScreenMode_Click(Control arg1, Point arg2)
        {
            screenMode.Color = Color.White;
            if (!HUDWindow.hudWindowFullScreen && (HUDWindow.hudWindowColumnsPagesCount > 0 || HUDWindow.hudWindowColumnsActualPage > 0 || HUDWindow.hudWindowLinesPagesCount > 1 || HUDWindow.hudWindowLinesActualPage > 1))
            {
                HUDWindow.hudWindowColumnsActualPage = 0;
                HUDWindow.hudWindowLinesActualPage = 1;
                HUDWindow.hudWindowFullScreen = true;
                
            }
            else
            {
                HUDWindow.hudWindowColumnsActualPage = 0;
                HUDWindow.hudWindowLinesActualPage = 1;
                HUDWindow.hudWindowFullScreen = false;
            }
        }

        private void PageRight_Click(Control arg1, Point arg2)
        {
            LabelReset();
            if (HUDWindow.hudWindowColumnsPagesCount > 0 && HUDWindow.hudWindowColumnsPagesCount > HUDWindow.hudWindowColumnsActualPage)
            {
                HUDWindow.hudWindowColumnsActualPage += 1;
                pageRight.Color = Color.White;
            }
        }

        private void PageLeft_Click(Control arg1, Point arg2)
        {
            if (HUDWindow.hudWindowColumnsActualPage > 0)
            {
                HUDWindow.hudWindowColumnsActualPage -= 1;
                pageLeft.Color = Color.White;
            }
        }

        private void PageUp_Click(Control arg1, Point arg2)
        {
            if (HUDWindow.hudWindowLinesActualPage > 1)
            {
                HUDWindow.hudWindowLinesActualPage -= 1;
                pageUp.Color = Color.White;
            }
        }

        private void PageDown_Click(Control arg1, Point arg2)
        {
            if (HUDWindow.hudWindowLinesPagesCount > 1 && HUDWindow.hudWindowLinesPagesCount > HUDWindow.hudWindowLinesActualPage)
            {
                HUDWindow.hudWindowLinesActualPage += 1;
                pageDown.Color = Color.White;
            }
        }

        private void NextLoco_Click(Control arg1, Point arg2)
        {
            if (!HUDWindow.hudWindowSteamLocoLead && HUDWindow.hudWindowLocoPagesCount > 0 && HUDWindow.hudWindowLocoPagesCount > HUDWindow.hudWindowLocoActualPage)
            {
                HUDWindow.hudWindowLocoActualPage += 1;
                nextLoco.Color = Color.White;
            }
        }

        private void PrevLoco_Click(Control arg1, Point arg2)
        {
            if (!HUDWindow.hudWindowSteamLocoLead && HUDWindow.hudWindowLocoActualPage > 0)
            {
                HUDWindow.hudWindowLocoActualPage -= 1;
                prevLoco.Color = Color.White;
                if (HUDWindow.hudWindowLocoActualPage == 0)
                {//Restore to initial values
                    HUDWindow.hudWindowLinesActualPage = 1;
                    HUDWindow.hudWindowColumnsActualPage = 0;
                }
            }
        }

        private void LabelReset()
        {
            if (HUDWindow.hudWindowLinesPagesCount==1) pageDown.Text = Viewer.Catalog.GetString("▼ Page Down");
            if (HUDWindow.hudWindowLinesPagesCount>1) pageUp.Text= Viewer.Catalog.GetString("▲ Page Up");
        }

        protected override ControlLayout Layout(ControlLayout layout)
        {
            var vbox = base.Layout(layout).AddLayoutVertical();
            {
                var hbox = vbox.AddLayoutHorizontalLineOfText();
                pageDown = new Label(hbox.RemainingWidth, hbox.RemainingHeight, HUDWindow.hudWindowLinesPagesCount > 1 ? Viewer.Catalog.GetString("▼ Page Down (") + HUDWindow.hudWindowLinesActualPage + "/" + HUDWindow.hudWindowLinesPagesCount + ")" : Viewer.Catalog.GetString("▼ Page Down")) { Color = (HUDWindow.hudWindowLinesPagesCount > HUDWindow.hudWindowLinesActualPage) ? Color.Gray : Color.Black };
                pageDown.Click += PageDown_Click;
                vbox.Add(pageDown);
                
                pageUp = new Label(hbox.RemainingWidth, hbox.RemainingHeight, HUDWindow.hudWindowLinesPagesCount > 1 ? Viewer.Catalog.GetString("▲ Page Up (") + HUDWindow.hudWindowLinesActualPage + " / " + HUDWindow.hudWindowLinesPagesCount + ")" : Viewer.Catalog.GetString("▲ Page Up")) { Color = HUDWindow.hudWindowLinesActualPage > 1 ? Color.Gray : Color.Black };
                pageUp.Click += PageUp_Click;
                vbox.Add(pageUp);

                vbox.AddHorizontalSeparator();
                pageLeft = new Label(hbox.RemainingWidth, hbox.RemainingHeight, Viewer.Catalog.GetString("◄ Page Left")) { Color = HUDWindow.hudWindowColumnsActualPage > 0 ? Color.Gray : Color.Black };
                pageLeft.Click += PageLeft_Click;
                vbox.Add(pageLeft);

                pageRight = new Label(hbox.RemainingWidth, hbox.RemainingHeight, Viewer.Catalog.GetString("► Page Right")) { Color = HUDWindow.hudWindowColumnsPagesCount > 0 && HUDWindow.hudWindowColumnsActualPage < HUDWindow.hudWindowColumnsPagesCount ? Color.Gray : Color.Black };
                pageRight.Click += PageRight_Click;
                vbox.Add(pageRight);

                vbox.AddHorizontalSeparator();
                nextLoco = new Label(hbox.RemainingWidth, hbox.RemainingHeight, !HUDWindow.hudWindowSteamLocoLead && HUDWindow.hudWindowLocoActualPage > 0 ? Viewer.Catalog.GetString("▼ Next Loco (") + HUDWindow.hudWindowLocoActualPage + "/" + HUDWindow.hudWindowLocoPagesCount + ")" : Viewer.Catalog.GetPluralString("= One Locomotive.", "= All Locomotives.", (long)HUDWindow.hudWindowLocoPagesCount), LabelAlignment.Left) { Color = HUDWindow.hudWindowSteamLocoLead || HUDWindow.hudWindowLocoPagesCount > HUDWindow.hudWindowLocoActualPage ? Color.Gray : Color.Black };
                nextLoco.Click += NextLoco_Click;
                vbox.Add(nextLoco);

                prevLoco = new Label(hbox.RemainingWidth, hbox.RemainingHeight, Viewer.Catalog.GetString("▲ Prev. Loco")) { Color = !HUDWindow.hudWindowSteamLocoLead && HUDWindow.hudWindowLocoActualPage > 0 ? Color.Gray : Color.Black };
                prevLoco.Click += PrevLoco_Click;
                vbox.Add(prevLoco);

                vbox.AddHorizontalSeparator();
                screenMode = new Label(hbox.RemainingWidth, hbox.RemainingHeight, (HUDWindow.hudWindowFullScreen? Viewer.Catalog.GetString("Screen: Normal"): Viewer.Catalog.GetString("Screen: Full")), LabelAlignment.Center) { Color = Color.Gray };
                screenMode.Click += ScreenMode_Click;
                vbox.Add(screenMode);
            }
            return vbox;
        }

        public override void PrepareFrame(in ElapsedTime elapsedTime, bool updateFull)
        {
            base.PrepareFrame(elapsedTime, updateFull);

            if (updateFull)
                Layout();
        }
    }
}
