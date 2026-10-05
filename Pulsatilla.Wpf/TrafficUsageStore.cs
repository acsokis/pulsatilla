using System.IO;
using System.Text.Json;

namespace Pulsatilla.Wpf;

public sealed class TrafficUsageStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly string _filePath = AppStorage.FilePath("usage-history.json");
    private readonly Dictionary<string, UsageBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private int _pendingWrites;

    public TrafficUsageStore()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var loaded = JsonSerializer.Deserialize<List<UsageBucket?>>(File.ReadAllText(_filePath)) ?? [];
            foreach (var bucket in loaded.Where(item => item is not null && item.MinuteUtc >= DateTime.UtcNow - Retention))
            {
                if (bucket is null) continue;
                _buckets[MakeKey(bucket)] = bucket;
            }
        }
        catch (Exception) { }
    }

    public void Record(string application, string executablePath, string host, string protocol,
        bool outbound, int byteCount, DateTime timestampUtc)
    {
        var minute = new DateTime(timestampUtc.Ticks - timestampUtc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        var bucket = new UsageBucket
        {
            MinuteUtc = minute,
            Application = application,
            ExecutablePath = executablePath,
            Host = host,
            Protocol = protocol
        };
        var key = MakeKey(bucket);
        lock (_sync)
        {
            if (!_buckets.TryGetValue(key, out var existing))
                _buckets[key] = existing = bucket;
            existing.LastSeenUtc = timestampUtc;
            if (outbound) existing.UploadBytes += byteCount;
            else existing.DownloadBytes += byteCount;
            _pendingWrites++;
            if (_buckets.Count > 50_000 || _pendingWrites % 10_000 == 0)
                TrimExpired();
        }
    }

    public IReadOnlyList<UsageSummary> Query(TimeSpan period)
    {
        var start = DateTime.UtcNow - period;
        lock (_sync)
        {
            return _buckets.Values
                .Where(bucket => bucket.MinuteUtc >= start)
                .GroupBy(bucket => new { bucket.Application, bucket.ExecutablePath, bucket.Host, bucket.Protocol })
                .Select(group => new UsageSummary(group.Key.Application, group.Key.ExecutablePath, group.Key.Host,
                    group.Key.Protocol, group.Sum(item => item.UploadBytes), group.Sum(item => item.DownloadBytes),
                    group.Max(item => item.LastSeenUtc)))
                .OrderByDescending(item => item.UploadBytes + item.DownloadBytes)
                .Take(2_000)
                .ToArray();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            UsageBucket[] snapshot;
            lock (_sync)
            {
                TrimExpired();
                snapshot = _buckets.Values.ToArray();
                _pendingWrites = 0;
            }

            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _filePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _filePath, true);
        }
        finally { _saveLock.Release(); }
    }

    private void TrimExpired()
    {
        var minimum = DateTime.UtcNow - Retention;
        foreach (var key in _buckets.Where(pair => pair.Value.MinuteUtc < minimum).Select(pair => pair.Key).ToArray())
            _buckets.Remove(key);
    }

    private static string MakeKey(UsageBucket bucket) =>
        $"{bucket.MinuteUtc:O}|{bucket.Application}|{bucket.ExecutablePath}|{bucket.Host}|{bucket.Protocol}";

    private sealed class UsageBucket
    {
        public DateTime MinuteUtc { get; set; }
        public string Application { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string Host { get; set; } = "";
        public string Protocol { get; set; } = "";
        public long UploadBytes { get; set; }
        public long DownloadBytes { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }
}

public sealed record UsageSummary(string Application, string ExecutablePath, string Host, string Protocol,
    long UploadBytes, long DownloadBytes, DateTime LastSeenUtc);
