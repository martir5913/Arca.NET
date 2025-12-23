using Arca.Core.Common;
using System.Diagnostics;
using System.IO;
using SecretEntry = Arca.Core.Entities.SecretEntry;

namespace Arca.NET.Services;

/// <summary>
/// Servicio para comunicarse con el Daemon y sincronizar el estado del vault.
/// </summary>
public sealed class DaemonSyncService : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Verifica si el Daemon está corriendo.
    /// </summary>
    public bool IsDaemonRunning()
    {
        var processes = Process.GetProcessesByName(ArcaConstants.DaemonProcessName);
        return processes.Length > 0;
    }

    /// <summary>
    /// Intenta iniciar el Daemon si no está corriendo.
    /// </summary>
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

            // Esperar un poco para que inicie
            Thread.Sleep(1000);

            return IsDaemonRunning();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Notifica al Daemon que el vault fue desbloqueado.
    /// </summary>
    public async Task NotifyUnlockAsync(byte[] derivedKey, IEnumerable<SecretEntry> secrets, string vaultPath)
    {
        // El Daemon necesita recibir los secretos descifrados para servirlos
        // Por ahora, la UI actúa como el "desbloqueador" y el Daemon sirve los datos        

        await Task.CompletedTask;
    }

    public async Task NotifyLockAsync()
    {
        // TODO: Implementar notificación de bloqueo
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
