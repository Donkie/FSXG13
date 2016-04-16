using System;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;
using FSXG13.Helpers;
using Microsoft.FlightSimulator.SimConnect;
using Timer = System.Timers.Timer;

namespace FSXG13
{
    public enum DEFINITIONS : uint
    {
        Struct1
    }

    public enum DATA_REQUESTS : uint
    {
        REQUEST_1
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct Struct1
    {
        public int airspeed;
        public float verticalspeed;
        public int altitude;
        public bool stall;

        public float heading;
        public float pitch;
        public float roll;

        public float trimRudder;
        public float trimAileron;
        public float trimElevator;

        public int flaps;

        public bool apMaster;
        public bool apHeading;
        public int apHeadingLock;
        public bool apAltitude;
        public int apAltitudeLock;
        public int apAltitudeSpeedLock;
        public bool apSpeed;
        public int apSpeedLock;
    }

    internal class Program
    {
        // ReSharper disable once InconsistentNaming
        public const int WM_USER_SIMCONNECT = 0x0402;

        public const float Rad2Deg = (float) (180d/Math.PI);
        public const float Deg2Rad = (float) (Math.PI/180d);

        private const double LineLength = 20000d;

        private const float AhPitchmul = 1.2f;

        private const int AltimeterWidth = 44;
        private static int connection = DMcLgLCD.LGLCD_INVALID_CONNECTION;
        private static int device = DMcLgLCD.LGLCD_INVALID_DEVICE;
        private static int deviceType = DMcLgLCD.LGLCD_INVALID_DEVICE;

        private static Timer drawTimer;
        private static Timer stallTimer;
        private static uint buttons = 0;
        private static int config = 0;

        private static Bitmap LCD;
        public static bool Exit = false;
        private static bool _gameExit;

        private static int updateI;

        private static float planePitch;
        private static float planeRoll;
        private static int planeSpeed;
        private static int planeVertSpeed;
        private static int planeAltitude;
        private static bool planeStall;

        private static readonly Font Ft = new Font("Pixel Millennium", 6, FontStyle.Regular);
        private static readonly Font F = new Font("Pixelmix", 6, FontStyle.Regular);
        private static Brush B = new SolidBrush(Color.Black);
        private static Pen Pen = new Pen(B);

        private static readonly int[] SpdTicks = {0, 1000, 2000, 3000, 4000};
        public static IntPtr Handle { get; set; }

        private static void InitLCD()
        {
            if (DMcLgLCD.LcdInit() != DMcLgLCD.ERROR_SUCCESS)
                return;

            connection = DMcLgLCD.LcdConnectEx("FSX", 0, 0);

            if (connection == DMcLgLCD.LGLCD_INVALID_CONNECTION)
                return;

            device = DMcLgLCD.LcdOpenByType(connection, DMcLgLCD.LGLCD_DEVICE_QVGA);

            if (device == DMcLgLCD.LGLCD_INVALID_DEVICE)
            {
                device = DMcLgLCD.LcdOpenByType(connection, DMcLgLCD.LGLCD_DEVICE_BW);
                if (DMcLgLCD.LGLCD_INVALID_DEVICE != device)
                {
                    deviceType = DMcLgLCD.LGLCD_DEVICE_BW;
                }
            }
            else
            {
                deviceType = DMcLgLCD.LGLCD_DEVICE_QVGA;
            }

            if (deviceType == DMcLgLCD.LGLCD_DEVICE_BW)
            {
                LCD = new Bitmap(160, 43);
                var g = Graphics.FromImage(LCD);
                g.Clear(Color.White);
                g.Dispose();

                DMcLgLCD.LcdUpdateBitmap(device, LCD.GetHbitmap(), DMcLgLCD.LGLCD_DEVICE_BW);
                DMcLgLCD.LcdSetAsLCDForegroundApp(device, DMcLgLCD.LGLCD_FORE_YES);
            }
            else
            {
                LCD = new Bitmap(320, 240);
                var g = Graphics.FromImage(LCD);
                g.Clear(Color.White);
                g.Dispose();

                DMcLgLCD.LcdUpdateBitmap(device, LCD.GetHbitmap(), DMcLgLCD.LGLCD_DEVICE_QVGA);
                DMcLgLCD.LcdSetAsLCDForegroundApp(device, DMcLgLCD.LGLCD_FORE_YES);
            }

            if (deviceType <= 0)
                return;

            //The fastest you should send updates to the LCD is around 30fps or 34ms.  100ms is probably a good typical update speed.
            drawTimer = new Timer(40);
            drawTimer.Elapsed += TimerOnElapsed;
            drawTimer.Enabled = true;
        }

