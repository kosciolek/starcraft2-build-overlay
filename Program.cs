using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TextOverlay
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "TextOverlay.SingleInstance.7B7C2B77", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Text Overlay is already running.", "Text Overlay",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string baseFolder = AppDomain.CurrentDomain.BaseDirectory;
                string textPath = args.Length > 0
                    ? Path.GetFullPath(args[0])
                    : Path.Combine(baseFolder, "overlay.txt");

                if (!File.Exists(textPath))
                    File.WriteAllText(textPath, "Edit overlay.txt and save it.\r\nF1: go back one line\r\nF2: complete current line\r\nF3: exit", Encoding.UTF8);

                Application.Run(new OverlayForm(textPath));
                GC.KeepAlive(mutex);
            }
        }
    }

    internal sealed class OverlayForm : Form
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_BACK = 1;
        private const int HOTKEY_FORWARD = 2;
        private const int HOTKEY_EXIT = 3;
        private const uint VK_F1 = 0x70;
        private const uint VK_F2 = 0x71;
        private const uint VK_F3 = 0x72;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int ULW_ALPHA = 0x00000002;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private readonly string textPath;
        private readonly List<string> lines = new List<string>();
        private readonly FileSystemWatcher watcher;
        private readonly System.Windows.Forms.Timer reloadTimer;
        private readonly System.Windows.Forms.Timer topmostTimer;
        private readonly NotifyIcon trayIcon;
        private int completedCount;

        public OverlayForm(string path)
        {
            textPath = path;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open text file", null, delegate { OpenTextFile(); });
            menu.Items.Add("Reset progress", null, delegate { completedCount = 0; RenderOverlay(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { Close(); });

            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Text = "Text Overlay — F1 back, F2 forward, F3 exit",
                ContextMenuStrip = menu,
                Visible = true
            };
            trayIcon.DoubleClick += delegate { OpenTextFile(); };

            reloadTimer = new System.Windows.Forms.Timer { Interval = 180 };
            reloadTimer.Tick += delegate { reloadTimer.Stop(); LoadText(); };

            topmostTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            topmostTimer.Tick += delegate { KeepOnTop(); };

            watcher = new FileSystemWatcher(Path.GetDirectoryName(textPath), Path.GetFileName(textPath));
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
            watcher.Changed += WatchedTextChanged;
            watcher.Created += WatchedTextChanged;
            watcher.Renamed += WatchedTextChanged;
            watcher.EnableRaisingEvents = true;

            Load += delegate
            {
                bool backRegistered = RegisterHotKey(Handle, HOTKEY_BACK, 0, VK_F1);
                bool forwardRegistered = RegisterHotKey(Handle, HOTKEY_FORWARD, 0, VK_F2);
                bool exitRegistered = RegisterHotKey(Handle, HOTKEY_EXIT, 0, VK_F3);
                if (!backRegistered || !forwardRegistered || !exitRegistered)
                {
                    trayIcon.ShowBalloonTip(5000, "Text Overlay",
                        "One or more overlay hotkeys are reserved by another program.", ToolTipIcon.Warning);
                }
                LoadText();
                KeepOnTop();
                topmostTimer.Start();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_FORWARD && completedCount < lines.Count)
                {
                    completedCount++;
                    RenderOverlay();
                }
                else if (id == HOTKEY_BACK && completedCount > 0)
                {
                    completedCount--;
                    RenderOverlay();
                }
                else if (id == HOTKEY_EXIT)
                {
                    Close();
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UnregisterHotKey(Handle, HOTKEY_BACK);
            UnregisterHotKey(Handle, HOTKEY_FORWARD);
            UnregisterHotKey(Handle, HOTKEY_EXIT);
            topmostTimer.Stop();
            reloadTimer.Stop();
            watcher.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            base.OnFormClosed(e);
        }

        private void WatchedTextChanged(object sender, FileSystemEventArgs e)
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    reloadTimer.Stop();
                    reloadTimer.Start();
                });
            }
        }

        private void LoadText()
        {
            try
            {
                string content = File.ReadAllText(textPath, Encoding.UTF8).Replace("\r\n", "\n").Replace('\r', '\n');
                lines.Clear();
                foreach (string loadedLine in content.Split(new[] { '\n' }, StringSplitOptions.None))
                    lines.Add(loadedLine.TrimEnd());
                while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                    lines.RemoveAt(lines.Count - 1);
                completedCount = Math.Min(completedCount, lines.Count);
                RenderOverlay();
            }
            catch (IOException)
            {
                reloadTimer.Start();
            }
        }

        private void OpenTextFile()
        {
            try { System.Diagnostics.Process.Start(textPath); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Could not open text file"); }
        }

        private void KeepOnTop()
        {
            Rectangle area = Screen.PrimaryScreen.Bounds;
            if (Bounds != area) Bounds = area;
            SetWindowPos(Handle, HWND_TOPMOST, area.Left, area.Top, area.Width, area.Height,
                0x0010 | 0x0040); // SWP_NOACTIVATE | SWP_SHOWWINDOW
            RenderOverlay();
        }

        private void RenderOverlay()
        {
            if (!IsHandleCreated || ClientSize.Width < 1 || ClientSize.Height < 1) return;

            using (var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var font = new Font("Segoe UI", 11.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (var format = new StringFormat(StringFormatFlags.LineLimit))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                format.Alignment = StringAlignment.Near;
                format.Trimming = StringTrimming.EllipsisWord;

                float boxWidth = Math.Min(700.0f, Math.Max(320.0f, ClientSize.Width * 0.40f));
                float x = 6.0f;
                float y = 6.0f;
                float bottom = ClientSize.Height - 24.0f;

                for (int i = 0; i < lines.Count && y < bottom; i++)
                {
                    string text;
                    List<CharacterRange> highlightedRanges;
                    ParseMarkup(lines[i].Length == 0 ? " " : lines[i], out text, out highlightedRanges);
                    SizeF measured = g.MeasureString(text, font, new SizeF(boxWidth, bottom - y), format);
                    float height = Math.Max(font.GetHeight(g), measured.Height) + 1.0f;
                    if (y + height > bottom) break;

                    bool completed = i < completedCount;
                    int textAlpha = completed ? 38 : (i == completedCount ? 235 : 205);
                    int outlineAlpha = completed ? 24 : 165;

                    using (var path = new GraphicsPath())
                    using (var outline = new Pen(Color.FromArgb(outlineAlpha, 0, 0, 0), completed ? 2.0f : 3.2f))
                    using (var fill = new SolidBrush(Color.FromArgb(textAlpha, 255, 255, 255)))
                    {
                        var layout = new RectangleF(x, y, boxWidth, height);
                        path.AddString(text, font.FontFamily, (int)font.Style,
                            g.DpiY * font.SizeInPoints / 72.0f,
                            layout, format);
                        outline.LineJoin = LineJoin.Round;
                        g.DrawPath(outline, path);
                        g.FillPath(fill, path);

                        if (highlightedRanges.Count > 0)
                        {
                            using (var rangeFormat = new StringFormat(format))
                            {
                                rangeFormat.SetMeasurableCharacterRanges(highlightedRanges.ToArray());
                                Region[] regions = g.MeasureCharacterRanges(text, font, layout, rangeFormat);
                                for (int rangeIndex = 0; rangeIndex < regions.Length; rangeIndex++)
                                {
                                    Region region = regions[rangeIndex];
                                    CharacterRange range = highlightedRanges[rangeIndex];
                                    Color baseColor = GetHighlightColor(text.Substring(range.First, range.Length));
                                    using (var highlightFill = new SolidBrush(Color.FromArgb(
                                        textAlpha, baseColor.R, baseColor.G, baseColor.B)))
                                    using (var highlightWeight = new Pen(Color.FromArgb(
                                        textAlpha, baseColor.R, baseColor.G, baseColor.B), 1.0f))
                                    {
                                        // GDI+ rounds measurable character regions tightly and can clip
                                        // the antialiased right edge of the final glyph. Unioning a copy
                                        // shifted by two pixels covers that edge without altering layout.
                                        using (Region expandedRegion = region.Clone())
                                        using (Region shiftedRegion = region.Clone())
                                        {
                                            shiftedRegion.Translate(2.0f, 0.0f);
                                            expandedRegion.Union(shiftedRegion);
                                            GraphicsState state = g.Save();
                                            g.SetClip(expandedRegion, CombineMode.Intersect);
                                            highlightWeight.LineJoin = LineJoin.Round;
                                            g.DrawPath(highlightWeight, path);
                                            g.FillPath(highlightFill, path);
                                            g.Restore(state);
                                        }
                                    }
                                    region.Dispose();
                                }
                            }
                        }
                    }
                    y += height;
                }

                SetBitmap(bitmap);
            }
        }

        private static void ParseMarkup(string source, out string plainText, out List<CharacterRange> highlightedRanges)
        {
            var plain = new StringBuilder(source.Length);
            highlightedRanges = new List<CharacterRange>();
            int position = 0;

            while (position < source.Length)
            {
                int opening = source.IndexOf("**", position, StringComparison.Ordinal);
                if (opening < 0)
                {
                    plain.Append(source, position, source.Length - position);
                    break;
                }

                plain.Append(source, position, opening - position);
                int closing = source.IndexOf("**", opening + 2, StringComparison.Ordinal);
                if (closing < 0)
                {
                    plain.Append(source, opening, source.Length - opening);
                    break;
                }

                int rangeStart = plain.Length;
                plain.Append(source, opening + 2, closing - opening - 2);
                int rangeLength = plain.Length - rangeStart;
                if (rangeLength > 0)
                    highlightedRanges.Add(new CharacterRange(rangeStart, rangeLength));
                position = closing + 2;
            }

            plainText = plain.ToString();
        }

        private static Color GetHighlightColor(string content)
        {
            Color[] palette =
            {
                Color.FromArgb(105, 225, 255), // cyan
                Color.FromArgb(255, 222, 105), // yellow
                Color.FromArgb(125, 240, 155), // green
                Color.FromArgb(255, 185, 105), // orange
                Color.FromArgb(205, 160, 255), // violet
                Color.FromArgb(135, 195, 255), // sky blue
                Color.FromArgb(245, 155, 225), // pink
                Color.FromArgb(115, 240, 215)  // mint
            };

            // FNV-1a: deterministic, fast, and explicitly case-insensitive.
            uint hash = 2166136261;
            foreach (char character in content.ToUpperInvariant())
            {
                hash ^= character;
                hash *= 16777619;
            }
            return palette[hash % (uint)palette.Length];
        }

        private void SetBitmap(Bitmap bitmap)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memoryDc, hBitmap);
                var size = new SIZE(bitmap.Width, bitmap.Height);
                var source = new POINT(0, 0);
                var top = new POINT(Left, Top);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = 1
                };
                UpdateLayeredWindow(Handle, screenDc, ref top, ref size, memoryDc,
                    ref source, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int CX, CY; public SIZE(int x, int y) { CX = x; CY = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, int colorKey, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    }
}
