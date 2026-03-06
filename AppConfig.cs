using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace PingMon
{
    [DataContract]
    public class HostEntry
    {
        [DataMember] public string Host { get; set; }
        [DataMember] public string Name { get; set; } = "";
        [DataMember] public int FailThreshold { get; set; } = 3;
        [DataMember] public int LatencyThresholdMs { get; set; } = 0;
        [DataMember] public bool Enabled { get; set; } = true;
    }

    [DataContract]
    public class AppConfig
    {
        public const int MaxHosts = 10;

        [DataMember] public List<HostEntry> Hosts { get; set; } = new List<HostEntry>();
        [DataMember] public int PingIntervalSeconds { get; set; } = 10;
        [DataMember] public int PingTimeoutMs { get; set; } = 2000;
        [DataMember] public int StatsWindowX { get; set; } = int.MinValue;
        [DataMember] public int StatsWindowY { get; set; } = int.MinValue;
    }

    public class HostStatus
    {
        public string Host;
        public string DisplayName;  // custom name if set, else same as Host
        public bool IsEnabled;
        public bool IsDown;
        public bool LatencyAlert;
        public long LastRoundtripMs;  // -1 if last ping failed
        public int ConsecutiveFailures;
        public DateTime LastChecked;

        public HostStatus Clone()
        {
            return new HostStatus
            {
                Host = Host,
                DisplayName = DisplayName,
                IsEnabled = IsEnabled,
                IsDown = IsDown,
                LatencyAlert = LatencyAlert,
                LastRoundtripMs = LastRoundtripMs,
                ConsecutiveFailures = ConsecutiveFailures,
                LastChecked = LastChecked
            };
        }
    }

    public static class ConfigStore
    {
        public static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PingMon");
        public static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(AppConfig));

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    using (var fs = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read))
                        return (AppConfig)Serializer.ReadObject(fs);
                }
            }
            catch { /* fall through to default */ }

            var def = new AppConfig();
            Save(def);
            return def;
        }

        public static void Save(AppConfig cfg)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                using (var fs = new FileStream(ConfigPath, FileMode.Create, FileAccess.Write))
                    Serializer.WriteObject(fs, cfg);
            }
            catch { /* best effort */ }
        }

        public static void Delete()
        {
            try { if (File.Exists(ConfigPath)) File.Delete(ConfigPath); }
            catch { /* best effort */ }
        }
    }

    public class PingMonitor : IDisposable
    {
        public event Action<HostStatus[], string, string, bool> HostStateChanged;  // snapshot, title, message, isDown
        public event Action<HostStatus[]> StatusChanged;

        private AppConfig _config;
        private readonly SynchronizationContext _uiContext;
        private System.Threading.Timer _timer;
        private readonly object _lock = new object();
        private int _isBusy;  // 0=idle, 1=busy (Interlocked)

        // Per-host state (indexed 0-4)
        private int[] _consecutiveFailures;
        private bool[] _isDown;
        private bool[] _latencyAlert;
        private HostStatus[] _results;

        public HostStatus[] Results
        {
            get { lock (_lock) { return _results; } }
        }

        public PingMonitor(AppConfig config, SynchronizationContext uiContext)
        {
            _config = config;
            _uiContext = uiContext;
            InitState();
        }

        private void InitState()
        {
            _consecutiveFailures = new int[AppConfig.MaxHosts];
            _isDown = new bool[AppConfig.MaxHosts];
            _latencyAlert = new bool[AppConfig.MaxHosts];
            _results = new HostStatus[AppConfig.MaxHosts];
            for (int i = 0; i < AppConfig.MaxHosts; i++)
                _results[i] = new HostStatus();
        }

        public void Start()
        {
            _timer = new System.Threading.Timer(TimerCallback, null,
                dueTime: 0,
                period: _config.PingIntervalSeconds * 1000);
        }

        public void Restart(AppConfig config)
        {
            _timer?.Dispose();
            _timer = null;
            _config = config;
            InitState();
            Start();
        }

        private void TimerCallback(object state)
        {
            if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0)
                return;

            try
            {
                var hosts = _config.Hosts;
                int count = Math.Min(hosts.Count, AppConfig.MaxHosts);
                int timeout = _config.PingTimeoutMs;

                // Fan out pings in parallel
                long[] roundtrips = new long[AppConfig.MaxHosts];
                bool[] success = new bool[AppConfig.MaxHosts];
                for (int i = 0; i < AppConfig.MaxHosts; i++) roundtrips[i] = -1;

                if (count > 0)
                {
                    using (var countdown = new CountdownEvent(count))
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (!hosts[i].Enabled)
                            {
                                countdown.Signal();
                                continue;
                            }
                            int idx = i;
                            string host = hosts[i].Host;
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                try
                                {
                                    using (var ping = new Ping())
                                    {
                                        var reply = ping.Send(host, timeout);
                                        if (reply != null && reply.Status == IPStatus.Success)
                                        {
                                            roundtrips[idx] = reply.RoundtripTime;
                                            success[idx] = true;
                                        }
                                    }
                                }
                                catch { /* ping failed */ }
                                finally { countdown.Signal(); }
                            });
                        }
                        countdown.Wait();
                    }
                }

                // Evaluate thresholds and build notifications
                var notifications = new List<Tuple<string, string, bool>>();
                HostStatus[] snapshot;

                lock (_lock)
                {
                    for (int i = 0; i < AppConfig.MaxHosts; i++)
                    {
                        if (i >= count || !hosts[i].Enabled)
                        {
                            string h = i < count ? hosts[i].Host : "";
                            string n = i < count && !string.IsNullOrWhiteSpace(hosts[i].Name) ? hosts[i].Name : h;
                            _results[i] = new HostStatus { Host = h, DisplayName = n, IsEnabled = false };
                            continue;
                        }

                        var entry = hosts[i];
                        bool ok = success[i];
                        long rtt = roundtrips[i];
                        bool wasDown = _isDown[i];
                        bool wasLatency = _latencyAlert[i];

                        if (ok)
                        {
                            _consecutiveFailures[i] = 0;
                            if (wasDown)
                            {
                                _isDown[i] = false;
                                notifications.Add(Tuple.Create(
                                    "PingMon Recovery",
                                    string.Format("{0} is responding again ({1} ms)", entry.Host, rtt),
                                    false));
                            }

                            bool latNow = entry.LatencyThresholdMs > 0 && rtt > entry.LatencyThresholdMs;
                            if (latNow && !wasLatency)
                            {
                                notifications.Add(Tuple.Create(
                                    "PingMon Warning",
                                    string.Format("{0} latency high: {1} ms (threshold: {2} ms)", entry.Host, rtt, entry.LatencyThresholdMs),
                                    false));
                            }
                            _latencyAlert[i] = latNow;
                        }
                        else
                        {
                            _consecutiveFailures[i]++;
                            if (!wasDown && _consecutiveFailures[i] >= entry.FailThreshold)
                            {
                                _isDown[i] = true;
                                notifications.Add(Tuple.Create(
                                    "PingMon Alert",
                                    string.Format("{0} is not responding ({1} consecutive failures)", entry.Host, _consecutiveFailures[i]),
                                    true));
                            }
                        }

                        _results[i] = new HostStatus
                        {
                            Host = entry.Host,
                            DisplayName = string.IsNullOrWhiteSpace(entry.Name) ? entry.Host : entry.Name,
                            IsEnabled = true,
                            IsDown = _isDown[i],
                            LatencyAlert = _latencyAlert[i],
                            LastRoundtripMs = rtt,
                            ConsecutiveFailures = _consecutiveFailures[i],
                            LastChecked = DateTime.Now
                        };
                    }

                    snapshot = new HostStatus[AppConfig.MaxHosts];
                    for (int i = 0; i < AppConfig.MaxHosts; i++)
                        snapshot[i] = _results[i].Clone();
                }

                // Marshal to UI thread
                _uiContext.Post(_ =>
                {
                    foreach (var n in notifications)
                        HostStateChanged?.Invoke(snapshot, n.Item1, n.Item2, n.Item3);
                    StatusChanged?.Invoke(snapshot);
                }, null);
            }
            finally
            {
                Interlocked.Exchange(ref _isBusy, 0);
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public class HistoryPoint
    {
        public DateTime Time;
        public long RoundtripMs;  // -1 = failed/down
    }

    public class PingHistory
    {
        private const int MaxPointsPerHost = 2000;
        private readonly Dictionary<string, Queue<HistoryPoint>> _data =
            new Dictionary<string, Queue<HistoryPoint>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        public void Add(HostStatus[] snapshot)
        {
            lock (_lock)
            {
                foreach (var s in snapshot)
                {
                    if (!s.IsEnabled || string.IsNullOrEmpty(s.Host)) continue;
                    if (!_data.TryGetValue(s.Host, out var q))
                        _data[s.Host] = q = new Queue<HistoryPoint>();

                    while (q.Count >= MaxPointsPerHost)
                        q.Dequeue();

                    q.Enqueue(new HistoryPoint
                    {
                        Time = s.LastChecked == default ? DateTime.Now : s.LastChecked,
                        RoundtripMs = s.IsDown ? -1 : s.LastRoundtripMs
                    });
                }
            }
        }

        public Dictionary<string, HistoryPoint[]> GetSnapshot()
        {
            lock (_lock)
            {
                var result = new Dictionary<string, HistoryPoint[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _data)
                    result[kv.Key] = kv.Value.ToArray();
                return result;
            }
        }

        public void Clear()
        {
            lock (_lock) { _data.Clear(); }
        }
    }

    public static class Traceroute
    {
        public static void Run(
            string target,
            int maxHops,
            int timeoutMs,
            Action<int, System.Net.IPAddress, long> onHop,   // ttl, address, rttMs (-1=no reply)
            Action<bool, string> onComplete,                  // success, errorMessage
            System.Threading.CancellationToken ct)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    for (int ttl = 1; ttl <= maxHops; ttl++)
                    {
                        if (ct.IsCancellationRequested) { onComplete(false, "Cancelled"); return; }

                        IPStatus status = IPStatus.Unknown;
                        System.Net.IPAddress addr = null;
                        long rtt = -1;

                        try
                        {
                            using (var ping = new Ping())
                            {
                                var opts = new PingOptions(ttl, dontFragment: true);
                                var reply = ping.Send(target, timeoutMs, new byte[32], opts);
                                if (reply != null)
                                {
                                    status = reply.Status;
                                    addr = reply.Address;
                                    rtt = reply.RoundtripTime;
                                }
                            }
                        }
                        catch { /* non-responding hop — continue */ }

                        if (addr != null && !addr.Equals(System.Net.IPAddress.Any))
                            onHop(ttl, addr, rtt);

                        if (status == IPStatus.Success) { onComplete(true, null); return; }

                        if (status != IPStatus.TtlExpired &&
                            status != IPStatus.TimedOut &&
                            status != IPStatus.Unknown)
                        {
                            onComplete(false, "Stopped at hop " + ttl + ": " + status);
                            return;
                        }
                    }
                    onComplete(false, "Max hops reached without reaching target.");
                }
                catch (Exception ex) { onComplete(false, ex.Message); }
            });
        }
    }
}
