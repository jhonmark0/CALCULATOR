using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Calculator")]
[assembly: AssemblyProduct("Calculator")]
[assembly: AssemblyDescription("Calculator for Windows")]
[assembly: AssemblyCompany("Calculator")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace AppleCalculator
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CalculatorForm());
        }
    }

    internal sealed class CalculatorForm : Form
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int WM_GETMINMAXINFO = 0x24;
        private const int WM_NCHITTEST = 0x84;

        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT Reserved;
            public POINT MaxSize;
            public POINT MaxPosition;
            public POINT MinTrackSize;
            public POINT MaxTrackSize;
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private bool nativeRoundCorners;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly string DisplayFamily = PickFamily(
            "Segoe UI Light", "Segoe UI Variable Display Light", "Segoe UI", "Arial");
        private static readonly string TextFamily = PickFamily(
            "Segoe UI", "Segoe UI Variable Text", "Arial");

        private sealed class CalcKey
        {
            public string Label;
            public string Kind;
            public string Digit;
            public string Op;
            public string Action;
            public RectangleF Rect;
            public bool Hover;
            public bool Down;
            public bool Flash;
            public bool Selected;

            public CalcKey(string label, string kind, RectangleF rect)
            {
                Label = label;
                Kind = kind;
                Rect = rect;
            }
        }

        private sealed class CalcState
        {
            public string Entry = "0";
            public double? Acc;
            public string Op;
            public bool Awaiting;
            public bool StartNew = true;
            public string LastOp;
            public double LastRhs;
            public bool Error;
            public string Expr = "";
        }

        private readonly CalcState state = new CalcState();
        private readonly List<CalcKey> keys = new List<CalcKey>();
        private readonly Dictionary<int, Font> valueFonts = new Dictionary<int, Font>();
        private readonly Timer flashTimer = new Timer();
        private readonly RectangleF[] lightRects = new RectangleF[3];

        private float dpi = 1f;
        private float keyGap;
        private float keySize;
        private float padLeft;
        private float keysTop;
        private Font digitFont;
        private Font fnFont;
        private Font opFont;
        private Font exprFont;
        private int fontDigitPx = -1;
        private float fontDpi = -1f;

        private CalcKey pressedKey;
        private CalcKey flashKey;
        private int pressedLight = -1;
        private int lightHover = -1;
        private bool lightsHover;

        public CalculatorForm()
        {
            Text = "Calculator";
            using (System.IO.Stream s = typeof(CalculatorForm).Assembly.GetManifestResourceStream("Calculator.ico"))
            {
                if (s != null) Icon = new Icon(s);
            }
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.Black;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw, true);

            float screenDpi;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                screenDpi = g.DpiX;
            dpi = screenDpi / 96f;

            MinimumSize = new Size(
                (int)Math.Round(300f * dpi),
                (int)Math.Round(500f * dpi));

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int width = (int)Math.Round(420f * dpi);
            int height = (int)Math.Round(720f * dpi);
            width = Math.Min(width, (int)(wa.Width * 0.92f));
            height = Math.Min(height, (int)(wa.Height * 0.92f));
            ClientSize = new Size(
                Math.Max(width, MinimumSize.Width),
                Math.Max(height, MinimumSize.Height));

            flashTimer.Interval = 120;
            flashTimer.Tick += FlashTimerTick;

            BuildLayout();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            float newDpi = e.DeviceDpiNew / 96f;
            float ratio = newDpi / dpi;
            dpi = newDpi;

            MinimumSize = new Size(
                (int)Math.Round(300f * dpi),
                (int)Math.Round(500f * dpi));

            if (WindowState == FormWindowState.Normal)
            {
                ClientSize = new Size(
                    Math.Max(MinimumSize.Width, (int)Math.Round(ClientSize.Width * ratio)),
                    Math.Max(MinimumSize.Height, (int)Math.Round(ClientSize.Height * ratio)));
            }

            BuildLayout();
            Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int preference = DWMWCP_ROUND;
            try
            {
                nativeRoundCorners = DwmSetWindowAttribute(
                    Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) == 0;
            }
            catch (DllNotFoundException)
            {
                nativeRoundCorners = false;
            }
            catch (EntryPointNotFoundException)
            {
                nativeRoundCorners = false;
            }
            ApplyWindowShape();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (m.Result.ToInt32() == HTCLIENT && WindowState == FormWindowState.Normal)
                {
                    int lp = m.LParam.ToInt32();
                    int sx = (short)(lp & 0xFFFF);
                    int sy = (short)((lp >> 16) & 0xFFFF);
                    Point p = PointToClient(new Point(sx, sy));
                    int edge = (int)Math.Round(5f * dpi);

                    bool left = p.X < edge;
                    bool right = p.X >= ClientSize.Width - edge;
                    bool top = p.Y < edge;
                    bool bottom = p.Y >= ClientSize.Height - edge;

                    if (top && left) m.Result = (IntPtr)HTTOPLEFT;
                    else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (bottom && right) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (left) m.Result = (IntPtr)HTLEFT;
                    else if (right) m.Result = (IntPtr)HTRIGHT;
                    else if (top) m.Result = (IntPtr)HTTOP;
                    else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                }
                return;
            }

            if (m.Msg == WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                Screen screen = Screen.FromHandle(Handle);
                Rectangle wa = screen.WorkingArea;
                Rectangle bounds = screen.Bounds;

                MINMAXINFO info = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                info.MaxPosition.X = wa.Left - bounds.Left;
                info.MaxPosition.Y = wa.Top - bounds.Top;
                info.MaxSize.X = wa.Width;
                info.MaxSize.Y = wa.Height;
                info.MinTrackSize.X = MinimumSize.Width;
                info.MinTrackSize.Y = MinimumSize.Height;
                Marshal.StructureToPtr(info, m.LParam, false);
                return;
            }

            base.WndProc(ref m);
        }

        private static string PickFamily(params string[] names)
        {
            using (InstalledFontCollection fonts = new InstalledFontCollection())
            {
                foreach (string name in names)
                {
                    foreach (FontFamily family in fonts.Families)
                    {
                        if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                            return family.Name;
                    }
                }
            }
            return "Arial";
        }

        private static Font CreateFont(string family, float pixels, FontStyle style)
        {
            try
            {
                return new Font(family, pixels, style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font("Arial", pixels, style, GraphicsUnit.Pixel);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Width > 0 && ClientSize.Height > 0)
                BuildLayout();
        }

        private void BuildLayout()
        {
            float w = ClientSize.Width;
            float h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            float padSide = 18f * dpi;
            float padBottom = 20f * dpi;
            keyGap = Math.Max(8f * dpi, Math.Min(14f * dpi, w * 0.031f));

            float displayMin = 118f * dpi;
            float maxPadWidth = 520f * dpi;
            float usableWidth = Math.Min(w - padSide * 2f, maxPadWidth);

            float keyFromWidth = (usableWidth - keyGap * 3f) / 4f;
            float keyFromHeight = (h - padBottom - displayMin - keyGap * 4f) / 5f;
            keySize = Math.Min(keyFromWidth, keyFromHeight);
            if (keySize < 34f * dpi) keySize = 34f * dpi;

            float padWidth = keySize * 4f + keyGap * 3f;
            padLeft = (w - padWidth) / 2f;
            keysTop = h - padBottom - (keySize * 5f + keyGap * 4f);

            int digitPx = (int)Math.Round(keySize * 0.383f);
            if (digitFont == null || digitPx != fontDigitPx || Math.Abs(dpi - fontDpi) > 0.001f)
            {
                DisposeFonts();
                digitFont = CreateFont(TextFamily, digitPx, FontStyle.Regular);
                fnFont = CreateFont(TextFamily, (int)Math.Round(keySize * 0.312f), FontStyle.Regular);
                opFont = CreateFont(TextFamily, (int)Math.Round(keySize * 0.44f), FontStyle.Regular);
                exprFont = CreateFont(TextFamily,
                    (int)Math.Round(Math.Max(12f * dpi, keySize * 0.21f)), FontStyle.Regular);
                fontDigitPx = digitPx;
                fontDpi = dpi;
            }

            float lightD = 12f * dpi;
            float lightX = 20f * dpi;
            float lightY = 18f * dpi;
            float lightGap = 8f * dpi;
            for (int i = 0; i < 3; i++)
                lightRects[i] = new RectangleF(lightX + i * (lightD + lightGap), lightY, lightD, lightD);

            keys.Clear();
            AddKey("AC", "fn", null, null, "clear", 0, 0, 1);
            AddKey("±", "fn", null, null, "sign", 1, 0, 1);
            AddKey("%", "fn", null, null, "percent", 2, 0, 1);
            AddKey("÷", "op", null, "÷", null, 3, 0, 1);

            AddKey("7", "digit", "7", null, null, 0, 1, 1);
            AddKey("8", "digit", "8", null, null, 1, 1, 1);
            AddKey("9", "digit", "9", null, null, 2, 1, 1);
            AddKey("×", "op", null, "×", null, 3, 1, 1);

            AddKey("4", "digit", "4", null, null, 0, 2, 1);
            AddKey("5", "digit", "5", null, null, 1, 2, 1);
            AddKey("6", "digit", "6", null, null, 2, 2, 1);
            AddKey("−", "op", null, "−", null, 3, 2, 1);

            AddKey("1", "digit", "1", null, null, 0, 3, 1);
            AddKey("2", "digit", "2", null, null, 1, 3, 1);
            AddKey("3", "digit", "3", null, null, 2, 3, 1);
            AddKey("+", "op", null, "+", null, 3, 3, 1);

            AddKey("0", "digit", "0", null, null, 0, 4, 2);
            AddKey(".", "digit", null, null, "dot", 2, 4, 1);
            AddKey("=", "op", null, null, "equals", 3, 4, 1);

            ApplyWindowShape();
            Invalidate();
        }

        private void AddKey(string label, string kind, string digit, string op,
            string action, int col, int row, int span)
        {
            float x = padLeft + col * (keySize + keyGap);
            float y = keysTop + row * (keySize + keyGap);
            float wSize = span * keySize + (span - 1) * keyGap;
            CalcKey key = new CalcKey(label, kind, new RectangleF(x, y, wSize, keySize));
            key.Digit = digit;
            key.Op = op;
            key.Action = action;
            keys.Add(key);
        }

        private const float WindowRadius = 18f;

        private void ApplyWindowShape()
        {
            Region previous = Region;
            if (WindowState == FormWindowState.Maximized || nativeRoundCorners)
            {
                Region = null;
            }
            else
            {
                using (GraphicsPath path = RoundedRect(
                    new RectangleF(0f, 0f, ClientSize.Width, ClientSize.Height), WindowRadius * dpi))
                    Region = new Region(path);
            }
            if (previous != null) previous.Dispose();
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        private Font GetValueFont(int pixels)
        {
            if (pixels < 10) pixels = 10;
            Font font;
            if (!valueFonts.TryGetValue(pixels, out font))
            {
                font = CreateFont(DisplayFamily, pixels, FontStyle.Regular);
                valueFonts[pixels] = font;
            }
            return font;
        }

        private Font PickValueFont(string text, float availableWidth, float availableHeight)
        {
            float[] ratios = { 1.02f, 0.92f, 0.81f, 0.72f, 0.65f, 0.58f, 0.51f, 0.45f, 0.39f, 0.35f };
            int start = 0;
            int len = text.Length;
            if (len > 15) start = 8;
            else if (len > 11) start = 6;
            else if (len > 8) start = 4;
            else if (len > 5) start = 2;

            Font last = null;
            for (int i = start; i < ratios.Length; i++)
            {
                Font font = GetValueFont((int)Math.Round(keySize * ratios[i]));
                last = font;
                Size size = TextRenderer.MeasureText(text, font, new Size(10000, 1000),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (size.Width <= availableWidth && size.Height <= availableHeight)
                    return font;
            }
            return last;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush background = new SolidBrush(Color.Black))
                g.FillRectangle(background, ClientRectangle);

            SyncClearLabel();
            DrawDisplay(g);
            DrawLights(g);
            for (int i = 0; i < keys.Count; i++)
                DrawKey(g, keys[i]);

            if (WindowState != FormWindowState.Maximized)
            {
                float radius = (nativeRoundCorners ? 8f : WindowRadius) * dpi;
                using (GraphicsPath border = RoundedRect(
                    new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f), radius))
                using (Pen pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
                    g.DrawPath(pen, border);
            }
        }

        private void SyncClearLabel()
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].Action == "clear")
                {
                    keys[i].Label = state.Error || state.StartNew ? "AC" : "C";
                    return;
                }
            }
        }

        private void DrawDisplay(Graphics g)
        {
            string text = state.Error ? "Error" : state.Entry;
            float availableWidth = keySize * 4f + keyGap * 3f;
            int exprHeight = exprFont != null ? exprFont.Height : 0;
            float bottom = keysTop - 14f * dpi;
            float availableHeight = bottom - (exprHeight + 8f * dpi + 46f * dpi);
            if (availableHeight < 24f * dpi) availableHeight = 24f * dpi;

            Font font = PickValueFont(text, availableWidth, availableHeight);
            Size measured = TextRenderer.MeasureText(text, font, new Size(10000, 1000),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            Rectangle valueRect = new Rectangle(
                (int)padLeft, (int)(bottom - measured.Height),
                (int)availableWidth, measured.Height);

            TextRenderer.DrawText(g, text, font, valueRect, Color.White,
                TextFormatFlags.Right | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);

            if (state.Expr.Length > 0)
            {
                Rectangle exprRect = new Rectangle(
                    (int)padLeft,
                    valueRect.Top - (int)(8f * dpi) - exprHeight,
                    (int)availableWidth, exprHeight);
                TextRenderer.DrawText(g, state.Expr, exprFont, exprRect,
                    Color.FromArgb(0x8E, 0x8E, 0x93),
                    TextFormatFlags.Right | TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }
        }

        private void DrawLights(Graphics g)
        {
            Color[] colors =
            {
                Color.FromArgb(0xFF, 0x5F, 0x57),
                Color.FromArgb(0xFE, 0xBC, 0x2E),
                Color.FromArgb(0x28, 0xC8, 0x40),
            };

            for (int i = 0; i < 3; i++)
            {
                int alpha = lightsHover ? 255 : 120;
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, colors[i])))
                    g.FillEllipse(brush, lightRects[i]);

                if (!lightsHover) continue;

                RectangleF r = lightRects[i];
                float cx = r.X + r.Width / 2f;
                float cy = r.Y + r.Height / 2f;
                float half = r.Width * 0.22f;
                Color glyph = i == 0
                    ? Color.FromArgb(0x6B, 0x0F, 0x0A)
                    : i == 1 ? Color.FromArgb(0x9A, 0x62, 0x00)
                    : Color.FromArgb(0x0D, 0x5B, 0x0F);

                if (i == 2)
                {
                    float s = r.Width * 0.15f;
                    using (SolidBrush brush = new SolidBrush(glyph))
                    {
                        g.FillPolygon(brush, new PointF[]
                        {
                            new PointF(cx + s, cy - s),
                            new PointF(cx + s, cy + s * 0.2f),
                            new PointF(cx - s * 0.2f, cy - s),
                        });
                        g.FillPolygon(brush, new PointF[]
                        {
                            new PointF(cx - s, cy + s),
                            new PointF(cx - s, cy - s * 0.2f),
                            new PointF(cx + s * 0.2f, cy + s),
                        });
                    }
                }
                else
                {
                    using (Pen pen = new Pen(glyph, Math.Max(1f, 1.4f * dpi)))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        if (i == 0)
                        {
                            g.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
                            g.DrawLine(pen, cx - half, cy + half, cx + half, cy - half);
                        }
                        else
                        {
                            g.DrawLine(pen, cx - half, cy, cx + half, cy);
                        }
                    }
                }
            }
        }

        private void DrawKey(Graphics g, CalcKey key)
        {
            bool pressed = key.Down || key.Flash;
            Color fill;
            Color text;

            if (key.Kind == "fn")
            {
                fill = pressed ? Color.FromArgb(0xD6, 0xD6, 0xD6)
                    : key.Hover ? Color.FromArgb(0xB3, 0xB3, 0xB3)
                    : Color.FromArgb(0xA5, 0xA5, 0xA5);
                text = Color.Black;
            }
            else if (key.Kind == "op")
            {
                if (key.Selected)
                {
                    fill = pressed ? Color.FromArgb(0xFF, 0xD9, 0xA8)
                        : key.Hover ? Color.FromArgb(0xF2, 0xF2, 0xF2)
                        : Color.White;
                    text = Color.FromArgb(0xFF, 0x9F, 0x0A);
                }
                else
                {
                    fill = pressed ? Color.FromArgb(0xFF, 0xC8, 0x73)
                        : key.Hover ? Color.FromArgb(0xFF, 0xAD, 0x2A)
                        : Color.FromArgb(0xFF, 0x9F, 0x0A);
                    text = Color.White;
                }
            }
            else
            {
                fill = pressed ? Color.FromArgb(0x6E, 0x6E, 0x6E)
                    : key.Hover ? Color.FromArgb(0x3F, 0x3F, 0x3F)
                    : Color.FromArgb(0x33, 0x33, 0x33);
                text = Color.White;
            }

            using (SolidBrush brush = new SolidBrush(fill))
            {
                if (key.Rect.Width > key.Rect.Height + 1f)
                {
                    using (GraphicsPath pill = RoundedRect(key.Rect, key.Rect.Height / 2f))
                        g.FillPath(brush, pill);
                }
                else
                {
                    g.FillEllipse(brush, key.Rect);
                }
            }

            if (key.Label == ".")
            {
                float r = keySize * 0.042f;
                float cx = key.Rect.X + key.Rect.Width / 2f;
                float cy = key.Rect.Y + key.Rect.Height / 2f;
                using (SolidBrush dot = new SolidBrush(text))
                    g.FillEllipse(dot, cx - r, cy - r, r * 2f, r * 2f);
                return;
            }

            Font font = key.Kind == "fn" ? fnFont : key.Kind == "op" ? opFont : digitFont;
            Rectangle rect = Rectangle.Round(key.Rect);
            TextRenderer.DrawText(g, key.Label, font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        private CalcKey KeyAt(Point p)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].Rect.Contains(p)) return keys[i];
            }
            return null;
        }

        private int LightAt(Point p)
        {
            for (int i = 0; i < lightRects.Length; i++)
            {
                RectangleF r = lightRects[i];
                r.Inflate(4f * dpi, 4f * dpi);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int hoverLight = LightAt(e.Location);
            bool hoverGroup = hoverLight >= 0;
            if (hoverLight != lightHover || hoverGroup != lightsHover)
            {
                lightHover = hoverLight;
                lightsHover = hoverGroup;
                Invalidate();
            }

            CalcKey over = KeyAt(e.Location);
            bool changed = false;
            for (int i = 0; i < keys.Count; i++)
            {
                bool hover = keys[i] == over;
                if (keys[i].Hover != hover)
                {
                    keys[i].Hover = hover;
                    changed = true;
                }
            }
            if (changed) Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            lightHover = -1;
            lightsHover = false;
            for (int i = 0; i < keys.Count; i++)
                keys[i].Hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            int light = LightAt(e.Location);
            if (light >= 0)
            {
                pressedLight = light;
                return;
            }

            if (e.Y < keysTop)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                return;
            }

            CalcKey key = KeyAt(e.Location);
            if (key != null)
            {
                pressedKey = key;
                key.Down = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            if (pressedLight >= 0)
            {
                int light = pressedLight;
                pressedLight = -1;
                if (LightAt(e.Location) == light)
                {
                    if (light == 0) Close();
                    else if (light == 1) WindowState = FormWindowState.Minimized;
                    else ToggleMaximize();
                }
                Invalidate();
                return;
            }

            if (pressedKey != null)
            {
                CalcKey key = pressedKey;
                pressedKey = null;
                key.Down = false;
                if (key.Rect.Contains(e.Location)) ClickKey(key);
                else Invalidate();
            }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        private void ClickKey(CalcKey key)
        {
            if (key.Digit != null) InputDigit(key.Digit);
            else if (key.Op != null) InputOperator(key.Op);
            else if (key.Action == "clear") ClearKey();
            else if (key.Action == "sign") ToggleSign();
            else if (key.Action == "percent") InputPercent();
            else if (key.Action == "dot") InputDot();
            else if (key.Action == "equals") InputEquals();

            SyncState();
        }

        private void SyncState()
        {
            for (int i = 0; i < keys.Count; i++)
            {
                CalcKey key = keys[i];
                key.Selected = !state.Error && key.Op != null && key.Op == state.Op;
            }
            Invalidate();
        }

        private CalcKey FindDigit(string digit)
        {
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].Digit == digit) return keys[i];
            return null;
        }

        private CalcKey FindOp(string op)
        {
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].Op == op) return keys[i];
            return null;
        }

        private CalcKey FindAction(string action)
        {
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].Action == action) return keys[i];
            return null;
        }

        private void Flash(CalcKey key)
        {
            if (key == null) return;
            if (flashKey != null) flashKey.Flash = false;
            flashKey = key;
            key.Flash = true;
            flashTimer.Stop();
            flashTimer.Start();
            Invalidate();
        }

        private void FlashTimerTick(object sender, EventArgs e)
        {
            flashTimer.Stop();
            if (flashKey != null)
            {
                flashKey.Flash = false;
                flashKey = null;
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            bool shift = e.Shift;
            Keys code = e.KeyCode;
            CalcKey target = null;

            if (code >= Keys.D0 && code <= Keys.D9)
            {
                int digit = (int)code - (int)Keys.D0;
                if (shift)
                {
                    if (digit != 5) return;
                    InputPercent();
                    target = FindAction("percent");
                }
                else
                {
                    string d = digit.ToString(Inv);
                    InputDigit(d);
                    target = FindDigit(d);
                }
            }
            else if (code >= Keys.NumPad0 && code <= Keys.NumPad9)
            {
                string d = ((int)code - (int)Keys.NumPad0).ToString(Inv);
                InputDigit(d);
                target = FindDigit(d);
            }
            else if (code == Keys.OemPeriod || code == Keys.Decimal)
            {
                InputDot();
                target = FindAction("dot");
            }
            else if (code == Keys.OemMinus || code == Keys.Subtract)
            {
                InputOperator("−");
                target = FindOp("−");
            }
            else if (code == Keys.Oemplus)
            {
                if (shift)
                {
                    InputOperator("+");
                    target = FindOp("+");
                }
                else
                {
                    InputEquals();
                    target = FindAction("equals");
                }
            }
            else if (code == Keys.Add)
            {
                InputOperator("+");
                target = FindOp("+");
            }
            else if (code == Keys.Multiply)
            {
                InputOperator("×");
                target = FindOp("×");
            }
            else if (code == Keys.Divide)
            {
                InputOperator("÷");
                target = FindOp("÷");
            }
            else if (code == Keys.OemQuestion)
            {
                if (shift) return;
                InputOperator("÷");
                target = FindOp("÷");
            }
            else if (code == Keys.Enter)
            {
                InputEquals();
                target = FindAction("equals");
            }
            else if (code == Keys.Back)
            {
                Backspace();
            }
            else if (code == Keys.Escape || code == Keys.Delete)
            {
                ResetAll();
                target = FindAction("clear");
            }
            else
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            SyncState();
            Flash(target);
        }

        private static string FormatNumber(double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n)) return "Error";
            if (n == 0) return "0";

            double abs = Math.Abs(n);
            if (abs >= 1e12 || abs < 1e-9)
            {
                string s = n.ToString("E6", Inv);
                int e = s.IndexOf('E');
                string mantissa = s.Substring(0, e).TrimEnd('0').TrimEnd('.');
                int exponent = int.Parse(s.Substring(e + 1), Inv);
                return mantissa + "e" + exponent.ToString(Inv);
            }

            double rounded = Math.Round(n, 12);
            return rounded.ToString("0.############", Inv);
        }

        private static double ParseEntry(string entry)
        {
            if (entry.EndsWith(".", StringComparison.Ordinal))
                entry = entry.Substring(0, entry.Length - 1);
            double value;
            if (!double.TryParse(entry, NumberStyles.Float, Inv, out value)) return 0;
            return value;
        }

        private static int CountDigits(string entry)
        {
            int count = 0;
            for (int i = 0; i < entry.Length; i++)
                if (entry[i] >= '0' && entry[i] <= '9') count++;
            return count;
        }

        private static double Apply(double a, string op, double b)
        {
            switch (op)
            {
                case "+": return a + b;
                case "−": return a - b;
                case "×": return a * b;
                default: return a / b;
            }
        }

        private void ResetAll()
        {
            state.Entry = "0";
            state.Acc = null;
            state.Op = null;
            state.Awaiting = false;
            state.StartNew = true;
            state.LastOp = null;
            state.LastRhs = 0;
            state.Error = false;
            state.Expr = "";
        }

        private void Fail()
        {
            ResetAll();
            state.Error = true;
            state.Entry = "Error";
        }

        private void InputDigit(string digit)
        {
            if (state.Error) ResetAll();

            if (state.Awaiting)
            {
                state.Entry = digit;
                state.Awaiting = false;
                state.StartNew = false;
                return;
            }

            if (state.StartNew)
            {
                state.Entry = digit;
                state.StartNew = false;
                state.LastOp = null;
                if (state.Op == null) state.Expr = "";
                return;
            }

            if (CountDigits(state.Entry) >= 12) return;
            if (state.Entry.IndexOf('e') >= 0)
            {
                state.Entry = digit;
                return;
            }
            state.Entry = state.Entry == "0" ? digit : state.Entry + digit;
        }

        private void InputDot()
        {
            if (state.Error) ResetAll();

            if (state.Awaiting || state.StartNew)
            {
                if (state.Op == null)
                {
                    state.Expr = "";
                    state.LastOp = null;
                }
                state.Entry = "0.";
                state.Awaiting = false;
                state.StartNew = false;
                return;
            }

            if (state.Entry.IndexOf('e') >= 0)
            {
                state.Entry = "0.";
                return;
            }
            if (state.Entry.IndexOf('.') < 0) state.Entry += ".";
        }

        private void InputPercent()
        {
            if (state.Error) return;

            double n = ParseEntry(state.Entry);
            bool relative = state.Op != null && !state.Awaiting &&
                (state.Op == "+" || state.Op == "−");
            double value = relative ? state.Acc.Value * n / 100.0 : n / 100.0;

            state.Entry = FormatNumber(value);
            state.Awaiting = false;
            state.StartNew = true;
        }

        private void InputOperator(string next)
        {
            if (state.Error) return;
            double n = ParseEntry(state.Entry);

            if (state.Op != null)
            {
                if (state.Awaiting)
                {
                    state.Op = next;
                    state.Expr = FormatNumber(state.Acc.Value) + " " + next;
                    return;
                }

                double result = Apply(state.Acc.Value, state.Op, n);
                if (double.IsNaN(result) || double.IsInfinity(result))
                {
                    Fail();
                    return;
                }
                state.Acc = result;
                state.Entry = FormatNumber(result);
                state.Expr = FormatNumber(result) + " " + next;
            }
            else
            {
                state.Acc = n;
                state.Expr = FormatNumber(n) + " " + next;
            }

            state.Op = next;
            state.LastOp = null;
            state.Awaiting = true;
            state.StartNew = true;
        }

        private void InputEquals()
        {
            if (state.Error) return;

            double result;
            string expr;

            if (state.Op != null)
            {
                double rhs = state.Awaiting ? state.Acc.Value : ParseEntry(state.Entry);
                result = Apply(state.Acc.Value, state.Op, rhs);
                if (double.IsNaN(result) || double.IsInfinity(result))
                {
                    Fail();
                    return;
                }
                expr = FormatNumber(state.Acc.Value) + " " + state.Op + " " +
                       FormatNumber(rhs) + " =";
                state.LastOp = state.Op;
                state.LastRhs = rhs;
                state.Op = null;
                state.Acc = null;
            }
            else if (state.LastOp != null)
            {
                double lhs = ParseEntry(state.Entry);
                result = Apply(lhs, state.LastOp, state.LastRhs);
                if (double.IsNaN(result) || double.IsInfinity(result))
                {
                    Fail();
                    return;
                }
                expr = FormatNumber(lhs) + " " + state.LastOp + " " +
                       FormatNumber(state.LastRhs) + " =";
            }
            else
            {
                return;
            }

            state.Entry = FormatNumber(result);
            state.Expr = expr;
            state.Awaiting = false;
            state.StartNew = true;
        }

        private void ClearKey()
        {
            if (state.Error)
            {
                ResetAll();
                return;
            }

            if (!state.StartNew)
            {
                state.Entry = "0";
                state.StartNew = true;
                if (state.Op != null) state.Awaiting = true;
                return;
            }

            ResetAll();
        }

        private void ToggleSign()
        {
            if (state.Error || state.Awaiting) return;
            if (state.Entry == "0") return;

            state.Entry = state.Entry.StartsWith("-", StringComparison.Ordinal)
                ? state.Entry.Substring(1)
                : "-" + state.Entry;
            state.StartNew = false;
        }

        private void Backspace()
        {
            if (state.Error)
            {
                ResetAll();
                return;
            }
            if (state.Awaiting) return;

            if (state.StartNew || state.Entry.IndexOf('e') >= 0)
            {
                state.Entry = "0";
                state.StartNew = false;
                return;
            }

            string next = state.Entry.Substring(0, state.Entry.Length - 1);
            if (next.Length == 0 || next == "-") next = "0";
            state.Entry = next;
        }

        private void DisposeFonts()
        {
            if (digitFont != null) { digitFont.Dispose(); digitFont = null; }
            if (fnFont != null) { fnFont.Dispose(); fnFont = null; }
            if (opFont != null) { opFont.Dispose(); opFont = null; }
            if (exprFont != null) { exprFont.Dispose(); exprFont = null; }
            foreach (Font font in valueFonts.Values) font.Dispose();
            valueFonts.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeFonts();
                flashTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
