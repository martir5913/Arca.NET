using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Arca.Core.Common;
using Arca.Core.Entities;

namespace Arca.NET.Services;

/// <summary>
/// Servidor embebido que permite que otras aplicaciones obtengan secretos
/// directamente desde la UI cuando el Daemon no está disponible.
/// Usa Named Pipes para comunicación ultra-rápida.
/// </summary>
public sealed class EmbeddedSecretServer : IDisposable
{
    private readonly ConcurrentDictionary<string, SecretEntry> _secrets = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _disposed;
    private volatile bool _isRunning;
    private readonly string _pipeName;

    public bool IsRunning => _isRunning;

    public EmbeddedSecretServer()
    {
        _pipeName = $"{ArcaConstants.PipeName}-simple";
    }

    /// <summary>
    /// Actualiza los secretos disponibles para servir.
    /// </summary>
    public void UpdateSecrets(IEnumerable<SecretEntry> secrets)
    {
        _secrets.Clear();
        foreach (var secret in secrets)
        {
            _secrets[secret.Key] = secret;
        }
        Debug.WriteLine($"[EmbeddedSecretServer] Secrets updated: {_secrets.Count} secrets loaded");
    }

    /// <summary>
    /// Inicia el servidor de secretos.
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _isRunning = true;
        _serverTask = Task.Run(() => RunServerLoopAsync(_cts.Token));
        
        Debug.WriteLine($"[EmbeddedSecretServer] Server started on pipe: {_pipeName}");
    }

    /// <summary>
    /// Detiene el servidor de secretos.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        Debug.WriteLine("[EmbeddedSecretServer] Stopping server...");
        
        _isRunning = false;
        _cts?.Cancel();
        
        try
        {
            // Crear una conexión dummy para desbloquear WaitForConnectionAsync
            using var dummyClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            dummyClient.Connect(100);
        }
        catch { }

        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
        
        Debug.WriteLine("[EmbeddedSecretServer] Server stopped");
    }

    private async Task RunServerLoopAsync(CancellationToken cancellationToken)
    {
        Debug.WriteLine($"[EmbeddedSecretServer] Server loop started, listening on: {_pipeName}");
        
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            
            try
            {
                pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                Debug.WriteLine("[EmbeddedSecretServer] Waiting for client connection...");
                
                await pipeServer.WaitForConnectionAsync(cancellationToken);
                
                if (!_isRunning || cancellationToken.IsCancellationRequested)
                {
                    pipeServer.Dispose();
                    break;
                }

                Debug.WriteLine("[EmbeddedSecretServer] Client connected!");
                
                // Manejar cliente de forma síncrona para simplificar
                await HandleClientAsync(pipeServer);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[EmbeddedSecretServer] Operation cancelled");
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmbeddedSecretServer] Error: {ex.Message}");
                await Task.Delay(100, cancellationToken);
            }
            finally
            {
                try
                {
                    if (pipeServer != null)
                    {
                        if (pipeServer.IsConnected)
                        {
                            pipeServer.Disconnect();
                        }
                        pipeServer.Dispose();
                    }
                }
                catch { }
            }
        }
        
        Debug.WriteLine("[EmbeddedSecretServer] Server loop ended");
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipeServer)
    {
        try
        {
            // Usar buffer para leer
            var buffer = new byte[4096];
            var bytesRead = await pipeServer.ReadAsync(buffer, 0, buffer.Length);
            
            if (bytesRead == 0)
            {
                Debug.WriteLine("[EmbeddedSecretServer] Empty request received");
                return;
            }

            var request = Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n');
            Debug.WriteLine($"[EmbeddedSecretServer] Request: {request}");

            var parts = request.Split('|', 2);
            var command = parts[0].ToUpperInvariant();

            string response;
            if (command == "GET" && parts.Length == 2)
                response = HandleGet(parts[1]);
            else if (command == "EXISTS" && parts.Length == 2)
                response = HandleExists(parts[1]);
            else if (command == "LIST")
                response = HandleList(parts.Length > 1 ? parts[1] : null);
            else if (command == "STATUS")
                response = HandleStatus();
            else
                response = "ERROR|Unknown command";

            Debug.WriteLine($"[EmbeddedSecretServer] Response: {response}");

            // Enviar respuesta
            var responseBytes = Encoding.UTF8.GetBytes(response + "\n");
            await pipeServer.WriteAsync(responseBytes, 0, responseBytes.Length);
            await pipeServer.FlushAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EmbeddedSecretServer] HandleClient error: {ex.Message}");
        }
    }

    private string HandleGet(string key)
    {
        if (_secrets.TryGetValue(key, out var secret))
        {
            return $"OK|{secret.Value}|{secret.Description ?? ""}";
        }
        return "NOTFOUND";
    }

    private string HandleExists(string key)
    {
        return _secrets.ContainsKey(key) ? "TRUE" : "FALSE";
    }

    private string HandleList(string? filter)
    {
        var keys = string.IsNullOrWhiteSpace(filter)
            ? _secrets.Keys
            : _secrets.Keys.Where(k => k.Contains(filter, StringComparison.OrdinalIgnoreCase));

        return $"OK|{string.Join(",", keys)}";
    }

    private string HandleStatus()
    {
        return $"OK|UNLOCKED|{_secrets.Count}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}