        private static void TimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            try
            {
                var g = Graphics.FromImage(LCD);
                g.Clear(planeStall ? Color.Black : Color.White);
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixel;

                DrawScreen(g, 160, 43);

                DMcLgLCD.LcdUpdateBitmap(device, LCD.GetHbitmap(), DMcLgLCD.LGLCD_DEVICE_BW);

                g.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        private static void AddToDataDefinition(SimConnect con, string name, string type, SIMCONNECT_DATATYPE datatype)
        {
            con.AddToDataDefinition(DEFINITIONS.Struct1, name, type, datatype, 0, SimConnect.SIMCONNECT_UNUSED);
        }

        private static void Main(string[] args)
        {
            InitLCD();

            SimConnect simConnect;

            StartSim:
            _gameExit = false;

            Console.Write("Starting up SimConnect... : ");
            try
            {
                simConnect = new SimConnect("Managed Data Request", Handle, WM_USER_SIMCONNECT, null, 0);
            }
            catch (COMException)
            {
                Console.WriteLine("Simconnect COM not found. Retrying in 2 seconds...");
                Thread.Sleep(2000);
                goto StartSim;
            }
            catch (Exception e)
            {
                Console.WriteLine("Couldn't initialize communications using simconnect. " + e.Message);
                Console.Read();
                return;
            }
            Console.WriteLine("Success!");

            simConnect.OnRecvOpen += OnRecvOpen;
            simConnect.OnRecvQuit += OnRecvQuit;

            AddToDataDefinition(simConnect, "AIRSPEED INDICATED", "Knots", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "VERTICAL SPEED", "Knots", SIMCONNECT_DATATYPE.FLOAT32);
            AddToDataDefinition(simConnect, "INDICATED ALTITUDE", "Feet", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "STALL WARNING", "Bool", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "HEADING INDICATOR", "Radians", SIMCONNECT_DATATYPE.FLOAT32);
            AddToDataDefinition(simConnect, "PLANE PITCH DEGREES", "Radians", SIMCONNECT_DATATYPE.FLOAT32);
            AddToDataDefinition(simConnect, "PLANE BANK DEGREES", "Radians", SIMCONNECT_DATATYPE.FLOAT32);
            AddToDataDefinition(simConnect, "RUDDER TRIM", "Radians", SIMCONNECT_DATATYPE.FLOAT32);
            AddToDataDefinition(simConnect, "AILERON TRIM", "Radians", SIMCONNECT_DATATYPE.FLOAT32);
            AddToDataDefinition(simConnect, "ELEVATOR TRIM POSITION", "Radians", SIMCONNECT_DATATYPE.FLOAT32);
            AddToDataDefinition(simConnect, "FLAPS HANDLE PERCENT", "Percent", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "AUTOPILOT MASTER", "Bool", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "AUTOPILOT HEADING LOCK", "Bool", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "AUTOPILOT HEADING LOCK DIR", "Degrees", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "AUTOPILOT ALTITUDE LOCK", "Bool", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "AUTOPILOT ALTITUDE LOCK VAR", "Feet", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "AUTOPILOT VERTICAL HOLD VAR ", "Feet/minute", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "AUTOPILOT AIRSPEED HOLD", "Bool", SIMCONNECT_DATATYPE.INT32);
            AddToDataDefinition(simConnect, "AUTOPILOT AIRSPEED HOLD VAR", "Knots", SIMCONNECT_DATATYPE.INT32);
            
            simConnect.RegisterDataDefineStruct<Struct1>(DEFINITIONS.Struct1);

            simConnect.OnRecvSimobjectData += OnRecvSimobjectData;
            simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_1, DEFINITIONS.Struct1,
                SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.VISUAL_FRAME, 0, 0, 0, 0);

            //Main loop
            while (!Exit)
            {
                try
                {
                    simConnect.ReceiveMessage();
                }
                catch (COMException)
                {
                    Console.WriteLine("Game Shutdown");
                    goto StartSim;
                }
                Thread.Sleep(40);
                if (_gameExit)
                    goto StartSim;
            }

            simConnect.Dispose();

            Console.WriteLine("Press the Enter key to exit the program... ");
            Console.ReadLine();
            Console.WriteLine("Terminating the application...");
        }

