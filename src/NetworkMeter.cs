using System.Net.NetworkInformation;

namespace SlimMonitorPC;

internal sealed class NetworkMeter
{
    private NetworkInterface? _adapter;
    private string? _adapterId;
    private long _lastReceived;
    private long _lastSent;
    private long _sessionReceived;
    private long _sessionSent;
    private DateTime _lastSampleUtc = DateTime.UtcNow;

    internal NetworkSnapshot Sample()
    {
        try
        {
            var adapter = FindActiveWifiAdapter();
            if (adapter is null)
            {
                Reset();
                return NetworkSnapshot.Disconnected;
            }

            var stats = adapter.GetIPv4Statistics();
            var nowUtc = DateTime.UtcNow;

            if (_adapterId != adapter.Id)
            {
                _adapter = adapter;
                _adapterId = adapter.Id;
                _lastReceived = stats.BytesReceived;
                _lastSent = stats.BytesSent;
                _sessionReceived = 0;
                _sessionSent = 0;
                _lastSampleUtc = nowUtc;
                return new NetworkSnapshot(adapter.Name, 0, 0, 0, 0, true);
            }

            var elapsed = Math.Max((nowUtc - _lastSampleUtc).TotalSeconds, 0.1);
            var receivedDelta = Math.Max(0, stats.BytesReceived - _lastReceived);
            var sentDelta = Math.Max(0, stats.BytesSent - _lastSent);

            _lastReceived = stats.BytesReceived;
            _lastSent = stats.BytesSent;
            _lastSampleUtc = nowUtc;
            _sessionReceived += receivedDelta;
            _sessionSent += sentDelta;
            _adapter = adapter;

            return new NetworkSnapshot(
                adapter.Name,
                receivedDelta / elapsed,
                sentDelta / elapsed,
                _sessionReceived,
                _sessionSent,
                true);
        }
        catch
        {
            return new NetworkSnapshot(_adapter?.Name ?? "Wi-Fi", 0, 0, _sessionReceived, _sessionSent, _adapter is not null);
        }
    }

    private void Reset()
    {
        _adapter = null;
        _adapterId = null;
        _lastReceived = 0;
        _lastSent = 0;
        _sessionReceived = 0;
        _sessionSent = 0;
        _lastSampleUtc = DateTime.UtcNow;
    }

    private static NetworkInterface? FindActiveWifiAdapter()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .ToArray();

        return adapters.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            ?? adapters.FirstOrDefault(n =>
            {
                var text = $"{n.Name} {n.Description}".ToLowerInvariant();
                return text.Contains("wi-fi") || text.Contains("wifi") || text.Contains("wireless") || text.Contains("wlan");
            });
    }

    internal static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:0}|B/s";
        var kb = bytesPerSecond / 1024d;
        if (kb < 1024) return $"{(kb < 10 ? kb.ToString("0.0") : kb.ToString("0"))}|KB/s";
        var mb = kb / 1024d;
        if (mb < 1024) return $"{(mb < 10 ? mb.ToString("0.0") : mb.ToString("0"))}|MB/s";
        return $"{mb / 1024d:0.00}|GB/s";
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var kb = bytes / 1024d;
        if (kb < 1024) return $"{kb:0.0} KB";
        var mb = kb / 1024d;
        if (mb < 1024) return $"{mb:0.0} MB";
        return $"{mb / 1024d:0.00} GB";
    }
}

internal readonly record struct NetworkSnapshot(
    string AdapterName,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    long SessionReceived,
    long SessionSent,
    bool Connected)
{
    internal static NetworkSnapshot Disconnected => new("Wi-Fi", 0, 0, 0, 0, false);
}
