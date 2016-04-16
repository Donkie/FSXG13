using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FSXG13.Helpers
{
    public enum LCDColorMode
    {
        Monochrome,
        Color
    }

    public class LCD
    {
        public int Width { get; }
        public int Height { get; }
        public LCDColorMode ColorMode { get; }
        public bool HasDevice { get; }

        public bool ForceForeground
        {
            get { return _forceForeground; }
            set
            {
                _forceForeground = value;

                if (value && device != DMcLgLCD.LGLCD_INVALID_DEVICE)
                    DMcLgLCD.LcdSetAsLCDForegroundApp(device, DMcLgLCD.LGLCD_FORE_YES);
            }
        }

        private readonly int connection = DMcLgLCD.LGLCD_INVALID_CONNECTION;
        private int device = DMcLgLCD.LGLCD_INVALID_DEVICE;
        private int deviceType = DMcLgLCD.LGLCD_INVALID_DEVICE;
        private Bitmap bitmap;
        private Graphics g;
        private bool _forceForeground;

        public LCD(string appName, int isPersistent = 0, int isAutoStartable = 0)
        {
            //LCD Init
            var initCode = DMcLgLCD.LcdInit();
            if (initCode != DMcLgLCD.ERROR_SUCCESS)
                return;
            
            //Connection
            connection = DMcLgLCD.LcdConnectEx(appName, isPersistent, isAutoStartable);
            if (connection == DMcLgLCD.LGLCD_INVALID_CONNECTION)
                return;
            
            //Device
            device = DMcLgLCD.LcdOpenByType(connection, DMcLgLCD.LGLCD_DEVICE_QVGA);
            if (device != DMcLgLCD.LGLCD_INVALID_DEVICE)
            {
                HasDevice = true;
                ColorMode = LCDColorMode.Color;
                Width = 320;
                Height = 240;
                deviceType = DMcLgLCD.LGLCD_DEVICE_QVGA;
            }
            else
            {
                device = DMcLgLCD.LcdOpenByType(connection, DMcLgLCD.LGLCD_DEVICE_QVGA);
                if (device != DMcLgLCD.LGLCD_INVALID_DEVICE)
                {
                    HasDevice = true;
                    ColorMode = LCDColorMode.Monochrome;
                    Width = 160;
                    Height = 43;
                    deviceType = DMcLgLCD.LGLCD_DEVICE_BW;
                }
                else
                {
                    HasDevice = false;
                    ColorMode = LCDColorMode.Monochrome;
                    Width = 160;
                    Height = 43;
                }
            }

            //Bitmap init
            bitmap = new Bitmap(Width, Height);
            g = Graphics.FromImage(bitmap);

            Clear();

            ForceForeground = true;
        }

        public Graphics GetGraphics()
        {
            return g;
        }

        public Bitmap GetBitmap()
        {
            return bitmap;
        }

        public void Clear(Color c)
        {
            g.Clear(c);
        }

        public void Clear()
        {
            Clear(Color.White);
        }

        public void Draw()
        {
            if (device == DMcLgLCD.LGLCD_INVALID_DEVICE)
                return;

            DMcLgLCD.LcdUpdateBitmap(device, bitmap.GetHbitmap(), DMcLgLCD.LGLCD_DEVICE_BW);
        }
    }
}
