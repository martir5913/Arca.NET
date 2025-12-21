using Arca.Core.Entities;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Arca.Core.Services;

public sealed class AuditService : IDisposable
{
    private readonly string _logDirectory;
    private readonly ConcurrentQueue<AuditLogEntry> _pendingLogs = new();
    private readonly List<AuditLogEntry> _recentLogs = new();
    private readonly object _lock = new();
    private readonly Timer _flushTimer;
    private readonly int _maxRecentLogs;
    private bool _disposed;

    public AuditService(string? logDirectory = null, int maxRecentLogs = 1000)
    {
        _logDirectory = logDirectory ?? GetDefaultLogDirectory();
        _maxRecentLogs = maxRecentLogs;

        // Asegurar que el directorio existe
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        // Cargar logs recientes del día actual
        LoadTodaysLogs();

        // Timer para flush periódico (cada 5 segundos)
        _flushTimer = new Timer(_ => FlushLogs(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Log(
        string apiKeyName,
        string apiKeyId,
        string action,
        string? secretKey,
        bool success,
        string? errorMessage = null)
    {
        var entry = new AuditLogEntry(
            Guid.NewGuid(),
            DateTime.UtcNow,
            apiKeyName,
            apiKeyId,
            action,
            secretKey,
            success,
            errorMessage);

        _pendingLogs.Enqueue(entry);

        lock (_lock)
        {
            _recentLogs.Add(entry);

            // Mantener solo los logs más recientes en memoria
            while (_recentLogs.Count > _maxRecentLogs)
            {
                _recentLogs.RemoveAt(0);
            }
        }
    }

    public IReadOnlyList<AuditLogEntry> GetRecentLogs(int count = 100)
    {
        lock (_lock)
        {
            return _recentLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToList();
        }
    }

    public IReadOnlyList<AuditLogEntry> GetLogs(
        DateTime? from = null,
        DateTime? to = null,
        string? apiKeyName = null,
        string? action = null,
        string? secretKey = null,
        bool? successOnly = null,
        int maxResults = 100)
    {
        lock (_lock)
        {
            var query = _recentLogs.AsEnumerable();

            if (from.HasValue)
                query = query.Where(l => l.Timestamp >= from.Value);

            if (to.HasValue)
                query = query.Where(l => l.Timestamp <= to.Value);

            if (!string.IsNullOrEmpty(apiKeyName))
                query = query.Where(l => l.ApiKeyName.Contains(apiKeyName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(action))
                query = query.Where(l => l.Action.Equals(action, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(secretKey))
                query = query.Where(l => l.SecretKey?.Contains(secretKey, StringComparison.OrdinalIgnoreCase) == true);

            if (successOnly.HasValue)
                query = query.Where(l => l.Success == successOnly.Value);

            return query
                .OrderByDescending(l => l.Timestamp)
                .Take(maxResults)
                .ToList();
        }
    }

    public AuditStatistics GetStatistics(DateTime? since = null)
    {
        lock (_lock)
        {
            var logs = since.HasValue
                ? _recentLogs.Where(l => l.Timestamp >= since.Value).ToList()
                : _recentLogs;

            return new AuditStatistics
            {
                TotalRequests = logs.Count,
                SuccessfulRequests = logs.Count(l => l.Success),
                FailedRequests = logs.Count(l => !l.Success),
                UniqueApiKeys = logs.Select(l => l.ApiKeyName).Distinct().Count(),
                UniqueSecrets = logs.Where(l => l.SecretKey != null).Select(l => l.SecretKey).Distinct().Count(),
                RequestsByAction = logs.GroupBy(l => l.Action)
                    .ToDictionary(g => g.Key, g => g.Count()),
                RequestsByApiKey = logs.GroupBy(l => l.ApiKeyName)
                    .ToDictionary(g => g.Key, g => g.Count()),
                MostAccessedSecrets = logs.Where(l => l.SecretKey != null)
                    .GroupBy(l => l.SecretKey!)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }
    }

    public void FlushLogs()
    {
        var logsToWrite = new List<AuditLogEntry>();

        while (_pendingLogs.TryDequeue(out var log))
        {
            logsToWrite.Add(log);
        }

        if (logsToWrite.Count == 0)
            return;

        try
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var logFile = Path.Combine(_logDirectory, $"audit-{today}.json");

            // Append logs to file
            using var stream = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);

            foreach (var log in logsToWrite)
            {
                var json = JsonSerializer.Serialize(log);
                writer.WriteLine(json);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuditService] Error writing logs: {ex.Message}");
        }
    }

    private void LoadTodaysLogs()
    {
        try
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var logFile = Path.Combine(_logDirectory, $"audit-{today}.json");

            if (!File.Exists(logFile))
                return;

            var lines = File.ReadAllLines(logFile);

            lock (_lock)
            {
                foreach (var line in lines.TakeLast(_maxRecentLogs))
                {
                    try
                    {
                        var entry = JsonSerializer.Deserialize<AuditLogEntry>(line);
                        if (entry != null)
                        {
                            _recentLogs.Add(entry);
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuditService] Error loading logs: {ex.Message}");
        }
    }

    public IReadOnlyList<AuditLogEntry> LoadLogsFromDate(DateTime date)
    {
        var logs = new List<AuditLogEntry>();

        try
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var logFile = Path.Combine(_logDirectory, $"audit-{dateStr}.json");

            if (!File.Exists(logFile))
                return logs;

            var lines = File.ReadAllLines(logFile);

            foreach (var line in lines)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<AuditLogEntry>(line);
                    if (entry != null)
                    {
                        logs.Add(entry);
                    }
                }
                catch { }
            }
        }
        catch { }

        return logs;
    }

    public IReadOnlyList<(DateTime Date, string FilePath, long SizeBytes)> GetAvailableLogFiles()
    {
        var files = new List<(DateTime, string, long)>();

        try
        {
            var logFiles = Directory.GetFiles(_logDirectory, "audit-*.json");

            foreach (var file in logFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var dateStr = fileName.Replace("audit-", "");

                if (DateTime.TryParse(dateStr, out var date))
                {
                    var info = new FileInfo(file);
                    files.Add((date, file, info.Length));
                }
            }
        }
        catch { }

        return files.OrderByDescending(f => f.Item1).ToList();
    }

    private static string GetDefaultLogDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Arca", "Logs");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flushTimer.Dispose();
        FlushLogs(); // Guardar logs pendientes antes de cerrar
    }
}

public class AuditStatistics
{
    public int TotalRequests { get; init; }
    public int SuccessfulRequests { get; init; }
    public int FailedRequests { get; init; }
    public int UniqueApiKeys { get; init; }
    public int UniqueSecrets { get; init; }
    public Dictionary<string, int> RequestsByAction { get; init; } = new();
    public Dictionary<string, int> RequestsByApiKey { get; init; } = new();
    public Dictionary<string, int> MostAccessedSecrets { get; init; } = new();
}
