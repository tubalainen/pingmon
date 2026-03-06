using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PingMon
{
    class ConfigForm : Form
    {
        public AppConfig ResultConfig { get; private set; }
        public bool EraseAllRequested { get; private set; }

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "PingMon";

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

        private void ApplyTheme(bool isDark)
        {
            Color back      = isDark ? Color.FromArgb(32, 32, 32)  : SystemColors.Control;
            Color text      = isDark ? Color.FromArgb(210, 210, 210) : SystemColors.ControlText;
            Color inputBack = isDark ? Color.FromArgb(45, 45, 45)  : SystemColors.Window;
            Color grayText  = isDark ? Color.FromArgb(140, 140, 140) : SystemColors.GrayText;

            BackColor = back;
            ForeColor = text;
            ApplyThemeToControls(Controls, back, text, inputBack, grayText);
        }

        private static void ApplyThemeToControls(
            Control.ControlCollection controls,
            Color back, Color text, Color inputBack, Color grayText)
        {
            foreach (Control c in controls)
            {
                switch (c)
                {
                    case TextBox tb:
                        tb.BackColor = inputBack;
                        tb.ForeColor = text;
                        break;
                    case NumericUpDown nud:
                        nud.BackColor = inputBack;
                        nud.ForeColor = text;
                        break;
                    case Button btn:
                        btn.FlatStyle = back == SystemColors.Control
                            ? FlatStyle.Standard
                            : FlatStyle.Flat;
                        btn.BackColor = back == SystemColors.Control
                            ? SystemColors.Control
                            : Color.FromArgb(55, 55, 55);
                        btn.ForeColor = text;
                        break;
                    case CheckBox chk:
                        chk.ForeColor = text;
                        break;
                    case Label lbl:
                        // Labels with Tag="gray" keep a subdued color
                        lbl.ForeColor = lbl.Tag as string == "gray" ? grayText : text;
                        break;
                    case Panel p:
                        p.BackColor = back;
                        ApplyThemeToControls(p.Controls, back, text, inputBack, grayText);
                        break;
                }
            }
        }

        private NumericUpDown _intervalNum;
        private NumericUpDown _timeoutNum;
        private CheckBox _chkAutoStart;
        private CheckBox[] _enabledChecks = new CheckBox[AppConfig.MaxHosts];
        private TextBox[] _hostBoxes = new TextBox[AppConfig.MaxHosts];
        private TextBox[] _nameBoxes = new TextBox[AppConfig.MaxHosts];
        private NumericUpDown[] _failNums = new NumericUpDown[AppConfig.MaxHosts];
        private NumericUpDown[] _latNums = new NumericUpDown[AppConfig.MaxHosts];

        public ConfigForm(AppConfig config)
        {
            Text = "PingMon Configuration";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(660, 320);
            Font = new Font("Segoe UI", 9f);

            BuildUI(config);
            ApplyTheme(IsDarkMode());

            UserPreferenceChangedEventHandler onPrefChanged = null;
            onPrefChanged = (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General)
                    ApplyTheme(IsDarkMode());
            };
            SystemEvents.UserPreferenceChanged += onPrefChanged;
            FormClosed += (s, e) => SystemEvents.UserPreferenceChanged -= onPrefChanged;
        }

        private void BuildUI(AppConfig config)
        {
            // --- Global settings row ---
            int y = 12;

            var lblInterval = new Label { Text = "Ping interval (sec):", Left = 10, Top = y + 3, Width = 140, AutoSize = false };
            _intervalNum = new NumericUpDown
            {
                Left = 155, Top = y, Width = 70,
                Minimum = 1, Maximum = 3600, Value = Clamp(config.PingIntervalSeconds, 1, 3600)
            };

            var lblTimeout = new Label { Text = "Timeout (ms):", Left = 240, Top = y + 3, Width = 110, AutoSize = false };
            _timeoutNum = new NumericUpDown
            {
                Left = 355, Top = y, Width = 80,
                Minimum = 100, Maximum = 30000, Value = Clamp(config.PingTimeoutMs, 100, 30000)
            };

            Controls.Add(lblInterval);
            Controls.Add(_intervalNum);
            Controls.Add(lblTimeout);
            Controls.Add(_timeoutNum);

            y += 34;

            // --- Column headers ---
            var headerPanel = new Panel { Left = 10, Top = y, Width = 640, Height = 20 };
            headerPanel.Controls.Add(new Label { Text = "En",         Left = 4,   Top = 2, Width = 28,  ForeColor = SystemColors.GrayText, Tag = "gray" });
            headerPanel.Controls.Add(new Label { Text = "Host / IP", Left = 26,  Top = 2, Width = 130 });
            headerPanel.Controls.Add(new Label { Text = "Name",       Left = 162, Top = 2, Width = 130 });
            headerPanel.Controls.Add(new Label { Text = "Fail #",    Left = 298, Top = 2, Width = 55,  ForeColor = SystemColors.GrayText, Tag = "gray" });
            headerPanel.Controls.Add(new Label { Text = "Latency ms",Left = 360, Top = 2, Width = 80,  ForeColor = SystemColors.GrayText, Tag = "gray" });
            Controls.Add(headerPanel);
            y += 22;

            // --- Host rows ---
            for (int i = 0; i < AppConfig.MaxHosts; i++)
            {
                HostEntry entry = i < config.Hosts.Count ? config.Hosts[i] : null;

                _enabledChecks[i] = new CheckBox
                {
                    Left = 14, Top = y + 2, Width = 20,
                    Checked = entry != null && entry.Enabled
                };

                _hostBoxes[i] = new TextBox
                {
                    Left = 36, Top = y, Width = 130,
                    Text = entry?.Host ?? ""
                };

                _nameBoxes[i] = new TextBox
                {
                    Left = 172, Top = y, Width = 130,
                    Text = entry?.Name ?? ""
                };

                _failNums[i] = new NumericUpDown
                {
                    Left = 308, Top = y, Width = 55,
                    Minimum = 1, Maximum = 20,
                    Value = Clamp(entry?.FailThreshold ?? 3, 1, 20)
                };

                _latNums[i] = new NumericUpDown
                {
                    Left = 370, Top = y, Width = 80,
                    Minimum = 0, Maximum = 30000,
                    Value = Clamp(entry?.LatencyThresholdMs ?? 0, 0, 30000)
                };

                Controls.Add(_enabledChecks[i]);
                Controls.Add(_hostBoxes[i]);
                Controls.Add(_nameBoxes[i]);
                Controls.Add(_failNums[i]);
                Controls.Add(_latNums[i]);

                y += 30;
            }

            // --- Hint ---
            var hint = new Label
            {
                Text = "Fail # = consecutive failures before alert.  Latency ms = 0 disables latency alerting.",
                Left = 10, Top = y + 4, Width = 540, ForeColor = SystemColors.GrayText, AutoSize = false,
                Tag = "gray"
            };
            Controls.Add(hint);
            y += 26;

            // --- Auto-start ---
            _chkAutoStart = new CheckBox
            {
                Text = "Start PingMon automatically when Windows starts",
                Left = 10, Top = y + 4, AutoSize = true,
                Checked = IsAutoStartEnabled()
            };
            Controls.Add(_chkAutoStart);
            y += 26;

            // --- Erase / OK / Cancel buttons ---
            var btnErase = new Button
            {
                Text = "Erase All Settings\u2026",
                Left = 10, Top = y + 4, Width = 145
            };
            btnErase.Click += BtnErase_Click;

            var btnOk = new Button
            {
                Text = "OK", DialogResult = DialogResult.OK,
                Left = 490, Top = y + 4, Width = 75
            };
            btnOk.Click += BtnOk_Click;

            var btnCancel = new Button
            {
                Text = "Cancel", DialogResult = DialogResult.Cancel,
                Left = 575, Top = y + 4, Width = 75
            };

            Controls.Add(btnErase);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            ClientSize = new Size(660, y + 40);
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            var hosts = new List<HostEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < AppConfig.MaxHosts; i++)
            {
                string host = _hostBoxes[i].Text.Trim();
                bool enabled = _enabledChecks[i].Checked;

                if (enabled && string.IsNullOrEmpty(host))
                {
                    MessageBox.Show(string.Format("Row {0}: Host cannot be empty when enabled.", i + 1),
                        "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }

                if (!string.IsNullOrEmpty(host))
                {
                    if (seen.Contains(host))
                    {
                        var r = MessageBox.Show(
                            string.Format("'{0}' is listed more than once. Continue anyway?", host),
                            "Duplicate Host", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (r == DialogResult.No)
                        {
                            DialogResult = DialogResult.None;
                            return;
                        }
                    }
                    seen.Add(host);

                    hosts.Add(new HostEntry
                    {
                        Host = host,
                        Name = _nameBoxes[i].Text.Trim(),
                        Enabled = enabled,
                        FailThreshold = (int)_failNums[i].Value,
                        LatencyThresholdMs = (int)_latNums[i].Value
                    });
                }
            }

            ResultConfig = new AppConfig
            {
                Hosts = hosts,
                PingIntervalSeconds = (int)_intervalNum.Value,
                PingTimeoutMs = (int)_timeoutNum.Value
            };

            // Apply auto-start registry setting
            try { SetAutoStart(_chkAutoStart.Checked); }
            catch { /* best effort */ }
        }

        private void BtnErase_Click(object sender, EventArgs e)
        {
            var r = MessageBox.Show(
                "This will delete all settings and restart the setup wizard.\n\nContinue?",
                "Erase All Settings",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (r != DialogResult.Yes) return;

            EraseAllRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        private void SetAutoStart(bool enable)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
            {
                if (key == null) return;
                if (enable)
                    key.SetValue(AppName, Application.ExecutablePath);
                else
                    key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }

        private static decimal Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
