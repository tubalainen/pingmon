using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PingMon
{
    static class Program
    {
        static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            _mutex = new Mutex(true, "PingMon_SingleInstance_Mutex", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("PingMon is already running.", "PingMon",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayAppContext());

            _mutex.ReleaseMutex();
        }
    }

    class TrayAppContext : ApplicationContext
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private NotifyIcon _trayIcon;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miConfigure;
        private ToolStripMenuItem _miStats;
        private ToolStripMenuItem[] _miStatus;
        private PingMonitor _monitor;
        private AppConfig _config;
        private IntPtr _currentIconHandle = IntPtr.Zero;
        private bool _configFormOpen;
        private PingHistory _history = new PingHistory();
        private StatsForm _statsForm;

        public TrayAppContext()
        {
            _miStatus = new ToolStripMenuItem[AppConfig.MaxHosts];

            bool isFirstRun = !System.IO.File.Exists(ConfigStore.ConfigPath);
            if (isFirstRun)
            {
                _config = RunSetupWizard();
                if (_config == null)
                {
                    // User cancelled setup — exit without showing tray icon
                    Application.ExitThread();
                    return;
                }
                ConfigStore.Save(_config);
            }
            else
            {
                _config = ConfigStore.Load();
            }

            InitTray();
            StartMonitor();
        }

        private AppConfig RunSetupWizard()
        {
            using (var f = new SetupForm())
            {
                if (f.ShowDialog() == DialogResult.OK)
                    return f.ResultConfig;
                return null;
            }
        }

        private void StartMonitor()
        {
            _monitor = new PingMonitor(_config, SynchronizationContext.Current);
            _monitor.HostStateChanged += OnHostStateChanged;
            _monitor.StatusChanged += OnStatusChanged;
            _monitor.StatusChanged += s => _history.Add(s);
            _monitor.Start();
        }

        private void InitTray()
        {
            // Build status rows (disabled, non-clickable — display only)
            _menu = new ContextMenuStrip();
            for (int i = 0; i < AppConfig.MaxHosts; i++)
            {
                _miStatus[i] = new ToolStripMenuItem { Enabled = false, Visible = false };
                _menu.Items.Add(_miStatus[i]);
            }
            _menu.Items.Add(new ToolStripSeparator());

            _miStats = new ToolStripMenuItem("Stats");
            _miStats.Click += (s, e) => OpenStatsWindow();
            _menu.Items.Add(_miStats);

            _miConfigure = new ToolStripMenuItem("Configure...");
            _miConfigure.Click += (s, e) => OpenConfigForm();
            _menu.Items.Add(_miConfigure);

            _menu.Items.Add(new ToolStripSeparator());

            var miExit = new ToolStripMenuItem("Exit");
            miExit.Click += (s, e) =>
            {
                _trayIcon.Visible = false;
                Application.ExitThread();
            };
            _menu.Items.Add(miExit);

            _menu.Opening += (s, e) => RefreshStatusRows(_monitor?.Results);

            _trayIcon = new NotifyIcon
            {
                ContextMenuStrip = _menu,
                Visible = true,
                Text = "PingMon"
            };
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) OpenStatsWindow();
            };
            SetTrayIcon(Color.Gray);
        }

        private void RefreshStatusRows(HostStatus[] results)
        {
            if (results == null)
            {
                for (int i = 0; i < AppConfig.MaxHosts; i++) _miStatus[i].Visible = false;
                return;
            }

            bool any = false;
            for (int i = 0; i < AppConfig.MaxHosts; i++)
            {
                var r = results[i];
                if (!r.IsEnabled || string.IsNullOrEmpty(r.Host))
                {
                    _miStatus[i].Visible = false;
                    continue;
                }
                any = true;
                _miStatus[i].Visible = true;

                string status;
                if (r.IsDown)
                    status = "FAILED";
                else if (r.LastRoundtripMs >= 0)
                    status = r.LastRoundtripMs + " ms" + (r.LatencyAlert ? " !" : "");
                else
                    status = "...";

                string icon = r.IsDown ? "x" : (r.LatencyAlert ? "~" : "v");
                string label = r.DisplayName ?? r.Host;
                _miStatus[i].Text = string.Format("  [{0}] {1}   {2}", icon, PadRight(label, 20), status);
            }

            // Show separator only if there are host rows
            _menu.Items[AppConfig.MaxHosts].Visible = any;  // separator after status rows
        }

        private static string PadRight(string s, int len)
        {
            if (s.Length >= len) return s.Substring(0, len);
            return s + new string(' ', len - s.Length);
        }

        private void OnHostStateChanged(HostStatus[] snapshot, string title, string message, bool isDown)
        {
            _trayIcon.ShowBalloonTip(
                10000,
                title,
                message,
                isDown ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        private void OnStatusChanged(HostStatus[] snapshot)
        {
            var status = ComputeStatus(snapshot);
            Color color;
            switch (status)
            {
                case OverallStatus.AllOk:       color = Color.FromArgb(32, 200, 32); break;
                case OverallStatus.LatencyWarn: color = Color.Gold; break;
                case OverallStatus.SomeFailing: color = Color.OrangeRed; break;
                case OverallStatus.AllFailing:  color = Color.Red; break;
                default:                        color = Color.Gray; break;
            }
            SetTrayIcon(color);

            // Update tooltip
            var enabled = snapshot.Where(r => r.IsEnabled && !string.IsNullOrEmpty(r.Host)).ToArray();
            if (enabled.Length == 0)
            {
                _trayIcon.Text = "PingMon - no hosts configured";
                return;
            }
            int downCount = enabled.Count(r => r.IsDown);
            string tip = string.Format("PingMon: {0}/{1} OK", enabled.Length - downCount, enabled.Length);
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            _trayIcon.Text = tip;
        }

        private enum OverallStatus { NoHosts, AllOk, LatencyWarn, SomeFailing, AllFailing }

        private OverallStatus ComputeStatus(HostStatus[] results)
        {
            var enabled = results.Where(r => r.IsEnabled && !string.IsNullOrEmpty(r.Host)).ToArray();
            if (enabled.Length == 0) return OverallStatus.NoHosts;
            int down = enabled.Count(r => r.IsDown);
            int latWarn = enabled.Count(r => r.LatencyAlert && !r.IsDown);
            if (down == enabled.Length) return OverallStatus.AllFailing;
            if (down > 0) return OverallStatus.SomeFailing;
            if (latWarn > 0) return OverallStatus.LatencyWarn;
            return OverallStatus.AllOk;
        }

        private void SetTrayIcon(Color color)
        {
            if (_currentIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_currentIconHandle);
                _currentIconHandle = IntPtr.Zero;
            }

            using (var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(color))
                    g.FillEllipse(brush, 1, 1, 13, 13);
                using (var pen = new Pen(Color.FromArgb(100, 0, 0, 0), 1f))
                    g.DrawEllipse(pen, 1, 1, 13, 13);

                _currentIconHandle = bmp.GetHicon();
                _trayIcon.Icon = Icon.FromHandle(_currentIconHandle);
            }
        }

        private void OpenConfigForm()
        {
            if (_configFormOpen) return;
            _configFormOpen = true;
            _miConfigure.Enabled = false;
            try
            {
                using (var f = new ConfigForm(_config))
                {
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        if (f.EraseAllRequested)
                        {
                            HandleEraseAllSettings();
                        }
                        else
                        {
                            _config = f.ResultConfig;
                            ConfigStore.Save(_config);
                            _history.Clear();
                            _monitor.Restart(_config);
                            SetTrayIcon(Color.Gray);
                            _trayIcon.Text = "PingMon";
                        }
                    }
                }
            }
            finally
            {
                _configFormOpen = false;
                _miConfigure.Enabled = true;
            }
        }

        private void HandleEraseAllSettings()
        {
            // Stop existing monitor
            _monitor?.Dispose();
            _monitor = null;
            _history.Clear();

            // Close stats window if open
            if (_statsForm != null && !_statsForm.IsDisposed)
            {
                _statsForm.Close();
                _statsForm = null;
            }

            // Delete config file
            ConfigStore.Delete();

            // Show setup wizard
            var newConfig = RunSetupWizard();
            if (newConfig == null)
            {
                // User cancelled wizard after erasing — exit application
                _trayIcon.Visible = false;
                Application.ExitThread();
                return;
            }

            _config = newConfig;
            ConfigStore.Save(_config);
            SetTrayIcon(Color.Gray);
            _trayIcon.Text = "PingMon";
            StartMonitor();
        }

        private void OpenStatsWindow()
        {
            if (_statsForm != null && !_statsForm.IsDisposed)
            {
                _statsForm.BringToFront();
                return;
            }
            _statsForm = new StatsForm(_monitor, _history, _config);
            _statsForm.Show();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _statsForm?.Dispose();
                _monitor?.Dispose();
                if (_currentIconHandle != IntPtr.Zero)
                {
                    DestroyIcon(_currentIconHandle);
                    _currentIconHandle = IntPtr.Zero;
                }
                _trayIcon?.Dispose();
                _menu?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
