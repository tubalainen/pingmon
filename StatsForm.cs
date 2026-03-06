using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PingMon
{
    class StatsForm : Form
    {
        private static readonly Color[] HostColorsDark = {
            Color.DodgerBlue, Color.OrangeRed, Color.LimeGreen, Color.Gold, Color.MediumOrchid,
            Color.DeepSkyBlue, Color.Tomato, Color.SpringGreen, Color.Yellow, Color.Violet
        };
        private static readonly Color[] HostColorsLight = {
            Color.RoyalBlue, Color.Crimson, Color.DarkGreen, Color.DarkGoldenrod, Color.Purple,
            Color.SteelBlue, Color.Firebrick, Color.SeaGreen, Color.Goldenrod, Color.DarkViolet
        };

        private readonly PingMonitor _monitor;
        private readonly PingHistory _history;
        private readonly AppConfig _config;
        private readonly Action<HostStatus[]> _onStatusChanged;
        private readonly UserPreferenceChangedEventHandler _onPrefChanged;

        private bool _isDark;
        private Panel _graphPanel;
        private Panel _hostRow;
        private Panel _bottomRow;
        private FlowLayoutPanel _hostPanel;
        private Label _hostsLabel;
        private CheckBox _chkAlwaysOnTop;

        private readonly Dictionary<string, CheckBox> _hostChecks =
            new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Color> _hostColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _displayNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private int _nextColorIdx;

        private Dictionary<string, HistoryPoint[]> _currentSnapshot =
            new Dictionary<string, HistoryPoint[]>(StringComparer.OrdinalIgnoreCase);

        private static bool IsDarkMode()
        {
            try
            {
                var val = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return val is int i && i == 0;
            }
            catch { return false; }
        }

        public StatsForm(PingMonitor monitor, PingHistory history, AppConfig config)
        {
            _monitor = monitor;
            _history = history;
            _config  = config;

            Text = "PingMon Stats";
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(520, 320);
            MinimumSize = new Size(320, 240);
            Font = new Font("Segoe UI", 9f);

            BuildUI();
            SetInitialPosition();
            _isDark = IsDarkMode();
            ApplyTheme();

            _currentSnapshot = _history.GetSnapshot();
            RebuildHostCheckboxes();

            _onStatusChanged = OnStatusChanged;
            _monitor.StatusChanged += _onStatusChanged;

            _onPrefChanged = (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General)
                {
                    _isDark = IsDarkMode();
                    ApplyTheme();
                    _graphPanel.Invalidate();
                }
            };
            SystemEvents.UserPreferenceChanged += _onPrefChanged;

            FormClosing += (s, e) =>
            {
                _config.StatsWindowX = Location.X;
                _config.StatsWindowY = Location.Y;
                ConfigStore.Save(_config);
            };

            FormClosed += (s, e) =>
            {
                _monitor.StatusChanged -= _onStatusChanged;
                SystemEvents.UserPreferenceChanged -= _onPrefChanged;
            };
        }

        private void SetInitialPosition()
        {
            if (_config.StatsWindowX != int.MinValue && _config.StatsWindowY != int.MinValue)
            {
                var probe = new Point(_config.StatsWindowX + Width / 2, _config.StatsWindowY + Height / 2);
                if (Screen.AllScreens.Any(s => s.WorkingArea.Contains(probe)))
                {
                    Location = new Point(_config.StatsWindowX, _config.StatsWindowY);
                    return;
                }
            }
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 10, wa.Bottom - Height - 10);
        }

        private void BuildUI()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(layout);

            // Graph panel
            _graphPanel = new Panel { Dock = DockStyle.Fill };
            _graphPanel.Paint += GraphPanel_Paint;
            layout.Controls.Add(_graphPanel, 0, 0);

            // Host checkboxes row
            _hostRow = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(4, 2, 4, 2)
            };
            _hostsLabel = new Label { Text = "Hosts:", Left = 6, Top = 8, Width = 46, AutoSize = false };
            _hostRow.Controls.Add(_hostsLabel);

            _hostPanel = new FlowLayoutPanel
            {
                Left = 54, Top = 2,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            _hostPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            _hostRow.Controls.Add(_hostPanel);
            _hostRow.Resize += (s, e) => _hostPanel.Width = Math.Max(10, _hostRow.Width - 58);
            layout.Controls.Add(_hostRow, 0, 1);

            // Bottom row: always-on-top checkbox only
            _bottomRow = new Panel { Dock = DockStyle.Fill };

            _chkAlwaysOnTop = new CheckBox { Text = "Always on top", Left = 6, Top = 5, AutoSize = true };
            _chkAlwaysOnTop.CheckedChanged += (s, e) => TopMost = _chkAlwaysOnTop.Checked;
            _bottomRow.Controls.Add(_chkAlwaysOnTop);

            layout.Controls.Add(_bottomRow, 0, 2);
        }

        private void ApplyTheme()
        {
            Color formBack  = _isDark ? Color.FromArgb(32, 32, 32)    : SystemColors.Control;
            Color formText  = _isDark ? Color.FromArgb(210, 210, 210) : SystemColors.ControlText;
            Color graphBack = _isDark ? Color.FromArgb(28, 28, 28)    : Color.FromArgb(245, 245, 245);

            BackColor = formBack;
            ForeColor = formText;
            _hostRow.BackColor    = formBack;
            _hostPanel.BackColor  = formBack;
            _bottomRow.BackColor  = formBack;
            _hostsLabel.ForeColor = formText;
            _chkAlwaysOnTop.ForeColor = formText;
            _graphPanel.BackColor = graphBack;

            foreach (var kv in _hostChecks)
            {
                int colorIdx = Array.IndexOf(HostColorsDark, _hostColors.TryGetValue(kv.Key, out var c) ? c : HostColorsDark[0]);
                if (colorIdx < 0) colorIdx = 0;
                kv.Value.ForeColor = _isDark ? HostColorsDark[colorIdx % HostColorsDark.Length]
                                              : HostColorsLight[colorIdx % HostColorsLight.Length];
            }
        }

        private void RebuildHostCheckboxes()
        {
            var activeHosts = _currentSnapshot.Keys.ToArray();
            bool changed = false;

            foreach (var host in activeHosts)
            {
                string displayName = _displayNames.TryGetValue(host, out var dn) ? dn : host;
                if (!_hostChecks.ContainsKey(host))
                {
                    int colorIdx = _nextColorIdx++ % HostColorsDark.Length;
                    _hostColors[host] = HostColorsDark[colorIdx];

                    Color checkColor = _isDark ? HostColorsDark[colorIdx] : HostColorsLight[colorIdx];
                    var chk = new CheckBox
                    {
                        Text = displayName,
                        Checked = true,
                        AutoSize = true,
                        ForeColor = checkColor,
                        BackColor = Color.Transparent,
                        Margin = new Padding(2, 2, 6, 2)
                    };
                    chk.CheckedChanged += (s, e) => _graphPanel.Invalidate();
                    _hostChecks[host] = chk;
                    _hostPanel.Controls.Add(chk);
                    changed = true;
                }
                else if (_hostChecks[host].Text != displayName)
                {
                    _hostChecks[host].Text = displayName;
                    changed = true;
                }
            }

            var toRemove = _hostChecks.Keys.Except(activeHosts, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var host in toRemove)
            {
                _hostPanel.Controls.Remove(_hostChecks[host]);
                _hostChecks[host].Dispose();
                _hostChecks.Remove(host);
                changed = true;
            }

            if (changed) _hostPanel.Refresh();
        }

        private void OnStatusChanged(HostStatus[] snapshot)
        {
            foreach (var s in snapshot)
                if (s.IsEnabled && !string.IsNullOrEmpty(s.Host))
                    _displayNames[s.Host] = s.DisplayName ?? s.Host;

            _currentSnapshot = _history.GetSnapshot();
            RebuildHostCheckboxes();
            _graphPanel.Invalidate();
        }

        private void GraphPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = _graphPanel.ClientSize.Width;
            int h = _graphPanel.ClientSize.Height;

            const int leftMargin   = 52;
            const int rightMargin  = 10;
            const int topMargin    = 10;
            const int bottomMargin = 22;

            int plotW = w - leftMargin - rightMargin;
            int plotH = h - topMargin - bottomMargin;

            if (plotW < 10 || plotH < 10) return;

            var snapshot = _currentSnapshot;
            var now      = DateTime.Now;

            // --- X axis: dynamic window from oldest visible data point ---
            DateTime oldest = now;
            foreach (var kv in snapshot)
            {
                if (!_hostChecks.TryGetValue(kv.Key, out var chk) || !chk.Checked) continue;
                if (kv.Value.Length > 0 && kv.Value[0].Time < oldest)
                    oldest = kv.Value[0].Time;
            }
            double windowSec = Math.Min(7200.0, Math.Max(60.0, (now - oldest).TotalSeconds));

            // --- Y axis: data-range auto-scale ---
            long minRtt = long.MaxValue, maxRtt = 0;
            foreach (var kv in snapshot)
            {
                if (!_hostChecks.TryGetValue(kv.Key, out var chk) || !chk.Checked) continue;
                foreach (var pt in kv.Value)
                    if (pt.RoundtripMs >= 0 && (now - pt.Time).TotalSeconds <= windowSec)
                    {
                        if (pt.RoundtripMs < minRtt) minRtt = pt.RoundtripMs;
                        if (pt.RoundtripMs > maxRtt) maxRtt = pt.RoundtripMs;
                    }
            }

            long yMin, yMax;
            if (minRtt == long.MaxValue)
            {
                yMin = 0; yMax = 100;
            }
            else if (minRtt == maxRtt)
            {
                yMin = 0;
                yMax = RoundUpNice(maxRtt * 2 == 0 ? 10 : maxRtt * 2);
            }
            else
            {
                long range = maxRtt - minRtt;
                long pad = Math.Max(1, (long)(range * 0.15));
                yMin = RoundDownNice(Math.Max(0, minRtt - pad));
                yMax = RoundUpNice(maxRtt + pad);
            }
            if (yMax <= yMin) yMax = yMin + 10;

            double yRange = yMax > yMin ? (double)(yMax - yMin) : 1.0;

            // --- Theme-aware colors ---
            Color gridColor   = _isDark ? Color.FromArgb(55, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0);
            Color labelColor  = _isDark ? Color.FromArgb(160, 220, 220, 220) : Color.FromArgb(100, 50, 50, 50);
            Color borderColor = _isDark ? Color.FromArgb(100, 200, 200, 200) : Color.FromArgb(120, 80, 80, 80);

            using (var gridPen   = new Pen(gridColor, 1f))
            using (var axisBrush = new SolidBrush(labelColor))
            using (var labelFont = new Font("Segoe UI", 7.5f))
            {
                // Horizontal grid lines
                for (int i = 0; i <= 4; i++)
                {
                    float frac = i / 4f;
                    int py = topMargin + (int)(plotH * (1f - frac));
                    g.DrawLine(gridPen, leftMargin, py, leftMargin + plotW, py);

                    long labelVal = yMin + (long)((yMax - yMin) * frac);
                    var sz = g.MeasureString(labelVal.ToString(), labelFont);
                    g.DrawString(labelVal.ToString(), labelFont, axisBrush,
                        leftMargin - sz.Width - 2, py - sz.Height / 2);
                }

                // Vertical grid lines — adaptive spacing
                int gridIntervalSec;
                bool useHours;
                if      (windowSec <= 300)   { gridIntervalSec = 60;   useHours = false; }
                else if (windowSec <= 900)   { gridIntervalSec = 120;  useHours = false; }
                else if (windowSec <= 3600)  { gridIntervalSec = 300;  useHours = false; }
                else if (windowSec <= 14400) { gridIntervalSec = 900;  useHours = false; }
                else if (windowSec <= 86400) { gridIntervalSec = 3600; useHours = true;  }
                else                         { gridIntervalSec = 7200; useHours = true;  }

                double elapsed = 0;
                while (true)
                {
                    elapsed += gridIntervalSec;
                    if (elapsed > windowSec + gridIntervalSec) break;
                    int px = leftMargin + plotW - (int)(elapsed / windowSec * plotW);
                    if (px < leftMargin) break;
                    g.DrawLine(gridPen, px, topMargin, px, topMargin + plotH);

                    int count = (int)(elapsed / gridIntervalSec);
                    string label = useHours ? "-" + count + "h" : "-" + (int)(elapsed / 60) + "m";
                    var sz = g.MeasureString(label, labelFont);
                    g.DrawString(label, labelFont, axisBrush, px - sz.Width / 2, topMargin + plotH + 3);
                }

                // "now" label
                var nowSz = g.MeasureString("now", labelFont);
                g.DrawString("now", labelFont, axisBrush,
                    leftMargin + plotW - nowSz.Width, topMargin + plotH + 3);

                // Oldest time label
                g.DrawString(oldest.ToString("HH:mm"), labelFont, axisBrush, leftMargin + 1, topMargin + plotH + 3);

                // Y axis unit
                g.DrawString("ms", labelFont, axisBrush, 2, topMargin);
            }

            // --- Draw host lines ---
            using (var labelFont = new Font("Segoe UI", 7.5f))
            {
                foreach (var kv in snapshot)
                {
                    if (!_hostChecks.TryGetValue(kv.Key, out var chk) || !chk.Checked) continue;
                    if (!_hostColors.TryGetValue(kv.Key, out var lineColor)) continue;

                    var points = kv.Value;
                    if (points.Length == 0) continue;

                    using (var linePen  = new Pen(lineColor, 1.5f))
                    using (var dotBrush = new SolidBrush(lineColor))
                    {
                        PointF? prev = null;
                        HistoryPoint lastGood = null;
                        float lastGoodPx = 0, lastGoodPy = 0;

                        // points[] is oldest-first → iterates left-to-right on graph
                        foreach (var pt in points)
                        {
                            double secAgo = (now - pt.Time).TotalSeconds;
                            if (secAgo > windowSec + 1) continue;

                            float px = leftMargin + plotW - (float)(secAgo / windowSec * plotW);
                            if (px < leftMargin - 5 || px > leftMargin + plotW + 5) continue;

                            if (pt.RoundtripMs < 0)
                            {
                                // Timeout — X marker near top
                                float d = 3.5f, ty = topMargin + 5f;
                                g.DrawLine(linePen, px - d, ty - d, px + d, ty + d);
                                g.DrawLine(linePen, px + d, ty - d, px - d, ty + d);
                                prev = null;
                            }
                            else
                            {
                                float py = topMargin + plotH - (float)((pt.RoundtripMs - yMin) / yRange * plotH);
                                py = Math.Max(topMargin, Math.Min(topMargin + plotH, py));

                                if (prev.HasValue)
                                    g.DrawLine(linePen, prev.Value.X, prev.Value.Y, px, py);

                                g.FillEllipse(dotBrush, px - 2.5f, py - 2.5f, 5f, 5f);

                                prev = new PointF(px, py);
                                lastGood   = pt;
                                lastGoodPx = px;
                                lastGoodPy = py;
                            }
                        }

                        // RTT label at most recent ping
                        if (lastGood != null)
                        {
                            string rttLabel = lastGood.RoundtripMs + " ms";
                            using (var lblBrush = new SolidBrush(lineColor))
                            {
                                var sz = g.MeasureString(rttLabel, labelFont);
                                float lx = lastGoodPx + 4;
                                float ly = lastGoodPy - 9;
                                if (lx + sz.Width > leftMargin + plotW) lx = lastGoodPx - sz.Width - 4;
                                if (ly < topMargin) ly = topMargin;
                                g.DrawString(rttLabel, labelFont, lblBrush, lx, ly);
                            }
                        }
                    }
                }
            }

            // Axis border
            using (var axisPen = new Pen(borderColor, 1f))
                g.DrawRectangle(axisPen, leftMargin, topMargin, plotW, plotH);
        }

        private static long RoundUpNice(long value)
        {
            if (value <= 0) return 10;
            long[] steps = { 5, 10, 20, 25, 50, 100, 200, 500, 1000, 2000, 5000, 10000 };
            foreach (var s in steps)
                if (value <= s) return s;
            return ((value / 1000) + 1) * 1000;
        }

        private static long RoundDownNice(long value)
        {
            if (value <= 0) return 0;
            long[] steps = { 5, 10, 20, 25, 50, 100, 200, 500, 1000, 2000, 5000, 10000 };
            long prev = 0;
            foreach (var s in steps)
            {
                if (value < s) return prev;
                prev = s;
            }
            return (value / 1000) * 1000;
        }
    }
}
