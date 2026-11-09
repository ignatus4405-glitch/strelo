using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace StreloReminder.Services
{
    public static class IconGenerator
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private static Icon _cachedActiveIcon;
        private static Icon _cachedPausedIcon;

        public static Icon GetAppIcon(bool isPaused = false)
        {
            if (isPaused)
            {
                if (_cachedPausedIcon == null)
                {
                    _cachedPausedIcon = CreateBellIcon(Color.FromArgb(120, 130, 145), Color.FromArgb(230, 140, 40));
                }
                return _cachedPausedIcon;
            }

            if (_cachedActiveIcon == null)
            {
                _cachedActiveIcon = CreateBellIcon(Color.FromArgb(37, 99, 235), Color.FromArgb(245, 158, 11));
            }
            return _cachedActiveIcon;
        }

        private static Icon CreateBellIcon(Color bgColor, Color accentColor)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    // Круглый фон
                    using (SolidBrush bgBrush = new SolidBrush(bgColor))
                    {
                        g.FillEllipse(bgBrush, 1, 1, 29, 29);
                    }

                    // Купол колокольчика
                    using (SolidBrush bellBrush = new SolidBrush(Color.White))
                    {
                        g.FillPie(bellBrush, 8, 7, 16, 16, 180, 180);
                        g.FillRectangle(bellBrush, 8, 14, 16, 7);
                        g.FillRectangle(bellBrush, 6, 20, 20, 3);
                    }

                    // Язычок колокольчика
                    using (SolidBrush clapperBrush = new SolidBrush(accentColor))
                    {
                        g.FillEllipse(clapperBrush, 13, 22, 6, 5);
                        g.FillEllipse(clapperBrush, 14, 5, 4, 4);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon tempIcon = Icon.FromHandle(hIcon))
                    {
                        return (Icon)tempIcon.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
    }
}
