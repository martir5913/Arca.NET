using Arca.Core.Common;
using Arca.Core.Entities;
using Arca.Core.Security;
using Arca.Core.Services;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Arca.NET.Services;

/// <summary>
/// Servidor embebido que permite que otras aplicaciones obtengan secretos
/// directamente desde la UI cuando el Daemon no está disponible.
/// Usa Named Pipes para comunicación ultra-rápida.
/// Requiere autenticación via API Key.
/// Incluye sistema de auditoría completo.
/// </summary>
public sealed class EmbeddedSecretServer : IDisposable
{
    private readonly ConcurrentDictionary<string, SecretEntry> _secrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ApiKeyEntry> _apiKeys = new(); // KeyHash -> ApiKeyEntry
    private readonly AuditService _auditService;
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

    public AuditService AuditService => _auditService;

    public EmbeddedSecretServer()
    {
        _pipeName = $"{ArcaConstants.PipeName}-simple";
        _auditService = new AuditService();
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
    /// Verifica si una API Key es válida y retorna su información.
    /// </summary>
    private (bool IsValid, ApiKeyEntry? Entry) ValidateApiKeyWithInfo(string apiKey)
    {
        if (!RequireAuthentication)
            return (true, null);

        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, null);

        var keyHash = ApiKeyService.ComputeHash(apiKey);
        if (_apiKeys.TryGetValue(keyHash, out var entry))
        {
            return (true, entry);
        }

        return (false, null);
    }

    /// <summary>
    /// Registra el uso de una API Key.
    /// </summary>
    public event EventHandler<string>? ApiKeyUsed;

    public void Start()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _isRunning = true;
        _serverTask = Task.Run(() => RunServerLoopAsync(_cts.Token));

