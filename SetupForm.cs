using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using Microsoft.Win32;

namespace PingMon
{
    class SetupForm : Form
    {
        public AppConfig ResultConfig { get; private set; }

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
            Color back      = isDark ? Color.FromArgb(32, 32, 32)   : SystemColors.Control;
            Color text      = isDark ? Color.FromArgb(210, 210, 210) : SystemColors.ControlText;
            Color inputBack = isDark ? Color.FromArgb(45, 45, 45)   : SystemColors.Window;
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
                        lbl.ForeColor = lbl.Tag as string == "gray" ? grayText : text;
                        break;
                    case ListView lv:
                        lv.BackColor = inputBack;
                        lv.ForeColor = text;
                        break;
                    case Panel p:
                        p.BackColor = back;
                        ApplyThemeToControls(p.Controls, back, text, inputBack, grayText);
                        break;
                }
            }
        }

        // Controls
        private TextBox  _traceTargetBox;
        private Button   _btnTrace;
        private Label    _traceStatusLabel;
        private ListView _listView;
        private TextBox  _manualHostBox;
        private Button   _btnAddManual;
        private Button   _btnSaveStart;

        private CancellationTokenSource _traceCts;

        public SetupForm()
        {
            Text = "PingMon \u2013 First-Run Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            BuildUI();
            ApplyTheme(IsDarkMode());

            UserPreferenceChangedEventHandler onPrefChanged = null;
            onPrefChanged = (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General)
                    ApplyTheme(IsDarkMode());
            };
            SystemEvents.UserPreferenceChanged += onPrefChanged;
            FormClosed += (s, e) => SystemEvents.UserPreferenceChanged -= onPrefChanged;

            FormClosing += (s, e) => _traceCts?.Cancel();
        }

        private void BuildUI()
        {
            int y = 12;

            // Welcome header
            var lblTitle = new Label
            {
                Text = "Welcome to PingMon",
                Left = 12, Top = y, AutoSize = true,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold)
            };
            Controls.Add(lblTitle);
            y += 28;

            var lblHint = new Label
            {
                Text = "Trace a route to discover hops to monitor, or add hosts manually. Select up to " + AppConfig.MaxHosts + ".",
                Left = 12, Top = y, Width = 456, AutoSize = false,
                Tag = "gray"
            };
            Controls.Add(lblHint);
            y += 24;

            // Separator line (visual)
            var sep1 = new Label
            {
                Left = 12, Top = y, Width = 456, Height = 1,
                BorderStyle = BorderStyle.Fixed3D, AutoSize = false
            };
            Controls.Add(sep1);
            y += 8;

            // Traceroute section
            var lblTrace = new Label { Text = "Trace route to target:", Left = 12, Top = y + 3, Width = 150, AutoSize = false };
            _traceTargetBox = new TextBox { Left = 166, Top = y, Width = 210 };
            _btnTrace = new Button { Text = "Trace", Left = 382, Top = y - 1, Width = 86, Height = 26 };
            _btnTrace.Click += BtnTrace_Click;

            Controls.Add(lblTrace);
            Controls.Add(_traceTargetBox);
            Controls.Add(_btnTrace);
            y += 30;

            _traceStatusLabel = new Label
            {
                Text = "",
                Left = 12, Top = y, Width = 456, AutoSize = false,
                Tag = "gray"
            };
            Controls.Add(_traceStatusLabel);
            y += 22;

            // ListView
            _listView = new ListView
            {
                Left = 12, Top = y, Width = 456, Height = 200,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _listView.Columns.Add("IP / Host", 300);
            _listView.Columns.Add("Source", 140);
            Controls.Add(_listView);
            y += 208;

            // Manual add row
            var lblManual = new Label { Text = "Add host:", Left = 12, Top = y + 3, Width = 70, AutoSize = false };
            _manualHostBox = new TextBox { Left = 86, Top = y, Width = 255 };
            _btnAddManual = new Button { Text = "Add", Left = 347, Top = y - 1, Width = 86, Height = 26 };
            _btnAddManual.Click += BtnAddManual_Click;

            // Allow pressing Enter in the manual host box to add
            _manualHostBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { BtnAddManual_Click(s, e); e.SuppressKeyPress = true; }
            };

            Controls.Add(lblManual);
            Controls.Add(_manualHostBox);
            Controls.Add(_btnAddManual);
            y += 34;

            // Buttons row
            _btnSaveStart = new Button
            {
                Text = "Save && Start",
                Left = 280, Top = y, Width = 110, Height = 28
            };
            _btnSaveStart.Click += BtnSaveStart_Click;

            var btnCancel = new Button
            {
                Text = "Cancel",
                Left = 396, Top = y, Width = 72, Height = 28,
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(_btnSaveStart);
            Controls.Add(btnCancel);
            CancelButton = btnCancel;

            ClientSize = new Size(480, y + 44);
        }

        private void BtnTrace_Click(object sender, EventArgs e)
        {
            string target = _traceTargetBox.Text.Trim();
            if (string.IsNullOrEmpty(target))
            {
                MessageBox.Show("Please enter a target IP address or hostname.", "Trace Route",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // If currently tracing, stop it
            if (_btnTrace.Text == "Stop")
            {
                _traceCts?.Cancel();
                return;
            }

            // Cancel any previous trace and clear traced items from list
            _traceCts?.Cancel();
            _traceCts = new CancellationTokenSource();

            // Remove previously traced items (keep manual ones)
            for (int i = _listView.Items.Count - 1; i >= 0; i--)
            {
                if (_listView.Items[i].SubItems[1].Text.StartsWith("Traced"))
                    _listView.Items.RemoveAt(i);
            }

            _btnTrace.Text = "Stop";
            _btnTrace.Enabled = true;
            _traceStatusLabel.Text = "Tracing...";

            int hopCount = 0;
            var cts = _traceCts;

            Traceroute.Run(
                target,
                maxHops: 30,
                timeoutMs: 1500,
                onHop: (ttl, addr, rtt) =>
                {
                    if (IsDisposed) return;
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;
                        hopCount++;
                        AddOrUpdateListViewItem(addr.ToString(), "Traced hop " + ttl);
                        _traceStatusLabel.Text = string.Format("Tracing... hop {0}", ttl);
                    }));
                },
                onComplete: (success, msg) =>
                {
                    if (IsDisposed) return;
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;
                        _btnTrace.Text = "Trace";
                        if (cts.IsCancellationRequested)
                        {
                            _traceStatusLabel.Text = "Trace cancelled.";
                        }
                        else if (success)
                        {
                            _traceStatusLabel.Text = string.Format(
                                "Done. {0} hop{1} found. Target reached.", hopCount, hopCount == 1 ? "" : "s");
                        }
                        else
                        {
                            _traceStatusLabel.Text = string.Format(
                                "{0} hop{1} found. {2}", hopCount, hopCount == 1 ? "" : "s",
                                msg ?? "Trace ended.");
                        }
                    }));
                },
                ct: cts.Token);
        }

        private void AddOrUpdateListViewItem(string ip, string source)
        {
            // Skip duplicates
            foreach (ListViewItem existing in _listView.Items)
                if (string.Equals(existing.Text, ip, StringComparison.OrdinalIgnoreCase)) return;

            var lvi = new ListViewItem(ip);
            lvi.SubItems.Add(source);
            lvi.Checked = true;
            _listView.Items.Add(lvi);
        }

        private void BtnAddManual_Click(object sender, EventArgs e)
        {
            string host = _manualHostBox.Text.Trim();
            if (string.IsNullOrEmpty(host))
            {
                MessageBox.Show("Please enter a host or IP address.", "Add Host",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Check for duplicate
            foreach (ListViewItem existing in _listView.Items)
            {
                if (string.Equals(existing.Text, host, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(string.Format("'{0}' is already in the list.", host), "Duplicate",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            AddOrUpdateListViewItem(host, "Manual");
            _manualHostBox.Clear();
            _manualHostBox.Focus();
        }

        private void BtnSaveStart_Click(object sender, EventArgs e)
        {
            var selected = new System.Collections.Generic.List<string>();
            foreach (ListViewItem item in _listView.Items)
                if (item.Checked) selected.Add(item.Text);

            if (selected.Count == 0)
            {
                MessageBox.Show("Please select at least one host to monitor.", "No Hosts Selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (selected.Count > AppConfig.MaxHosts)
            {
                MessageBox.Show(
                    string.Format("Only up to {0} hosts can be monitored. You have {1} checked. Please uncheck the extras.", AppConfig.MaxHosts, selected.Count),
                    "Too Many Hosts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var config = new AppConfig { PingIntervalSeconds = 10, PingTimeoutMs = 2000 };
            foreach (string host in selected)
                config.Hosts.Add(new HostEntry { Host = host, Enabled = true, FailThreshold = 3, LatencyThresholdMs = 0 });

            ResultConfig = config;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
