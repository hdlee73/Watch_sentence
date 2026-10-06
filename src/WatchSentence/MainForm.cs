using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WatchSentence;

public sealed class MainForm : Form
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WatchSentence";

    private readonly Settings _settings = Settings.Load();
    private readonly QuoteBook _quotes = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly ContextMenuStrip _menu = new();
    private DateTime _shown = DateTime.MinValue;
    private int _pickOffset;
    private int _pickMinute = -1;

    private Color Bg => ColorTranslator.FromHtml(_settings.Background);
    private Color Fg => ColorTranslator.FromHtml(_settings.Foreground);

    public MainForm()
    {
        Text = "Watch Sentence";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        MinimumSize = new Size(260, 130);
        MaximizeBox = false;
        using (var icoStream = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico"))
            if (icoStream != null) Icon = new Icon(icoStream);

        ApplySettings();
        BuildMenu();
        ContextMenuStrip = _menu;

        _timer.Tick += (_, _) =>
        {
            var now = DateTime.Now;
            if (now.Second != _shown.Second || now.Minute != _shown.Minute)
            {
                _shown = now;
                Invalidate();
            }
        };
        _timer.Start();
    }

    private void ApplySettings()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Size = new Size(_settings.Width, _settings.Height);
        var loc = _settings.X < 0 || _settings.Y < 0
            ? new Point(area.Right - Width - 24, area.Top + 24)
            : new Point(_settings.X, _settings.Y);
        // Keep the window reachable if a monitor was disconnected since last run.
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(loc, Size))))
            loc = new Point(area.Right - Width - 24, area.Top + 24);
        Location = loc;
        TopMost = _settings.AlwaysOnTop;
        Opacity = Math.Clamp(_settings.Opacity, 0.3, 1.0);
        BackColor = Bg;
    }

    // ---------------------------------------------------------------- menu

    private void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.ShowCheckMargin = true;

        AddCheck("항상 위에 고정 (Always on top)", _settings.AlwaysOnTop, v => { _settings.AlwaysOnTop = v; TopMost = v; });
        AddCheck("24시간제 (24-hour clock)", _settings.Use24Hour, v => _settings.Use24Hour = v);
        AddCheck("요일 한글 표시", _settings.KoreanWeekday, v => _settings.KoreanWeekday = v);
        _menu.Items.Add(new ToolStripSeparator());

        var theme = new ToolStripMenuItem("테마 (Theme)");
        foreach (var t in Themes.All)
        {
            var item = new ToolStripMenuItem(t.Label)
            {
                Checked = _settings.Theme == t.Name,
            };
            item.Click += (_, _) =>
            {
                _settings.Theme = t.Name;
                _settings.Background = t.Background;
                _settings.Foreground = t.Foreground;
                Changed();
            };
            theme.DropDownItems.Add(item);
        }
        theme.DropDownItems.Add(new ToolStripSeparator());
        theme.DropDownItems.Add("배경색 직접 선택…", null, (_, _) => PickColor(true));
        theme.DropDownItems.Add("글자색 직접 선택…", null, (_, _) => PickColor(false));
        _menu.Items.Add(theme);

        var opacity = new ToolStripMenuItem("투명도 (Opacity)");
        foreach (var o in new[] { 1.0, 0.9, 0.8, 0.7, 0.6 })
        {
            var item = new ToolStripMenuItem($"{o * 100:0}%") { Checked = Math.Abs(_settings.Opacity - o) < 0.01 };
            item.Click += (_, _) => { _settings.Opacity = o; Opacity = o; Changed(); };
            opacity.DropDownItems.Add(item);
        }
        _menu.Items.Add(opacity);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("다른 문구 보기 (Next quote)", null, (_, _) => { _pickOffset++; Invalidate(); });
        _menu.Items.Add("문구 복사 (Copy quote)", null, (_, _) => CopyQuote());
        AddCheck("Windows 시작 시 실행", IsStartupEnabled(), SetStartup);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("종료 (Exit)", null, (_, _) => Close());
    }

    private void AddCheck(string label, bool value, Action<bool> set)
    {
        var item = new ToolStripMenuItem(label) { Checked = value, CheckOnClick = true };
        item.CheckedChanged += (_, _) => { set(item.Checked); Changed(); };
        _menu.Items.Add(item);
    }

    private void Changed()
    {
        BackColor = Bg;
        _settings.Save();
        BeginInvoke(BuildMenu);
        Invalidate();
    }

    private void PickColor(bool background)
    {
        using var dlg = new ColorDialog { Color = background ? Bg : Fg, FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var hex = ColorTranslator.ToHtml(Color.FromArgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
        if (background) _settings.Background = hex; else _settings.Foreground = hex;
        _settings.Theme = "Custom";
        Changed();
    }

    private void CopyQuote()
    {
        var q = CurrentQuote(DateTime.Now);
        if (q != null) Clipboard.SetText($"{q.Text}\n— {q.Title}, {q.Author}");
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) != null;
    }

    private static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunValue, false);
    }

    // ---------------------------------------------------------------- quotes

    private Quote? CurrentQuote(DateTime now)
    {
        int minuteOfDay = now.Hour * 60 + now.Minute;
        if (minuteOfDay != _pickMinute)
        {
            _pickMinute = minuteOfDay;
            _pickOffset = 0;
        }
        // Stable for the whole minute, but varies from day to day.
        int pick = HashCode.Combine(now.DayOfYear, now.Year, minuteOfDay) + _pickOffset;
        return _quotes.For(now.Hour, now.Minute, pick);
    }

    // ---------------------------------------------------------------- painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var now = _shown == DateTime.MinValue ? DateTime.Now : _shown;
        var fg = Fg;
        var muted = Blend(fg, Bg, 0.35f);
        float h = ClientSize.Height, w = ClientSize.Width;
        float pad = Math.Max(10f, h * 0.07f);
        var fmt = StringFormat.GenericTypographic;
        fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        // Hairline frame, like the edge of a reader screen.
        using (var pen = new Pen(Blend(fg, Bg, 0.82f), 1f))
            g.DrawRectangle(pen, 0, 0, w - 1, h - 1);

        // Time: large HH:MM, smaller :SS on the same baseline.
        int hour = _settings.Use24Hour ? now.Hour : (now.Hour % 12 == 0 ? 12 : now.Hour % 12);
        string hm = _settings.Use24Hour ? $"{hour:00}:{now.Minute:00}" : $"{hour}:{now.Minute:00}";
        string ss = $":{now.Second:00}";
        float bigSize = Math.Clamp(Math.Min(h * 0.24f, w * 0.13f), 18f, 400f);
        using var bigFont = new Font("Segoe UI Light", bigSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var secFont = new Font("Segoe UI Light", bigSize * 0.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var smallFont = new Font("Segoe UI", bigSize * 0.22f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fgBrush = new SolidBrush(fg);
        using var mutedBrush = new SolidBrush(muted);

        float x = pad, y = pad * 0.8f;
        var hmSize = g.MeasureString(hm, bigFont, PointF.Empty, fmt);
        g.DrawString(hm, bigFont, fgBrush, x, y, fmt);
        float bigAscent = Ascent(bigFont), secAscent = Ascent(secFont);
        float secX = x + hmSize.Width + bigSize * 0.02f;
        g.DrawString(ss, secFont, mutedBrush, secX, y + bigAscent - secAscent, fmt);
        if (!_settings.Use24Hour)
        {
            var ssSize = g.MeasureString(ss, secFont, PointF.Empty, fmt);
            string ampm = now.Hour < 12 ? "AM" : "PM";
            g.DrawString(ampm, smallFont, mutedBrush, secX + ssSize.Width + bigSize * 0.08f,
                y + bigAscent - Ascent(smallFont), fmt);
        }

        // Date: YYYY-MM-DD weekday
        string weekday = _settings.KoreanWeekday
            ? now.ToString("dddd", CultureInfo.GetCultureInfo("ko-KR"))
            : now.ToString("dddd", CultureInfo.InvariantCulture);
        string date = $"{now:yyyy-MM-dd} {weekday}";
        using var dateFont = new Font(_settings.KoreanWeekday ? "Malgun Gothic" : "Segoe UI", Math.Max(9f, bigSize * 0.26f), FontStyle.Regular, GraphicsUnit.Pixel);
        float dateY = y + hmSize.Height + bigSize * 0.02f;
        g.DrawString(date, dateFont, mutedBrush, x + bigSize * 0.03f, dateY, fmt);
        float dateBottom = dateY + g.MeasureString(date, dateFont, PointF.Empty, fmt).Height;

        // Quote: bottom-right, the time phrase emphasised.
        var q = CurrentQuote(now);
        if (q == null) return;
        var box = new RectangleF(Math.Max(pad, w * 0.18f), dateBottom + pad * 0.6f, 0, 0);
        box.Width = w - pad - box.X;
        box.Height = h - pad * 0.8f - box.Y;
        if (box.Height < 20 || box.Width < 60) return;
        DrawQuote(g, q, box, fg, muted);
    }

    private void DrawQuote(Graphics g, Quote q, RectangleF box, Color fg, Color muted)
    {
        var fmt = StringFormat.GenericTypographic;
        string source = $"— {q.Title}, {q.Author}";
        float size = Math.Clamp(ClientSize.Height * 0.078f, 9f, 40f);

        for (; size >= 7f; size -= 0.5f)
        {
            using var regular = new Font("Georgia", size, FontStyle.Regular, GraphicsUnit.Pixel);
            using var bold = new Font("Georgia", size, FontStyle.Bold, GraphicsUnit.Pixel);
            using var srcFont = new Font("Georgia", size * 0.82f, FontStyle.Italic, GraphicsUnit.Pixel);

            var lines = LayoutQuote(g, q, regular, bold, box.Width);
            float lineH = regular.GetHeight(g) * 1.12f;
            var srcLines = WrapPlain(g, source, srcFont, box.Width);
            float srcH = srcFont.GetHeight(g) * srcLines.Count;
            float total = lines.Count * lineH + srcH + size * 0.45f;
            if (total > box.Height && size > 7.5f) continue;

            using var fgBrush = new SolidBrush(fg);
            using var mutedBrush = new SolidBrush(muted);
            float yy = box.Bottom - total;
            foreach (var line in lines)
            {
                float lineW = line.Sum(t => t.Width);
                float xx = box.Right - lineW;
                foreach (var t in line)
                {
                    g.DrawString(t.Text, t.Bold ? bold : regular, fgBrush, xx, yy, fmt);
                    xx += t.Width;
                }
                yy += lineH;
            }
            yy += size * 0.45f;
            foreach (var s in srcLines)
            {
                float sw = g.MeasureString(s, srcFont, PointF.Empty, fmt).Width;
                g.DrawString(s, srcFont, mutedBrush, box.Right - sw, yy, fmt);
                yy += srcFont.GetHeight(g);
            }
            return;
        }
    }

    private readonly record struct Token(string Text, bool Bold, float Width);

    private static List<List<Token>> LayoutQuote(Graphics g, Quote q, Font regular, Font bold, float maxWidth)
    {
        var fmt = StringFormat.GenericTypographic;
        int ps = q.Text.IndexOf(q.Phrase, StringComparison.Ordinal);
        int pe = ps < 0 ? -1 : ps + q.Phrase.Length;
        float space = g.MeasureString("a b", regular, PointF.Empty, fmt).Width - g.MeasureString("ab", regular, PointF.Empty, fmt).Width;

        // Split into words, then split any word that straddles the phrase boundary.
        var words = new List<(string text, bool bold)>();
        int i = 0;
        while (i < q.Text.Length)
        {
            while (i < q.Text.Length && q.Text[i] == ' ') i++;
            int start = i;
            while (i < q.Text.Length && q.Text[i] != ' ') i++;
            if (i == start) break;
            var pieces = new List<(int a, int b)> { (start, i) };
            if (ps >= 0)
            {
                foreach (var cut in new[] { ps, pe })
                {
                    var next = new List<(int a, int b)>();
                    foreach (var (a, b) in pieces)
                        if (cut > a && cut < b) { next.Add((a, cut)); next.Add((cut, b)); }
                        else next.Add((a, b));
                    pieces = next;
                }
            }
            for (int k = 0; k < pieces.Count; k++)
            {
                var (a, b) = pieces[k];
                bool isBold = ps >= 0 && a >= ps && b <= pe;
                words.Add((q.Text[a..b] + (k == pieces.Count - 1 ? " " : ""), isBold));
            }
        }

        var lines = new List<List<Token>> { new() };
        float lineW = 0;
        // Group pieces of the same word so a word never breaks across lines.
        var group = new List<Token>();
        float groupW = 0;
        void Flush()
        {
            if (group.Count == 0) return;
            float visible = groupW - (group[^1].Text.EndsWith(' ') ? space : 0);
            if (lineW > 0 && lineW + visible > maxWidth)
            {
                TrimLine(lines[^1], space);
                lines.Add(new List<Token>());
                lineW = 0;
            }
            lines[^1].AddRange(group);
            lineW += groupW;
            group.Clear();
            groupW = 0;
        }
        foreach (var (text, isBold) in words)
        {
            var font = isBold ? bold : regular;
            string core = text.TrimEnd(' ');
            float wdt = g.MeasureString(core, font, PointF.Empty, fmt).Width + (text.EndsWith(' ') ? space : 0);
            group.Add(new Token(text, isBold, wdt));
            groupW += wdt;
            if (text.EndsWith(' ')) Flush();
        }
        Flush();
        TrimLine(lines[^1], space);
        return lines;
    }

    private static void TrimLine(List<Token> line, float space)
    {
        if (line.Count > 0 && line[^1].Text.EndsWith(' '))
            line[^1] = line[^1] with { Text = line[^1].Text.TrimEnd(' '), Width = line[^1].Width - space };
    }

    private static List<string> WrapPlain(Graphics g, string text, Font font, float maxWidth)
    {
        var fmt = StringFormat.GenericTypographic;
        var lines = new List<string>();
        string cur = "";
        foreach (var word in text.Split(' '))
        {
            string trial = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && g.MeasureString(trial, font, PointF.Empty, fmt).Width > maxWidth)
            {
                lines.Add(cur);
                cur = word;
            }
            else cur = trial;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    private static float Ascent(Font f) =>
        f.Size * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style);

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    // ---------------------------------------------------------------- window behaviour

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

    private const int WM_NCLBUTTONDOWN = 0xA1, WM_NCHITTEST = 0x84;
    private const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
        HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.Clicks == 1)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        _pickOffset++;
        Invalidate();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_NCHITTEST || (int)m.Result != HTCLIENT) return;

        // Borderless window: let the outer few pixels act as resize handles.
        var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
        int grip = 8;
        bool l = p.X < grip, r = p.X >= ClientSize.Width - grip, t = p.Y < grip, b = p.Y >= ClientSize.Height - grip;
        int hit = (t, b, l, r) switch
        {
            (true, _, true, _) => HTTOPLEFT,
            (true, _, _, true) => HTTOPRIGHT,
            (_, true, true, _) => HTBOTTOMLEFT,
            (_, true, _, true) => HTBOTTOMRIGHT,
            (true, _, _, _) => HTTOP,
            (_, true, _, _) => HTBOTTOM,
            (_, _, true, _) => HTLEFT,
            (_, _, _, true) => HTRIGHT,
            _ => HTCLIENT,
        };
        m.Result = (IntPtr)hit;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (WindowState == FormWindowState.Normal)
        {
            _settings.X = Left;
            _settings.Y = Top;
            _settings.Width = Width;
            _settings.Height = Height;
        }
        _settings.Save();
        base.OnFormClosing(e);
    }
}
