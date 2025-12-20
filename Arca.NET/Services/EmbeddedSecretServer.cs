using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Arca.Core.Common;
using Arca.Core.Entities;
using Arca.Core.Security;

namespace Arca.NET.Services;

/// <summary>
/// Servidor embebido que permite que otras aplicaciones obtengan secretos
/// directamente desde la UI cuando el Daemon no está disponible.
/// Usa Named Pipes para comunicación ultra-rápida.
/// Requiere autenticación via API Key.
/// </summary>
public sealed class EmbeddedSecretServer : IDisposable
{
    private readonly ConcurrentDictionary<string, SecretEntry> _secrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ApiKeyEntry> _apiKeys = new(); // KeyHash -> ApiKeyEntry
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _disposed;
    private volatile bool _isRunning;
    private readonly string _pipeName;

    /// <summary>
    /// Si es true, requiere autenticación para acceder a secretos.
    /// Si es false, cualquier proceso local puede acceder (modo desarrollo).
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

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
    /// Actualiza las API Keys autorizadas.
    /// </summary>
    public void UpdateApiKeys(IEnumerable<ApiKeyEntry> apiKeys)
    {
        _apiKeys.Clear();
        foreach (var key in apiKeys.Where(k => k.IsActive))
        {
            _apiKeys[key.KeyHash] = key;
        }
        Debug.WriteLine($"[EmbeddedSecretServer] API Keys updated: {_apiKeys.Count} active keys");
    }

    /// <summary>
    /// Verifica si una API Key es válida.
    /// </summary>
    public bool ValidateApiKey(string apiKey)
    {
        if (!RequireAuthentication)
            return true;

        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        var keyHash = ApiKeyService.ComputeHash(apiKey);
        return _apiKeys.ContainsKey(keyHash);
    }

    /// <summary>
    /// Registra el uso de una API Key.
    /// </summary>
    public event EventHandler<string>? ApiKeyUsed;

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
        Debug.WriteLine($"[EmbeddedSecretServer] Authentication required: {RequireAuthentication}");
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

                await pipeServer.WaitForConnectionAsync(cancellationToken);
                
                if (!_isRunning || cancellationToken.IsCancellationRequested)
                {
                    pipeServer.Dispose();
                    break;
                }

                // Manejar cliente
                await HandleClientAsync(pipeServer);
            }
            catch (OperationCanceledException)
            {
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
            // Leer request
            var buffer = new byte[4096];
            var bytesRead = await pipeServer.ReadAsync(buffer, 0, buffer.Length);
            
            if (bytesRead == 0)
            {
                return;
            }

            var request = Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n');
            Debug.WriteLine($"[EmbeddedSecretServer] Request: {request}");

            // Parsear comando: COMANDO|API_KEY|PARAMETROS
            var parts = request.Split('|');
            var command = parts[0].ToUpperInvariant();

            string response;

            // STATUS no requiere autenticación (para verificar si el servidor está corriendo)
            if (command == "STATUS")
            {
                response = HandleStatus();
            }
            // AUTH verifica si una API Key es válida
            else if (command == "AUTH" && parts.Length >= 2)
            {
                response = HandleAuth(parts[1]);
            }
            // Todos los demás comandos requieren autenticación
            else if (RequireAuthentication)
            {
                if (parts.Length < 2)
                {
                    response = "ERROR|API Key required. Use: COMMAND|API_KEY|PARAMS";
                }
                else
                {
                    var apiKey = parts[1];
                    if (!ValidateApiKey(apiKey))
                    {
                        response = "ERROR|Invalid or expired API Key";
                        Debug.WriteLine("[EmbeddedSecretServer] Unauthorized access attempt");
                    }
                    else
                    {
                        // Notificar uso de API Key
                        var keyHash = ApiKeyService.ComputeHash(apiKey);
                        ApiKeyUsed?.Invoke(this, keyHash);
                        
                        // Procesar comando autenticado
                        response = ProcessAuthenticatedCommand(command, parts);
                    }
                }
            }
            else
            {
                // Modo sin autenticación (desarrollo)
                response = ProcessUnauthenticatedCommand(command, parts);
            }

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

    private string ProcessAuthenticatedCommand(string command, string[] parts)
    {
        // Formato: COMANDO|API_KEY|PARAM1|PARAM2...
        return command switch
        {
            "GET" when parts.Length >= 3 => HandleGet(parts[2]),
            "EXISTS" when parts.Length >= 3 => HandleExists(parts[2]),
            "LIST" => HandleList(parts.Length > 2 ? parts[2] : null),
            "KEYS" => HandleList(parts.Length > 2 ? parts[2] : null),
            _ => "ERROR|Unknown command"
        };
    }

    private string ProcessUnauthenticatedCommand(string command, string[] parts)
    {
        // Formato sin auth: COMANDO|PARAM1|PARAM2...
        return command switch
        {
            "GET" when parts.Length >= 2 => HandleGet(parts[1]),
            "EXISTS" when parts.Length >= 2 => HandleExists(parts[1]),
            "LIST" => HandleList(parts.Length > 1 ? parts[1] : null),
            "KEYS" => HandleList(parts.Length > 1 ? parts[1] : null),
            _ => "ERROR|Unknown command"
        };
    }

    private string HandleStatus()
    {
        var authRequired = RequireAuthentication ? "AUTH_REQUIRED" : "NO_AUTH";
        return $"OK|UNLOCKED|{_secrets.Count}|{authRequired}";
    }

    private string HandleAuth(string apiKey)
    {
        if (ValidateApiKey(apiKey))
        {
            return "OK|AUTHENTICATED";
        }
        return "ERROR|Invalid API Key";
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}