        private static void OnData(Struct1 s)
        {
            updateI++;

            planePitch = -s.pitch*Rad2Deg;
            planeRoll = -s.roll*Rad2Deg;

            //Update every third draw (120ms)
            if (updateI%3 == 0)
            {
                planeSpeed = s.airspeed;
                planeVertSpeed = (int) (s.verticalspeed*100f);
                planeAltitude = s.altitude;
            }

            //Update every tenth draw (400ms)
            if (updateI%10 == 0)
            {
                planeStall = s.stall;
            }
        }

        private static void DrawLineCenter(Graphics g, int x, int y, float rot, double len = LineLength)
        {
            var sin = Math.Sin(rot*Deg2Rad);
            var cos = Math.Cos(rot*Deg2Rad);

            var x1 = (int) Math.Round(-cos*len) + x;
            var y1 = (int) Math.Round(-sin*len) + y;
            var x2 = (int) Math.Round(cos*len) + x;
            var y2 = (int) Math.Round(sin*len) + y;

            g.DrawLine(Pen, x1, y1, x2, y2);
        }

        private static void DrawArtificialHorizon(Graphics g, int x, int y, int w, int h)
        {
            g.Clip = new Region(new Rectangle(x, y, w, h));

            var cX = x + (int) (w/2f);
            var cY = y + (int) (h/2f);

            for (var curP = -90f; curP <= 90f; curP += 10f)
            {
                var pdiff = planePitch - curP;
                var lineX = (int) Math.Round(Math.Sin(planeRoll*Deg2Rad)*AhPitchmul*pdiff) + cX;
                var lineY = (int) Math.Round(Math.Cos(planeRoll*Deg2Rad)*AhPitchmul*pdiff) + cY;

                DrawLineCenter(g, lineX, lineY, -planeRoll, Math.Abs(curP)/3f + 2f);
            }

            g.DrawLine(Pen, x, y, x, y + h);
            g.DrawLine(Pen, cX - 10, cY, cX - 2, cY);
            g.DrawLine(Pen, cX + 2, cY, cX + 10, cY);
            g.DrawLine(Pen, cX, cY + 2, cX, cY + 5);

            g.ResetClip();
        }

        private static void DrawAltimeter(Graphics g, int x, int y)
        {
            g.DrawLine(Pen, x - 1, y - 1, x + AltimeterWidth, y - 1);
            g.DrawLine(Pen, x + AltimeterWidth, y - 1, x + AltimeterWidth, y + 10);
            g.DrawLine(Pen, x + AltimeterWidth, y + 10, x - 1, y + 10);
            g.DrawLine(Pen, x - 1, y + 10, x - 1, y - 1);

            g.Clip = new Region(new Rectangle(x, y, AltimeterWidth, 10));

            g.DrawString("ft", F, B, x + 32, y + 1);

            double prevmul = 0;
            var j = 0;
            for (var i = 1; i <= 1000; i *= 10)
            {
                j++;

                var curDecimals = (int) Math.Floor((float) Math.Abs(planeAltitude)/i);
                var nextDecimals = curDecimals + 1;

                if (i < 1000)
                {
                    curDecimals %= 10;
                    nextDecimals %= 10;
                }
                else
                {
                    if (curDecimals >= 10)
                        j++;
                    prevmul *= prevmul;
                }

                prevmul *= prevmul;

                g.DrawString(curDecimals.ToString(CultureInfo.InvariantCulture), F, B, x + AltimeterWidth - j*6 - 14,
                    y + (int) (prevmul*10d));
                g.DrawString(nextDecimals.ToString(CultureInfo.InvariantCulture), F, B, x + AltimeterWidth - j*6 - 14,
                    y - (int) ((1 - prevmul)*10d));

                prevmul = curDecimals/10f + prevmul/10f;
            }

            g.ResetClip();
        }

