using System;
using System.Globalization;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.View.Track;
using Orts.View.Xna;

namespace Orts.View.DrawableComponents
{
    public enum TimeType
    {
        GameTotalTime,
        GameElapsedTime,
        RealWorlUtcTime,
        RealWorldLocalTime,
    }

    /// <summary>
    /// this class is primarily used for testing of various graphical aspects
    /// </summary>
    public class DigitalClockComponent : QuickRepeatableDrawableTextComponent
    {

        private readonly TimeType timeType;
        private string formatMask = "hh\\:mm\\:ss";
        private string previousTimestamp;

        public DigitalClockComponent(Game game, TimeType timeType, System.Drawing.Font font, Color color, Vector2 position, bool visibleImmediately) :
            base(game, font, color, position)
        {
            this.timeType = timeType;
            if (!visibleImmediately)
            {
                Enabled = false;
                Visible = false;
            }
            InitializeSize(TimeSpan.Zero.ToString(formatMask, CultureInfo.DefaultThreadCurrentUICulture));
            Window_ClientSizeChanged(this, EventArgs.Empty);
        }

        public string FormatMask
        {
            get => formatMask;
            set 
            {
                InitializeSize(TimeSpan.Zero.ToString(value, CultureInfo.DefaultThreadCurrentUICulture));
                formatMask = value;
            }
        }

        public override void Update(GameTime gameTime)
        {
            string timestamp = null;
            switch (timeType)
            {
                case TimeType.GameElapsedTime:
                    timestamp = gameTime?.ElapsedGameTime.ToString(formatMask, CultureInfo.DefaultThreadCurrentUICulture); break;
                case TimeType.GameTotalTime:
                    timestamp = gameTime?.TotalGameTime.ToString(formatMask, CultureInfo.DefaultThreadCurrentUICulture); break;
                case TimeType.RealWorlUtcTime:
                    timestamp = DateTime.UtcNow.TimeOfDay.ToString(formatMask, CultureInfo.DefaultThreadCurrentUICulture); break;
                case TimeType.RealWorldLocalTime:
                    timestamp = DateTime.Now.TimeOfDay.ToString(formatMask, CultureInfo.DefaultThreadCurrentUICulture); break;
            }
            if (timestamp != previousTimestamp)
            {
                DrawString(timestamp);
                previousTimestamp = timestamp;
            }
            base.Update(gameTime);
        }

        internal protected override void Enable(ContentArea content)
        {
            InitializeSize(TimeSpan.Zero.ToString(formatMask, CultureInfo.DefaultThreadCurrentUICulture));
            base.Enable(content);
        }

        public override void Draw(GameTime gameTime)
        {
            if (null == texture)
                return;
            spriteBatch.Begin();
            spriteBatch.Draw(texture, position, null, color, 0, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);

            base.Draw(gameTime);
            spriteBatch.End();
        }
    }
}
