// Patcher grafico: trova Disco Elysium sul PC e installa (o rimuove) la traduzione italiana.
// Non scrive nulla al di fuori della cartella del gioco.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Disco Elysium - Traduzione italiana")]
[assembly: AssemblyDescription("Installa la traduzione italiana amatoriale di Disco Elysium - The Final Cut")]
[assembly: AssemblyProduct("Disco Elysium - Traduzione italiana")]
[assembly: AssemblyCompany("Traduzione amatoriale: giovix93, Gianxs e contributori")]
[assembly: AssemblyCopyright("Mod gratuita, non affiliata a ZA/UM")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace DE
{
    public static class GameFinder
    {
        const string SteamAppId = "632470";

        static void Add(List<string> list, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                dir = Path.GetFullPath(dir.Trim().TrimEnd('\\', '/'));
                if (!PatchEngine.IsGameDir(dir)) return;
                foreach (string s in list) if (string.Equals(s, dir, StringComparison.OrdinalIgnoreCase)) return;
                list.Add(dir);
            }
            catch { }
        }

        static string RegValue(RegistryHive hive, RegistryView view, string key, string name)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey k = b.OpenSubKey(key))
                    return k == null ? null : k.GetValue(name) as string;
            }
            catch { return null; }
        }

        static void Steam(List<string> found)
        {
            List<string> roots = new List<string>();
            roots.Add(RegValue(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath"));
            roots.Add(RegValue(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath"));
            roots.Add(RegValue(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "InstallPath"));
            List<string> libs = new List<string>();
            foreach (string r in roots)
            {
                if (string.IsNullOrEmpty(r)) continue;
                string root = r.Replace('/', '\\');
                libs.Add(root);
                foreach (string vdf in new string[] { Path.Combine(root, @"steamapps\libraryfolders.vdf"), Path.Combine(root, @"config\libraryfolders.vdf") })
                {
                    try
                    {
                        if (!File.Exists(vdf)) continue;
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                    }
                    catch { }
                }
            }
            foreach (string lib in libs)
            {
                try
                {
                    string manifest = Path.Combine(lib, @"steamapps\appmanifest_" + SteamAppId + ".acf");
                    if (File.Exists(manifest))
                    {
                        Match m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                        if (m.Success) Add(found, Path.Combine(lib, @"steamapps\common\" + m.Groups[1].Value));
                    }
                    Add(found, Path.Combine(lib, @"steamapps\common\Disco Elysium"));
                }
                catch { }
            }
        }

        static void Gog(List<string> found)
        {
            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey games = b.OpenSubKey(@"SOFTWARE\GOG.com\Games"))
                    {
                        if (games == null) continue;
                        foreach (string id in games.GetSubKeyNames())
                            using (RegistryKey g = games.OpenSubKey(id))
                            {
                                string name = g.GetValue("gameName") as string;
                                if (name != null && name.IndexOf("Disco Elysium", StringComparison.OrdinalIgnoreCase) >= 0)
                                    Add(found, g.GetValue("path") as string);
                            }
                    }
                }
                catch { }
            }
        }

        static void Epic(List<string> found)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
                if (!Directory.Exists(dir)) return;
                foreach (string f in Directory.GetFiles(dir, "*.item"))
                {
                    string t = File.ReadAllText(f);
                    Match n = Regex.Match(t, "\"DisplayName\"\\s*:\\s*\"([^\"]*)\"");
                    Match l = Regex.Match(t, "\"InstallLocation\"\\s*:\\s*\"([^\"]*)\"");
                    if (n.Success && l.Success && n.Groups[1].Value.IndexOf("Disco Elysium", StringComparison.OrdinalIgnoreCase) >= 0)
                        Add(found, l.Groups[1].Value.Replace(@"\\", @"\").Replace('/', '\\'));
                }
            }
            catch { }
        }

        static void CommonPlaces(List<string> found)
        {
            Add(found, Program.BaseDir);
            string[] tails = new string[] {
                @"SteamLibrary\steamapps\common\Disco Elysium", @"Steam\steamapps\common\Disco Elysium",
                @"Program Files (x86)\Steam\steamapps\common\Disco Elysium", @"Program Files\Steam\steamapps\common\Disco Elysium",
                @"GOG Games\Disco Elysium", @"GOG Games\Disco Elysium - The Final Cut", @"Program Files (x86)\GOG Galaxy\Games\Disco Elysium",
                @"Program Files\Epic Games\DiscoElysium", @"Epic Games\DiscoElysium", @"Games\Disco Elysium", @"Giochi\Disco Elysium" };
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    foreach (string t in tails) Add(found, Path.Combine(d.RootDirectory.FullName, t));
                }
                catch { }
            }
        }

        public static List<string> FindAll()
        {
            List<string> found = new List<string>();
            Steam(found); Gog(found); Epic(found); CommonPlaces(found);
            return found;
        }
    }

    // Colori e caratteri dell'interfaccia: fondo scuro, testo color pergamena, arancio dei pulsanti del gioco
    // e i quattro colori degli attributi (Intelletto, Psiche, Fisico, Agilità).
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(0x16, 0x18, 0x1A);
        public static readonly Color Panel = Color.FromArgb(0x1F, 0x22, 0x25);
        public static readonly Color Well = Color.FromArgb(0x0F, 0x11, 0x12);
        public static readonly Color Border = Color.FromArgb(0x33, 0x37, 0x3B);
        public static readonly Color Text = Color.FromArgb(0xE9, 0xE4, 0xD6);
        public static readonly Color Muted = Color.FromArgb(0x9C, 0x98, 0x8C);
        public static readonly Color Faint = Color.FromArgb(0x6B, 0x69, 0x62);
        public static readonly Color Accent = Color.FromArgb(0xD8, 0x57, 0x2F);
        public static readonly Color AccentHover = Color.FromArgb(0xEC, 0x6B, 0x40);
        public static readonly Color AccentDown = Color.FromArgb(0xB5, 0x44, 0x22);
        public static readonly Color Int = Color.FromArgb(0x5B, 0xC0, 0xD6);
        public static readonly Color Psy = Color.FromArgb(0x7A, 0x5C, 0xD0);
        public static readonly Color Fys = Color.FromArgb(0xCB, 0x45, 0x6B);
        public static readonly Color Mot = Color.FromArgb(0xE2, 0xB5, 0x34);
        public static readonly Color Ok = Color.FromArgb(0x9F, 0xCB, 0x6E);
        public static readonly Color Err = Color.FromArgb(0xE6, 0x5F, 0x50);

        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }

        static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

        static Font Pick(string key, string[] names, float pt, FontStyle st)
        {
            Font f;
            if (cache.TryGetValue(key, out f)) return f;
            foreach (string n in names)
            {
                try
                {
                    Font c = new Font(n, pt, st, GraphicsUnit.Point);
                    if (string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)) { cache[key] = c; return c; }
                    c.Dispose();
                }
                catch { }
            }
            f = new Font(FontFamily.GenericSansSerif, pt, st, GraphicsUnit.Point);
            cache[key] = f; return f;
        }

        public static Font Ui(float pt, bool bold)
        {
            return Pick("ui" + pt + bold, bold ? new string[] { "Segoe UI Semibold", "Segoe UI" } : new string[] { "Segoe UI" }, pt, bold ? FontStyle.Bold : FontStyle.Regular);
        }
        // serif da "colonna dei dialoghi"
        public static Font Serif(float pt, bool italic)
        {
            return Pick("serif" + pt + italic, new string[] { "Constantia", "Georgia", "Times New Roman" }, pt, italic ? FontStyle.Italic : FontStyle.Regular);
        }
        // sans stretto per il titolo
        public static Font Title(float pt)
        {
            return Pick("title" + pt, new string[] { "Bahnschrift SemiBold Condensed", "Bahnschrift Condensed", "Arial Narrow", "Segoe UI Semibold" }, pt, FontStyle.Regular);
        }

        const TextFormatFlags Tight = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        // testo con spaziatura fra le lettere
        public static int DrawTracked(Graphics g, string s, Font f, Color c, int x, int y, int tracking)
        {
            foreach (char ch in s)
            {
                string t = ch.ToString();
                int w = ch == ' ' ? TextRenderer.MeasureText(g, "n", f, Size.Empty, Tight).Width * 2 / 3
                                  : TextRenderer.MeasureText(g, t, f, Size.Empty, Tight).Width;
                if (ch != ' ') TextRenderer.DrawText(g, t, f, new Point(x, y), c, Tight);
                x += w + tracking;
            }
            return x;
        }
    }

    public class FlatButton : Control
    {
        public bool Primary;
        bool hover, down;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true; Cursor = Cursors.Hand; Font = Theme.Ui(9.5f, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Rectangle r = ClientRectangle;
            Color back, fore, edge;
            if (!Enabled) { back = Theme.Panel; fore = Theme.Faint; edge = Theme.Border; }
            else if (Primary) { back = down ? Theme.AccentDown : hover ? Theme.AccentHover : Theme.Accent; fore = Color.White; edge = back; }
            else { back = down ? Theme.Well : hover ? Color.FromArgb(0x2A, 0x2E, 0x32) : Theme.Panel; fore = Theme.Text; edge = hover ? Theme.Muted : Theme.Border; }
            using (SolidBrush b = new SolidBrush(back)) g.FillRectangle(b, r);
            using (Pen p = new Pen(edge)) g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            if (Focused && Enabled && ShowFocusCues) // solo navigando da tastiera
                using (Pen p = new Pen(Color.FromArgb(150, Theme.Text))) { p.DashStyle = DashStyle.Dot; g.DrawRectangle(p, 3, 3, r.Width - 7, r.Height - 7); }
            TextRenderer.DrawText(g, Text.ToUpperInvariant(), Font, r, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    // Casella del percorso: bordo sottile che si accende quando ha il fuoco, più un pulsante per l'elenco delle installazioni trovate
    public class PathBox : Control
    {
        public readonly TextBox Box = new TextBox();
        public readonly List<string> Choices = new List<string>();
        Rectangle dropRect;

        public PathBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Well;
            Box.BorderStyle = BorderStyle.None; Box.BackColor = Theme.Well; Box.ForeColor = Theme.Text; Box.Font = Theme.Ui(10f, false);
            Box.GotFocus += delegate { Invalidate(); }; Box.LostFocus += delegate { Invalidate(); };
            Controls.Add(Box);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int drop = Choices.Count > 1 ? Height : 0;
            dropRect = new Rectangle(Width - drop, 0, drop, Height);
            Box.SetBounds(Theme.S(10), (Height - Box.PreferredHeight) / 2, Width - Theme.S(20) - drop, Box.PreferredHeight);
            base.OnLayout(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Choices.Count > 1 && dropRect.Contains(e.Location) && Enabled)
            {
                ContextMenuStrip m = new ContextMenuStrip();
                foreach (string c in Choices) { string path = c; m.Items.Add(c, null, delegate { Box.Text = path; }); }
                m.Show(this, new Point(0, Height));
            }
            else Box.Focus();
            base.OnMouseDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { Box.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Well);
            using (Pen p = new Pen(Box.Focused ? Theme.Accent : Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (Choices.Count > 1)
            {
                using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, dropRect.Left, 1, dropRect.Left, Height - 2);
                int cx = dropRect.Left + dropRect.Width / 2, cy = Height / 2, s = Theme.S(4);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Theme.Muted))
                    g.FillPolygon(b, new Point[] { new Point(cx - s, cy - s / 2), new Point(cx + s, cy - s / 2), new Point(cx, cy + s) });
            }
        }
    }

    // Registro delle operazioni, impaginato come la colonna dei dialoghi del gioco
    public class LogView : Control
    {
        class Line { public string Text; public int Kind; } // 0 testo, 1 titolo, 2 dettaglio, 3 errore, 4 esito positivo, 5 esito negativo
        readonly List<Line> lines = new List<Line>();
        readonly StringBuilder plain = new StringBuilder();

        public LogView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Copia registro", null, delegate { try { if (plain.Length > 0) Clipboard.SetText(plain.ToString()); } catch { } });
            ContextMenuStrip = m;
        }

        public string PlainText { get { return plain.ToString(); } }

        public void Add(string s)
        {
            plain.AppendLine(s);
            Line l = new Line();
            if (s.StartsWith("== ", StringComparison.Ordinal)) { l.Kind = 1; l.Text = s.Substring(3); }
            else if (s.StartsWith("ERRORE", StringComparison.Ordinal)) { l.Kind = 3; l.Text = s; }
            else if (s.StartsWith("  ", StringComparison.Ordinal)) { l.Kind = 2; l.Text = s.Trim(); }
            else { l.Kind = 0; l.Text = s; }
            if (l.Text.Length == 0) return;
            lines.Add(l); Invalidate();
        }

        // riga in stile "controllo di abilità": INTERFACCIAMENTO [Facile: Successo]
        public void AddCheck(bool success, string difficulty, string message)
        {
            plain.AppendLine(message);
            Line h = new Line(); h.Kind = success ? 4 : 5;
            h.Text = "INTERFACCIAMENTO [" + difficulty + ": " + (success ? "Successo" : "Fallimento") + "]";
            lines.Add(h);
            Line t = new Line(); t.Kind = 0; t.Text = message; lines.Add(t);
            Invalidate();
        }

        const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Well);
            using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            int padX = Theme.S(16), padY = Theme.S(12), w = Width - 2 * padX, gap = Theme.S(3);
            Font body = Theme.Serif(10.5f, false), head = Theme.Ui(8.25f, true);
            // altezze, poi si mostrano le ultime righe che entrano
            int[] hs = new int[lines.Count]; int[] ind = new int[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                Line l = lines[i];
                bool isHead = l.Kind == 1 || l.Kind >= 4;
                ind[i] = l.Kind == 2 ? Theme.S(14) : 0;
                hs[i] = TextRenderer.MeasureText(g, isHead ? l.Text.ToUpperInvariant() : l.Text, isHead ? head : body, new Size(w - ind[i], 10000), Wrap).Height
                        + (isHead && i > 0 ? Theme.S(8) : 0);
            }
            int first = lines.Count, total = 0, avail = Height - 2 * padY;
            while (first > 0 && total + hs[first - 1] + gap <= avail) { first--; total += hs[first] + gap; }
            int y = padY;
            for (int i = first; i < lines.Count; i++)
            {
                Line l = lines[i];
                bool isHead = l.Kind == 1 || l.Kind >= 4;
                int top = y + (isHead && i > first ? Theme.S(8) : 0);
                if (isHead && i == first && i > 0) y -= Theme.S(8); // niente spazio extra sopra il primo titolo visibile
                Color c = l.Kind == 1 ? Theme.Accent : l.Kind == 2 ? Theme.Muted : l.Kind == 3 ? Theme.Err : l.Kind == 4 ? Theme.Mot : l.Kind == 5 ? Theme.Err : Theme.Text;
                if (l.Kind == 2)
                    TextRenderer.DrawText(g, "–", body, new Point(padX, top), Theme.Faint, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, isHead ? l.Text.ToUpperInvariant() : l.Text, isHead ? head : body,
                    new Rectangle(padX + ind[i], top, w - ind[i], hs[i]), c, Wrap);
                y += hs[i] + gap;
            }
        }
    }

    // Linea di avanzamento: un segmento arancione che scorre mentre il programma lavora
    public class ProgressLine : Control
    {
        readonly Timer timer = new Timer();
        float pos; bool busy;

        public ProgressLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            timer.Interval = 25; timer.Tick += delegate { pos += 0.012f; if (pos > 1.35f) pos = -0.35f; Invalidate(); };
        }

        public bool Busy
        {
            get { return busy; }
            set { busy = value; pos = -0.35f; if (value) timer.Start(); else timer.Stop(); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(busy ? Theme.Border : Theme.Bg);
            if (!busy) return;
            int w = (int)(Width * 0.35f), x = (int)(pos * Width);
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(x - 1, 0, w + 2, Height), Color.FromArgb(0, Theme.Accent), Theme.Accent, 0f))
                e.Graphics.FillRectangle(b, x, 0, w, Height);
        }
    }

    public class MainForm : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        PathBox pathBox; FlatButton browseBtn, installBtn, removeBtn; LogView log; ProgressLine progress;
        BackgroundWorker worker; Payload payload; string payloadVersion = "";
        Bitmap header; string statusText = ""; Color statusColor = Theme.Muted;
        Rectangle statusRect, labelRect, footerRect; int headerH;

        static int S(int v) { return Theme.S(v); }

        public MainForm()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f;
            Text = "Disco Elysium - Traduzione italiana";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(720), S(606)); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Ui(9f, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Icon ico = Program.LoadIcon(); if (ico != null) Icon = ico;

            int m = S(28), w = ClientSize.Width - 2 * m;
            headerH = S(132);
            labelRect = new Rectangle(m, headerH + S(22), w, S(16));

            browseBtn = new FlatButton(); browseBtn.Text = "Sfoglia..."; browseBtn.SetBounds(ClientSize.Width - m - S(128), headerH + S(44), S(128), S(36));
            browseBtn.Click += Browse;
            pathBox = new PathBox(); pathBox.SetBounds(m, headerH + S(44), w - S(128) - S(10), S(36));
            pathBox.Box.TextChanged += delegate { RefreshStatus(); };
            statusRect = new Rectangle(m, headerH + S(90), w, S(22));

            installBtn = new FlatButton(); installBtn.Primary = true; installBtn.Text = "Installa traduzione"; installBtn.Font = Theme.Ui(10.5f, true);
            installBtn.SetBounds(m, headerH + S(124), S(340), S(48)); installBtn.Click += delegate { Run(true); };
            removeBtn = new FlatButton(); removeBtn.Text = "Ripristina file originali";
            removeBtn.SetBounds(m + S(352), headerH + S(124), w - S(352), S(48)); removeBtn.Click += delegate { Run(false); };

            progress = new ProgressLine(); progress.SetBounds(m, headerH + S(184), w, Math.Max(2, S(3)));
            log = new LogView(); log.SetBounds(m, headerH + S(199), w, S(214));
            footerRect = new Rectangle(m, headerH + S(423), w, S(44));

            Controls.Add(pathBox); Controls.Add(browseBtn); Controls.Add(installBtn); Controls.Add(removeBtn); Controls.Add(progress); Controls.Add(log);
            pathBox.TabIndex = 0; browseBtn.TabIndex = 1; installBtn.TabIndex = 2; removeBtn.TabIndex = 3;

            worker = new BackgroundWorker(); worker.DoWork += Work; worker.RunWorkerCompleted += Done;
            Shown += delegate { Init(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // barra del titolo scura su Windows 10/11 (ignorato dove non supportato)
            try { int on = 1; if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); } catch { }
        }

        // Testata: macchie di colore a pennellate, generate (nessuna immagine del gioco), con titolo e sottotitolo
        Bitmap BuildHeader()
        {
            int w = ClientSize.Width, h = headerH;
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle all = new Rectangle(0, 0, w, h);
                using (LinearGradientBrush b = new LinearGradientBrush(all, Color.FromArgb(0x20, 0x24, 0x28), Color.FromArgb(0x0D, 0x0F, 0x10), 25f)) g.FillRectangle(b, all);
                Random rnd = new Random(1951);
                Color[] pal = new Color[] { Theme.Int, Theme.Psy, Theme.Fys, Theme.Mot, Theme.Accent, Color.FromArgb(0x2B, 0x6A, 0x74), Color.FromArgb(0x8A, 0x3A, 0x2A) };
                for (int i = 0; i < 150; i++)
                {
                    float x = w * (0.30f + 0.80f * (float)rnd.NextDouble()), y = h * (float)(rnd.NextDouble() * 1.3 - 0.15);
                    float len = S(50 + rnd.Next(170)), thick = S(5 + rnd.Next(30));
                    float ang = -28f + rnd.Next(-22, 23);
                    Color c = pal[rnd.Next(pal.Length)]; int alpha = 16 + rnd.Next(58);
                    GraphicsState st = g.Save();
                    g.TranslateTransform(x, y); g.RotateTransform(ang);
                    RectangleF r = new RectangleF(-len / 2, -thick / 2, len, thick);
                    using (LinearGradientBrush b = new LinearGradientBrush(r, Color.FromArgb(alpha, c), Color.FromArgb(alpha / 5, c), 0f)) g.FillEllipse(b, r);
                    g.Restore(st);
                }
                // velo scuro da sinistra, così il titolo resta leggibile
                using (LinearGradientBrush veil = new LinearGradientBrush(new Rectangle(-1, 0, w + 2, h), Color.FromArgb(250, 0x12, 0x14, 0x16), Color.FromArgb(0, 0x12, 0x14, 0x16), 0f))
                {
                    ColorBlend cb = new ColorBlend(3);
                    cb.Colors = new Color[] { Color.FromArgb(250, 0x12, 0x14, 0x16), Color.FromArgb(215, 0x12, 0x14, 0x16), Color.FromArgb(40, 0x12, 0x14, 0x16) };
                    cb.Positions = new float[] { 0f, 0.42f, 1f };
                    veil.InterpolationColors = cb;
                    g.FillRectangle(veil, all);
                }
                using (LinearGradientBrush fade = new LinearGradientBrush(new Rectangle(0, h - S(40), w, S(41)), Color.FromArgb(0, Theme.Bg), Color.FromArgb(200, Theme.Bg), 90f))
                    g.FillRectangle(fade, 0, h - S(40), w, S(40));

                int x0 = S(28);
                Theme.DrawTracked(g, "DISCO ELYSIUM", Theme.Title(31f), Theme.Text, x0, S(20), S(3));
                int end = Theme.DrawTracked(g, "TRADUZIONE ITALIANA", Theme.Ui(9.5f, true), Theme.Accent, x0 + S(2), S(72), S(3));
                if (payloadVersion.Length > 0)
                    Theme.DrawTracked(g, "V" + payloadVersion, Theme.Ui(9.5f, true), Theme.Muted, end + S(10), S(72), S(2));
                TextRenderer.DrawText(g, "Prende il posto dello spagnolo: nel gioco scegli «Italiano» in Opzioni › Lingua.", Theme.Serif(10f, true),
                    new Point(x0 + S(2), S(97)), Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            return bmp;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            if (header == null) header = BuildHeader();
            g.DrawImageUnscaled(header, 0, 0);
            // i quattro colori degli attributi
            Color[] four = new Color[] { Theme.Int, Theme.Psy, Theme.Fys, Theme.Mot };
            int seg = ClientSize.Width / 4, sh = Math.Max(3, S(4));
            for (int i = 0; i < 4; i++)
                using (SolidBrush b = new SolidBrush(four[i])) g.FillRectangle(b, i * seg, headerH, i == 3 ? ClientSize.Width - 3 * seg : seg, sh);

            Theme.DrawTracked(g, "CARTELLA DEL GIOCO", Theme.Ui(8.25f, true), Theme.Muted, labelRect.X, labelRect.Y, S(2));

            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = S(9);
            using (SolidBrush b = new SolidBrush(statusColor)) g.FillEllipse(b, statusRect.X + 1, statusRect.Y + (statusRect.Height - d) / 2, d, d);
            TextRenderer.DrawText(g, statusText, Theme.Ui(9.75f, true), new Rectangle(statusRect.X + d + S(10), statusRect.Y, statusRect.Width - d - S(10), statusRect.Height),
                statusColor == Theme.Muted ? Theme.Text : statusColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g,
                "Traduzione originale di giovix93 · conversione di Gianxs · versione 2 completata e revisionata.\n" +
                "Progetto amatoriale e gratuito, non affiliato a ZA/UM.",
                Theme.Ui(8f, false), footerRect, Theme.Faint, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        public void SetPath(string dir) { pathBox.Box.Text = dir; }

        void AppendLog(string s)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { AppendLog(s); }); return; }
            log.Add(s);
        }

        void SetStatus(string text, Color c) { statusText = text; statusColor = c; Invalidate(statusRect); }

        void Init()
        {
            try
            {
                payload = Program.LoadPayload();
                payloadVersion = payload.Has("version") ? payload.Text("version").Trim() : "";
                if (header != null) { header.Dispose(); header = null; }
                Invalidate();
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "Impossibile leggere i dati della traduzione.\n\n" + e.Message +
                    "\n\nEstrai tutto l'archivio in una cartella e avvia il programma da lì: la cartella «" + Payload.FolderName +
                    "» deve trovarsi accanto al programma.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close(); return;
            }
            AppendLog("== Ricerca del gioco");
            List<string> found = GameFinder.FindAll();
            pathBox.Choices.Clear(); pathBox.Choices.AddRange(found); pathBox.PerformLayout(); pathBox.Invalidate();
            if (found.Count > 0)
            {
                if (pathBox.Box.Text.Trim().Length == 0) pathBox.Box.Text = found[0];
                AppendLog("Disco Elysium trovato.");
                foreach (string f in found) AppendLog("  " + f);
                if (found.Count > 1) AppendLog("Più installazioni trovate: scegli quella giusta dalla freccia accanto al percorso.");
            }
            else AppendLog("Gioco non trovato in automatico. Premi «Sfoglia...» e indica la cartella che contiene disco.exe.");
            RefreshStatus();
            installBtn.Focus();
        }

        void Browse(object s, EventArgs e)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Seleziona la cartella di Disco Elysium (quella che contiene disco.exe)";
                d.ShowNewFolderButton = false;
                if (Directory.Exists(pathBox.Box.Text)) d.SelectedPath = pathBox.Box.Text;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string p = d.SelectedPath;
                // tollera la selezione di una sottocartella (es. disco_Data)
                for (int i = 0; i < 3 && p != null && !PatchEngine.IsGameDir(p); i++) p = Path.GetDirectoryName(p);
                pathBox.Box.Text = p != null && PatchEngine.IsGameDir(p) ? p : d.SelectedPath;
            }
        }

        void RefreshStatus()
        {
            string dir = pathBox.Box.Text.Trim();
            bool busy = worker != null && worker.IsBusy;
            if (busy) return;
            if (!PatchEngine.IsGameDir(dir))
            {
                SetStatus(dir.Length == 0 ? "Indica la cartella del gioco." : "In questa cartella non c'è Disco Elysium - The Final Cut.", dir.Length == 0 ? Theme.Muted : Theme.Err);
                installBtn.Text = "Installa traduzione";
                installBtn.Enabled = removeBtn.Enabled = false; return;
            }
            InstallStatus st;
            try { st = PatchEngine.GetStatus(dir); } catch { st = new InstallStatus(); }
            if (st.Installed)
            {
                SetStatus("Traduzione installata (versione " + st.Version + ", " + st.Date + ").", Theme.Ok);
                installBtn.Text = st.Version == payloadVersion ? "Reinstalla traduzione" : "Aggiorna alla versione " + payloadVersion;
            }
            else if (st.Partial)
            {
                SetStatus("Il gioco è stato aggiornato dopo l'installazione: reinstalla la traduzione.", Theme.Mot);
                installBtn.Text = "Reinstalla traduzione";
            }
            else
            {
                SetStatus("Gioco trovato. Traduzione non ancora installata.", Theme.Muted);
                installBtn.Text = "Installa traduzione";
            }
            installBtn.Enabled = true; removeBtn.Enabled = st.Installed || st.Partial;
        }

        void Run(bool install)
        {
            string dir = pathBox.Box.Text.Trim();
            if (!PatchEngine.IsGameDir(dir) || worker.IsBusy) return;
            if (Program.GameRunning())
            {
                MessageBox.Show(this, "Disco Elysium è in esecuzione. Chiudi il gioco e riprova.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!install && MessageBox.Show(this, "Rimuovere la traduzione e ripristinare i file originali del gioco?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            installBtn.Enabled = removeBtn.Enabled = browseBtn.Enabled = pathBox.Enabled = false;
            progress.Busy = true;
            SetStatus(install ? "Installazione in corso..." : "Ripristino in corso...", Theme.Accent);
            AppendLog(install ? "== Installazione" : "== Ripristino");
            worker.RunWorkerAsync(new object[] { install, dir });
        }

        void Work(object s, DoWorkEventArgs e)
        {
            object[] a = (object[])e.Argument; bool install = (bool)a[0]; string dir = (string)a[1];
            PatchEngine eng = new PatchEngine(); eng.Log = AppendLog;
            if (install) eng.Install(dir, payload); else eng.Uninstall(dir);
            e.Result = a;
        }

        void Done(object s, RunWorkerCompletedEventArgs e)
        {
            progress.Busy = false; browseBtn.Enabled = pathBox.Enabled = true;
            if (e.Error != null)
            {
                bool denied = e.Error is UnauthorizedAccessException;
                log.AddCheck(false, "Impossibile", denied ? "Windows non permette di scrivere nella cartella del gioco." : e.Error.Message);
                RefreshStatus();
                if (denied)
                    MessageBox.Show(this, "Windows non permette di scrivere nella cartella del gioco.\n\nChiudi questo programma, poi fai clic con il tasto destro " +
                        "sul file e scegli «Esegui come amministratore».", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else MessageBox.Show(this, "Operazione non riuscita:\n\n" + e.Error.Message + "\n\nI file del gioco già scritti restano ripristinabili con «Ripristina file originali».",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            bool install = (bool)((object[])e.Result)[0];
            RefreshStatus();
            if (install)
            {
                log.AddCheck(true, "Facile", "Traduzione installata. Avvia il gioco e scegli «Italiano» in Opzioni › Lingua.");
                MessageBox.Show(this, "Traduzione installata!\n\nAvvia Disco Elysium e, nelle opzioni, scegli la lingua «Italiano» " +
                    "(prende il posto di «Español»).\n\nSe un aggiornamento del gioco o la verifica dei file di Steam riportano i testi in spagnolo, " +
                    "riavvia questo programma e premi di nuovo Installa.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                log.AddCheck(true, "Banale", "File originali del gioco ripristinati.");
                MessageBox.Show(this, "Traduzione rimossa: i file originali del gioco sono stati ripristinati.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // Solo per lo sviluppo: disegna la finestra su file, in uno stato d'esempio
        public void SavePreview(string file, string state)
        {
            StartPosition = FormStartPosition.Manual; Location = new Point(-30000, -30000); ShowInTaskbar = false;
            Show(); Application.DoEvents();
            if (state == "fatto")
            {
                log.Add("== Installazione");
                log.Add("Preparazione dei file...");
                log.Add("  Testi: 78909 stringhe tradotte");
                log.Add("  Scheda personaggio: 24 etichette skill tradotte");
                log.Add("  Menu: la lingua ora compare come \"Italiano\"");
                log.Add("Scrittura dei file tradotti...");
                log.AddCheck(true, "Facile", "Traduzione installata. Avvia il gioco e scegli «Italiano» in Opzioni › Lingua.");
                SetStatus("Traduzione installata (versione " + payloadVersion + ", 2026-10-13 21:40).", Theme.Ok);
                installBtn.Text = "Reinstalla traduzione"; removeBtn.Enabled = true;
            }
            else if (state == "nuovo")
            {
                SetStatus("Gioco trovato. Traduzione non ancora installata.", Theme.Muted);
                installBtn.Text = "Installa traduzione"; removeBtn.Enabled = false;
            }
            else if (state == "lavoro")
            {
                log.Add("== Installazione"); log.Add("Preparazione dei file...");
                SetStatus("Installazione in corso...", Theme.Accent);
                installBtn.Enabled = removeBtn.Enabled = browseBtn.Enabled = pathBox.Enabled = false; progress.Busy = true;
                for (int i = 0; i < 20; i++) { System.Threading.Thread.Sleep(30); Application.DoEvents(); }
            }
            Refresh(); Application.DoEvents();
            using (Bitmap bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                Rectangle client = RectangleToScreen(ClientRectangle);
                Rectangle crop = new Rectangle(client.X - Left, client.Y - Top, ClientSize.Width, ClientSize.Height);
                using (Bitmap c = bmp.Clone(crop, PixelFormat.Format32bppArgb)) c.Save(file, ImageFormat.Png);
            }
            Close();
        }
    }

    public static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        // Cartella del pacchetto: quella dell'exe, oppure quella indicata da Installa.ps1 quando il programma
        // viene compilato e avviato dai sorgenti (in quel caso l'eseguibile in esecuzione è PowerShell).
        static string baseDir;
        public static string BaseDir
        {
            get { return baseDir ?? Path.GetDirectoryName(Application.ExecutablePath); }
            set { baseDir = value; }
        }

        // I dati (testi in chiaro e immagini) stanno nella cartella "dati" del pacchetto
        public static Payload LoadPayload()
        {
            return Payload.LoadFolder(Path.Combine(BaseDir, Payload.FolderName));
        }

        public static Icon LoadIcon()
        {
            try
            {
                string ico = Path.Combine(Path.Combine(BaseDir, "sorgenti"), "icona.ico");
                if (baseDir != null && File.Exists(ico)) return new Icon(ico);
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { return null; }
        }

        public static bool GameRunning()
        {
            try { return Process.GetProcessesByName("disco").Length > 0; } catch { return false; }
        }

        // Uso senza interfaccia: /installa | /rimuovi  [/cartella "percorso"]  [/log "file"]
        static int Headless(bool install, string dir, string logFile)
        {
            StringBuilder log = new StringBuilder(); int code = 0;
            try
            {
                if (dir == null)
                {
                    List<string> found = GameFinder.FindAll();
                    if (found.Count == 0) throw new PatchException("Disco Elysium non trovato: indica la cartella con /cartella.");
                    dir = found[0];
                }
                log.AppendLine("Cartella: " + dir);
                if (GameRunning()) throw new PatchException("Disco Elysium è in esecuzione: chiudilo e riprova.");
                PatchEngine eng = new PatchEngine(); eng.Log = delegate(string s) { log.AppendLine(s); };
                if (install) eng.Install(dir, LoadPayload()); else eng.Uninstall(dir);
            }
            catch (Exception e) { log.AppendLine("ERRORE: " + e.Message); code = 1; }
            try { if (logFile != null) File.WriteAllText(logFile, log.ToString(), new UTF8Encoding(false)); } catch { }
            return code;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            bool install = false, remove = false; string dir = null, logFile = null, preview = null, previewState = "";
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "/installa") install = true;
                else if (a == "/rimuovi") remove = true;
                else if (a == "/cartella" && i + 1 < args.Length) dir = args[++i];
                else if (a == "/log" && i + 1 < args.Length) logFile = args[++i];
                else if (a == "/anteprima" && i + 1 < args.Length) { preview = args[++i]; if (i + 1 < args.Length) previewState = args[++i]; }
            }
            if (install || remove) return Headless(install, dir, logFile);
            // avviato dai sorgenti non c'è il manifest dell'exe: la nitidezza sugli schermi ad alta densità si chiede qui
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            try { Application.SetCompatibleTextRenderingDefault(false); } catch (InvalidOperationException) { }
            MainForm f = new MainForm();
            if (preview != null) { f.SavePreview(preview, previewState); return 0; }
            if (dir != null) f.Shown += delegate { f.SetPath(dir); };
            Application.Run(f);
            return 0;
        }
    }
}