        Debug.WriteLine($"[EmbeddedSecretServer] Server started on pipe: {_pipeName}");
        Debug.WriteLine($"[EmbeddedSecretServer] Authentication required: {RequireAuthentication}");
    }

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
                    LogAudit("Unknown", "", command, null, false, "API Key not provided");
                }
                else
                {
                    var apiKey = parts[1];
                    var (isValid, keyEntry) = ValidateApiKeyWithInfo(apiKey);

                    if (!isValid)
                    {
                        response = "ERROR|Invalid or expired API Key";
                        LogAudit("Invalid", "", command, parts.Length > 2 ? parts[2] : null, false, "Invalid API Key");
                        Debug.WriteLine("[EmbeddedSecretServer] Unauthorized access attempt");
                    }
                    else
                    {
                        // Notificar uso de API Key
                        var keyHash = ApiKeyService.ComputeHash(apiKey);
                        ApiKeyUsed?.Invoke(this, keyHash);

                        // Procesar comando autenticado
                        response = ProcessAuthenticatedCommand(command, parts, keyEntry!);
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

    private string ProcessAuthenticatedCommand(string command, string[] parts, ApiKeyEntry keyEntry)
    {
        // Formato: COMANDO|API_KEY|PARAM1|PARAM2...
        var secretKey = parts.Length > 2 ? parts[2] : null;
        string response;
        bool success;

        switch (command)
        {
            case "GET" when parts.Length >= 3:
                // Verificar permiso para acceder a este secreto
                if (!HasPermissionToAccess(keyEntry, parts[2]))
                {
                    response = "ERROR|Access denied to this secret";
                    LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "GET", parts[2], false, "Access denied - insufficient permissions");
                    break;
                }
                response = HandleGet(parts[2]);
                success = response.StartsWith("OK");
                LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "GET", parts[2], success,
                    success ? null : response.Replace("NOTFOUND", "Secret not found"));
                break;

            case "EXISTS" when parts.Length >= 3:
                // Verificar permiso para verificar este secreto
                if (!HasPermissionToAccess(keyEntry, parts[2]))
                {
                    response = "FALSE"; // No revelar si existe o no
                    LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "EXISTS", parts[2], false, "Access denied - insufficient permissions");
                    break;
                }
                response = HandleExists(parts[2]);
                success = true;
                LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "EXISTS", parts[2], true, null);
                break;

            case "LIST":
            case "KEYS":
                // Verificar si puede listar secretos
                if (!CanListSecrets(keyEntry))
                {
                    response = "ERROR|Access denied - cannot list secrets";
                    LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "LIST", null, false, "Access denied - cannot list secrets");
                    break;
                }
                // Filtrar solo los secretos que tiene permiso de ver
                response = HandleListWithPermissions(keyEntry, parts.Length > 2 ? parts[2] : null);
                LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "LIST", null, true, null);
                break;

            default:
                response = "ERROR|Unknown command";
                LogAudit(keyEntry.Name, keyEntry.Id.ToString(), command, secretKey, false, "Unknown command");
                break;
        }

        return response;
    }

    /// <summary>
    /// Verifica si la API Key tiene permiso para acceder a un secreto específico.
    /// </summary>
    private bool HasPermissionToAccess(ApiKeyEntry keyEntry, string secretKey)
    {
        var permissions = keyEntry.Permissions;

        // Full access permite todo
        if (permissions.Level == AccessLevel.Full)
            return true;

        // Verificar en lista de secretos permitidos
        if (permissions.AllowedSecrets.Any(s => 
            s.Equals(secretKey, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Verificar prefijos permitidos (ej: "ConnectionStrings:*" permite "ConnectionStrings:Database")
        foreach (var prefix in permissions.AllowedPrefixes)
        {
            var prefixPattern = prefix.TrimEnd('*');
            if (secretKey.StartsWith(prefixPattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Verifica si la API Key puede listar secretos.
    /// </summary>
    private bool CanListSecrets(ApiKeyEntry keyEntry)
    {
        return keyEntry.Permissions.CanList;
    }

    /// <summary>
    /// Lista solo los secretos que la API Key tiene permiso de ver.
    /// </summary>
    private string HandleListWithPermissions(ApiKeyEntry keyEntry, string? filter)
    {
        var permissions = keyEntry.Permissions;
        
        IEnumerable<string> keys;

        if (permissions.Level == AccessLevel.Full)
        {
            // Acceso completo - mostrar todos
            keys = _secrets.Keys;
        }
        else
        {
            // Filtrar solo los secretos permitidos
            keys = _secrets.Keys.Where(k => HasPermissionToAccess(keyEntry, k));
        }

        // Aplicar filtro adicional si se especificó
        if (!string.IsNullOrWhiteSpace(filter))
        {
            keys = keys.Where(k => k.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        return $"OK|{string.Join(",", keys)}";
    }

    private string ProcessUnauthenticatedCommand(string command, string[] parts)
    {
        // Formato sin auth: COMANDO|PARAM1|PARAM2...
        var secretKey = parts.Length > 1 ? parts[1] : null;

        switch (command)
        {
            case "GET" when parts.Length >= 2:
                var getResponse = HandleGet(parts[1]);
                LogAudit("Anonymous", "N/A", "GET", parts[1], getResponse.StartsWith("OK"), null);
                return getResponse;

            case "EXISTS" when parts.Length >= 2:
                var existsResponse = HandleExists(parts[1]);
                LogAudit("Anonymous", "N/A", "EXISTS", parts[1], true, null);
                return existsResponse;

            case "LIST":
                LogAudit("Anonymous", "N/A", "LIST", null, true, null);
                return HandleList(parts.Length > 1 ? parts[1] : null);

            case "KEYS":
                LogAudit("Anonymous", "N/A", "LIST", null, true, null);
                return HandleList(parts.Length > 1 ? parts[1] : null);

            default:
                LogAudit("Anonymous", "N/A", command, secretKey, false, "Unknown command");
                return "ERROR|Unknown command";
        }
    }

    private void LogAudit(string apiKeyName, string apiKeyId, string action, string? secretKey, bool success, string? error)
    {
        _auditService.Log(apiKeyName, apiKeyId, action, secretKey, success, error);
    }

    private string HandleStatus()
    {
        var authRequired = RequireAuthentication ? "AUTH_REQUIRED" : "NO_AUTH";
        return $"OK|UNLOCKED|{_secrets.Count}|{authRequired}";
    }

    private string HandleAuth(string apiKey)
    {
        var (isValid, keyEntry) = ValidateApiKeyWithInfo(apiKey);

        if (isValid)
        {
            LogAudit(keyEntry?.Name ?? "Unknown", keyEntry?.Id.ToString() ?? "", "AUTH", null, true, null);
            return "OK|AUTHENTICATED";
        }

        LogAudit("Invalid", "", "AUTH", null, false, "Invalid API Key");
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
        _auditService.Dispose();
    }
}
