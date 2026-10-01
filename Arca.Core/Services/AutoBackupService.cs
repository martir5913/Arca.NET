using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace Arca.Core.Services;

public sealed class AutoBackupService : IDisposable
{
    private readonly string _vaultDirectory;
    private string _backupDirectory;
    private int _maxSnapshots;
    private bool _enabled;
    private readonly Timer _timer;
    private bool _isDisposed;

    public DateTime? LastBackupTime { get; private set; }

    public AutoBackupService(
        string? vaultDirectory = null,
        int maxSnapshots = 15,
        int intervalHours = 6,
        string? customBackupDir = null,
        bool enabled = true)
    {
        _vaultDirectory = vaultDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Arca");

        _backupDirectory = !string.IsNullOrWhiteSpace(customBackupDir)
            ? customBackupDir
            : Path.Combine(_vaultDirectory, "Backups");

        _maxSnapshots = maxSnapshots;
        _enabled = enabled;

        try
        {
            Directory.CreateDirectory(_backupDirectory);
        }
        catch { }

        // Start background snapshot timer (default 6 hours, initial delay 2 minutes)
        var intervalTime = TimeSpan.FromHours(intervalHours > 0 ? intervalHours : 6);
        _timer = new Timer(_ => OnTimerTick(), null, TimeSpan.FromMinutes(2), intervalTime);
    }

    public string BackupDirectory => _backupDirectory;
    public bool IsEnabled => _enabled;
    public int MaxSnapshots => _maxSnapshots;

    public void Configure(string? customBackupDir, int intervalHours, int maxSnapshots, bool enabled)
    {
        _maxSnapshots = maxSnapshots > 0 ? maxSnapshots : 15;
        _enabled = enabled;

        _backupDirectory = !string.IsNullOrWhiteSpace(customBackupDir)
            ? customBackupDir
            : Path.Combine(_vaultDirectory, "Backups");

        try
        {
            Directory.CreateDirectory(_backupDirectory);
        }
        catch { }

        if (enabled && intervalHours > 0)
        {
            var intervalTime = TimeSpan.FromHours(intervalHours);
            _timer.Change(intervalTime, intervalTime);
        }
        else
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnTimerTick()
    {
        if (_enabled)
        {
            CreateSnapshot();
        }
    }

    public int GetSnapshotCount()
    {
        if (!Directory.Exists(_backupDirectory)) return 0;
        return Directory.GetFiles(_backupDirectory, "vault_auto_*.vlt.bak").Length;
    }

    public bool CreateSnapshot()
    {
        if (!_enabled) return false;

        try
        {
            var vaultFile = Path.Combine(_vaultDirectory, "vault.vlt");
            if (!File.Exists(vaultFile) || new FileInfo(vaultFile).Length == 0)
                return false;

            Directory.CreateDirectory(_backupDirectory);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // 1. Snapshot vault.vlt
            var backupVaultPath = Path.Combine(_backupDirectory, $"vault_auto_{timestamp}.vlt.bak");
            File.Copy(vaultFile, backupVaultPath, overwrite: true);

            // 2. Snapshot vault.keys
            var keysFile = Path.Combine(_vaultDirectory, "vault.keys");
            if (File.Exists(keysFile) && new FileInfo(keysFile).Length > 0)
            {
                var backupKeysPath = Path.Combine(_backupDirectory, $"vault_auto_{timestamp}.keys.bak");
                File.Copy(keysFile, backupKeysPath, overwrite: true);
            }

            // 3. Snapshot folders.json
            var foldersFile = Path.Combine(_vaultDirectory, "folders.json");
            if (File.Exists(foldersFile))
            {
                var backupFoldersPath = Path.Combine(_backupDirectory, $"folders_auto_{timestamp}.json.bak");
                File.Copy(foldersFile, backupFoldersPath, overwrite: true);
            }

            LastBackupTime = DateTime.Now;

            // 4. Prune snapshots older than maxSnapshots
            PruneOldBackups();

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoBackupService] Error creating snapshot: {ex.Message}");
            return false;
        }
    }

    private void PruneOldBackups()
    {
        try
        {
            var snapshots = Directory.GetFiles(_backupDirectory, "vault_auto_*.vlt.bak")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();

            if (snapshots.Count > _maxSnapshots)
            {
                var toDelete = snapshots.Skip(_maxSnapshots);
                foreach (var file in toDelete)
                {
                    try
                    {
                        var baseName = Path.GetFileNameWithoutExtension(file.Name);
                        var timestamp = baseName.Replace("vault_auto_", "").Replace(".vlt", "");

                        file.Delete();

                        var companionKeys = Path.Combine(_backupDirectory, $"vault_auto_{timestamp}.keys.bak");
                        if (File.Exists(companionKeys)) File.Delete(companionKeys);

                        var companionFolders = Path.Combine(_backupDirectory, $"folders_auto_{timestamp}.json.bak");
                        if (File.Exists(companionFolders)) File.Delete(companionFolders);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _timer?.Dispose();
            _isDisposed = true;
        }
    }
}
