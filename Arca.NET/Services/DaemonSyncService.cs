using Arca.Core.Common;
using System.Diagnostics;
using System.IO;
using SecretEntry = Arca.Core.Entities.SecretEntry;

namespace Arca.NET.Services;

public sealed class DaemonSyncService : IDisposable
{
    private bool _disposed;

    public bool IsDaemonRunning()
    {
        var processes = Process.GetProcessesByName(ArcaConstants.DaemonProcessName);
        return processes.Length > 0;
    }

    public bool TryStartDaemon()
    {
        if (IsDaemonRunning())
            return true;

        try
        {
            var daemonPath = GetDaemonPath();
            if (daemonPath is null || !File.Exists(daemonPath))
                return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = daemonPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(startInfo);

            Thread.Sleep(1000);

            return IsDaemonRunning();
        }
        catch
        {
            return false;
        }
    }

    public async Task NotifyUnlockAsync(byte[] derivedKey, IEnumerable<SecretEntry> secrets, string vaultPath)
    {
        await Task.CompletedTask;
    }

    public async Task NotifyLockAsync()
    {
        await Task.CompletedTask;
    }

    private static string? GetDaemonPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var daemonPath = Path.Combine(baseDir, "..", "Arca.Daemon", $"{ArcaConstants.DaemonProcessName}.exe");

        if (File.Exists(daemonPath))
            return Path.GetFullPath(daemonPath);

        // Buscar en la misma carpeta
        daemonPath = Path.Combine(baseDir, $"{ArcaConstants.DaemonProcessName}.exe");
        if (File.Exists(daemonPath))
            return daemonPath;

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
