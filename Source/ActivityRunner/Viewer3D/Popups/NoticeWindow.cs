// COPYRIGHT 2014 by the Open Rails project.
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
using Microsoft.Xna.Framework.Graphics;
using Orts.Simulation.RollingStocks;
using Orts.Common;

namespace Orts.ActivityRunner.Viewer3D.Popups
{
    public class NoticeWindow : LayeredWindow
    {
        private const double LocationX = 0.5;
        private const double LocationY = 0.25;
        private const double PaddingX = 0.1;
        private const double PaddingY = 0;
        private const double AnimationLength = 0.4;
        private const double AnimationFade = 0.1;
        private const double NoticeTextSize = 0.02;

        // Screen-related data
        private WindowTextFont Font;

        // Updated data
        private string Camera;
        private float FieldOfView;

        // Notice data
        private string NoticeText;
        private Point NoticeSize;
        private bool Animation;
        private double AnimationStart = -1;

        public NoticeWindow(WindowManager owner)
            : base(owner, 10, 10, "Field of View")
        {
            Visible = true;
        }

        internal override void ScreenChanged()
        {
            // SizeTo does not clamp the size so we should do it first; MoveTo clamps position.
            SizeTo(Owner.ScreenSize.X, Owner.ScreenSize.Y);
            MoveTo(0, 0);

            Font = Owner.TextManager.GetExact("Arial", (int)(Owner.ScreenSize.Y * NoticeTextSize), System.Drawing.FontStyle.Regular);

            base.ScreenChanged();
        }

        public override bool Interactive
        {
            get
            {
                return false;
            }
        }

        public override bool TopMost
        {
            get
            {
                return true;
            }
        }

        public override void PrepareFrame(in ElapsedTime elapsedTime, bool updateFull)
        {
            base.PrepareFrame(elapsedTime, updateFull);

            if (Animation && Owner.Viewer.RealTime > AnimationStart + AnimationLength)
                Animation = false;

            if (Owner.Viewer.RealTime > 0.1)
            {
                // TODO: Casting to MSTSLocomotive here suggests a problem with the data model in the Simulator.
                var playerLocomotive = Owner.Viewer.PlayerLocomotive as MSTSLocomotive;
                if (playerLocomotive != null && playerLocomotive.Train != null && playerLocomotive.OdometerVisible)
                {
                    SetNotice(Viewer.Catalog.GetString("Odometer {0}", FormatStrings.FormatShortDistanceDisplay(playerLocomotive.OdometerM, Owner.Viewer.MilepostUnitsMetric)));
                }
                // Camera notices are temporary so we put them after to override.
                if (Owner.Viewer.Camera != null)
                {
                    if (Owner.Viewer.Camera.Name != Camera)
                    {
                        Camera = Owner.Viewer.Camera.Name;
                        // Changing camera should not notify FOV change.
                        FieldOfView = Owner.Viewer.Camera.FieldOfView;
 //                       SetNotice(Viewer.Catalog.GetStringFmt("Camera: {0}", Camera));
                    }
                    else if (FieldOfView != Owner.Viewer.Camera.FieldOfView)
                    {
                        FieldOfView = Owner.Viewer.Camera.FieldOfView;
                        SetNotice(Viewer.Catalog.GetString("FOV: {0:F0}°", FieldOfView));
                    }
                }
            }
        }

        private void SetNotice(string noticeText)
        {
            NoticeText = noticeText;
            NoticeSize = new Point(Font.MeasureString(noticeText), Font.Height);
            if (Animation)
            {
                AnimationStart = Owner.Viewer.RealTime - AnimationFade;
            }
            else
            {
                Animation = true;
                AnimationStart = Owner.Viewer.RealTime;
            }
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (Animation)
            {
                var currentLength = Owner.Viewer.RealTime - AnimationStart;
                if (currentLength > AnimationLength)
                    currentLength = AnimationLength;

                var fade = 1.0f;
                if (currentLength < AnimationFade)
                    fade = (float)(currentLength / AnimationFade);
                else if (currentLength > AnimationLength - AnimationFade)
                    fade = (float)((currentLength - AnimationLength) / -AnimationFade);

                var color = new Color(Color.White.R / 255f, Color.White.G / 255f, Color.White.B / 255f, fade);
                var rectangle = new Rectangle((int)(Location.Width * LocationX), (int)(Location.Height * LocationY), 0, 0);
                rectangle.Inflate(NoticeSize.X / 2 + (int)(NoticeSize.Y * PaddingX), NoticeSize.Y / 2 + (int)(NoticeSize.Y * PaddingY));
                spriteBatch.Draw(WindowManager.NoticeTexture, rectangle, color);
                rectangle.Inflate(-(int)(NoticeSize.Y * PaddingX), -(int)(NoticeSize.Y * PaddingY));
                Font.Draw(spriteBatch, rectangle, Point.Zero, NoticeText, LabelAlignment.Center, color);
            }
        }
    }
}