        /// <summary>
        ///     Converts a vertical speed (knots) to a suitable multiplier
        /// </summary>
        /// <param name="spd">Input speed in knots</param>
        /// <returns>A multiplier between -1 and 1</returns>
        private static double SpdMul(double spd)
        {
            return Math.Max(Math.Min(
                -7.072623771*Math.Pow(10, -12)*Math.Pow(spd, 3)
                + 6.070939301*Math.Pow(10, -24)*Math.Pow(spd, 2)
                + 3.614714508*Math.Pow(10, -4)*spd
                , 1), -1);
        }

        private static void DrawVerticalSpeed(Graphics g, int x, int y, int h)
        {
            //Speed text
            var spd = Math.Floor(planeVertSpeed/100f)/10f;
            var str = "";
            if (Math.Abs(spd - Math.Floor(spd)) < 0.00001) // If we don't have any decimals
            {
                str = ".0";
            }
            str = spd.ToString(CultureInfo.CreateSpecificCulture("en-US")) + str;

            var strSize = g.MeasureString(str, Ft);
            g.DrawString(str, Ft, B, x - strSize.Width + 6, y);

            //The dial
            var mul = SpdMul(planeVertSpeed);

            h -= 10;
            y += 8;

            var cy = y + h/2;

            foreach (var tick in SpdTicks)
            {
                var m = SpdMul(tick);
                var offsetY = (int) Math.Round(h/2d*m);

                if (tick != 0)
                    g.DrawLine(Pen, x, cy - offsetY, x + 1, cy - offsetY);

                var lineLen = 1;
                if (tick == 0)
                    lineLen = 2;

                g.DrawLine(Pen, x, cy + offsetY, x + lineLen, cy + offsetY);
            }

            g.DrawLine(Pen, x, cy - (int) Math.Round(h/2d*mul), x + 40, cy);
        }

        private static void DrawSpeedometer(Graphics g, int x, int y)
        {
            var speed = planeSpeed;
            var dec1 = speed%10;
            var dec2 = (int) Math.Floor(speed/10f)%10;
            var dec3 = (int) Math.Floor(speed/100f)%10;

            g.DrawString(dec3.ToString(), F, B, x, y);
            g.DrawString(dec2.ToString(), F, B, x + 6, y);
            g.DrawString(dec1.ToString(), F, B, x + 12, y);
            g.DrawString("kts", F, B, x + 21, y);
        }

        private static void DrawScreen(Graphics g, int w, int h)
        {
            B = new SolidBrush(planeStall ? Color.White : Color.Black);
            Pen = new Pen(B);

            DrawArtificialHorizon(g, w - h - 6, 0, h, h);
            DrawAltimeter(g, w - h - 6 - AltimeterWidth, h - 10);
            DrawSpeedometer(g, w - h - 6 - AltimeterWidth + 6, h - 21);
            DrawVerticalSpeed(g, w - 6, 0, h);
        }

        private static void OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if ((DATA_REQUESTS) data.dwRequestID != DATA_REQUESTS.REQUEST_1)
                return;

            var s1 = (Struct1) data.dwData[0];
            OnData(s1);
        }

        private static void OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
        }

        private static void OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            _gameExit = true;
        }
    }
}