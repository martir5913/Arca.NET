using Arca.Core.Common;
using Arca.Core.Entities;
using Arca.Core.Security;
using Arca.Core.Services;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Arca.NET.Services;

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

    public bool RequireAuthentication { get; set; } = true;

    public bool IsRunning => _isRunning;

    public AuditService AuditService => _auditService;

    public EmbeddedSecretServer(string? customPipeName = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(customPipeName)
            ? ArcaConstants.GetUserPipeName()
            : customPipeName;
        _auditService = new AuditService();
    }

    public void UpdateSecrets(IEnumerable<SecretEntry> secrets)
    {
        _secrets.Clear();
        foreach (var secret in secrets)
        {
            _secrets[secret.FullKey] = secret;
        }
        Debug.WriteLine($"[EmbeddedSecretServer] Secrets updated: {_secrets.Count} entries indexed");
    }

    public void UpdateApiKeys(IEnumerable<ApiKeyEntry> apiKeys)
    {
        _apiKeys.Clear();
        foreach (var key in apiKeys.Where(k => k.IsActive))
        {
            _apiKeys[key.KeyHash] = key;
        }
        Debug.WriteLine($"[EmbeddedSecretServer] API Keys updated: {_apiKeys.Count} active keys");
    }

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

    public event EventHandler<string>? ApiKeyUsed;

    public void Start()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _isRunning = true;
        _serverTask = Task.Run(() => RunServerLoopAsync(_cts.Token));

        Debug.WriteLine($"[EmbeddedSecretServer] Server started on pipe: {_pipeName}");

        if (!RequireAuthentication)
        {
            Debug.WriteLine("[EmbeddedSecretServer] ?? WARNING: Authentication is DISABLED. Any application can access secrets.");
            Debug.WriteLine("[EmbeddedSecretServer] ?? This should only be used for development purposes.");
            LogAudit("SYSTEM", "", "SERVER_START", null, true, "WARNING: Server started WITHOUT authentication");
        }
        else
        {
            Debug.WriteLine($"[EmbeddedSecretServer] Authentication required: {_apiKeys.Count} API Keys configured");
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        Debug.WriteLine("[EmbeddedSecretServer] Stopping server...");
        _isRunning = false;
        _cts?.Cancel();

        // Enviar un ping dummy para desbloquear WaitForConnectionAsync
        try
        {
            using var dummyClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            dummyClient.Connect(100);
        }
        catch { }

        try
        {
            _serverTask?.Wait(500);
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
                pipeServer = CreatePipeServerStream();

                await pipeServer.WaitForConnectionAsync(cancellationToken);

                if (!_isRunning || cancellationToken.IsCancellationRequested)
                {
                    pipeServer.Dispose();
                    break;
                }

                // Cada cliente se atiende en paralelo sin bloquear el loop principal.
                var clientPipe = pipeServer;
                pipeServer = null;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleClientAsync(clientPipe);
                    }
                    finally
                    {
                        try
                        {
                            if (clientPipe.IsConnected) clientPipe.Disconnect();
                            clientPipe.Dispose();
                        }
                        catch { }
                    }
                });
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
                try { pipeServer?.Dispose(); }
                catch { }
            }
        }

        Debug.WriteLine("[EmbeddedSecretServer] Server loop ended");
    }

    private NamedPipeServerStream CreatePipeServerStream()
    {
        if (OperatingSystem.IsWindows())
        {
            var pipeSecurity = new PipeSecurity();

            // 1. Permitir acceso Read/Write a Todos (WorldSid: Cuentas virtuales de IIS AppPool, IIS_IUSRS, servicios y usuarios locales)
            var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                worldSid,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            // 2. Permitir acceso Read/Write a usuarios autenticados locales
            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                authenticatedUsersSid,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            // 3. Permitir acceso Read/Write al grupo BuiltinUsers
            var builtinUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                builtinUsersSid,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            // 4. Otorgar FullControl al usuario actual que ejecuta el servidor
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    currentUser,
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));
            }

            return NamedPipeServerStreamAcl.Create(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity: pipeSecurity);
        }

        return new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
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

            var parts = request.Split('|');
            var command = parts[0].ToUpperInvariant();
            string response;

            // STATUS y AUTH se manejan antes de verificar permisos de API Key
            if (command == "STATUS")
            {
                response = HandleStatus();
            }
            else if (command == "AUTH" && parts.Length >= 2)
            {
                response = HandleAuth(parts[1]);
            }
            else if (RequireAuthentication)
            {
                // Formato con auth: COMANDO|API_KEY|PARAM1|PARAM2...
                if (parts.Length < 2)
                {
                    response = "ERROR|Authentication required. Provide API Key.";
                    LogAudit("Unknown", "", command, null, false, "Missing API Key");
                }
                else
                {
                    var apiKey = parts[1];
                    var (isValid, keyEntry) = ValidateApiKeyWithInfo(apiKey);

                    if (!isValid)
                    {
                        response = "ERROR|Invalid API Key";
                        LogAudit("Unknown", "", command, null, false, "Invalid API Key");
                        Debug.WriteLine("[EmbeddedSecretServer] Unauthorized access attempt");
                    }
                    else
                    {
                        // Registrar uso de la API Key
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

        switch (command)
        {
            case "GET" when parts.Length >= 3:
                var requestedKey = parts[2];
                var secretToGet = FindAuthorizedSecret(requestedKey, keyEntry);
                if (secretToGet == null)
                {
                    bool existsSomewhere = _secrets.ContainsKey(requestedKey) ||
                                          _secrets.Values.Any(s => s.Key.Equals(requestedKey, StringComparison.OrdinalIgnoreCase));

                    if (existsSomewhere)
                    {
                        response = "ERROR|Access denied to this secret";
                        LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "GET", requestedKey, false, "Access denied - insufficient permissions");
                    }
                    else
                    {
                        response = "NOTFOUND";
                        LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "GET", requestedKey, false, "Secret not found");
                    }
                    break;
                }

                response = $"OK|{secretToGet.Value}|{secretToGet.Description ?? ""}";
                LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "GET", secretToGet.FullKey, true, null);
                break;

            case "EXISTS" when parts.Length >= 3:
                var checkKey = parts[2];
                var secretToCheck = FindAuthorizedSecret(checkKey, keyEntry);
                if (secretToCheck == null)
                {
                    response = "FALSE";
                    bool existsSomewhere = _secrets.ContainsKey(checkKey) ||
                                          _secrets.Values.Any(s => s.Key.Equals(checkKey, StringComparison.OrdinalIgnoreCase));

                    if (existsSomewhere)
                    {
                        LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "EXISTS", checkKey, false, "Access denied - insufficient permissions");
                    }
                    else
                    {
                        LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "EXISTS", checkKey, true, null);
                    }
                    break;
                }

                response = "TRUE";
                LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "EXISTS", secretToCheck.FullKey, true, null);
                break;

            case "LIST":
            case "KEYS":
                if (!CanListSecrets(keyEntry))
                {
                    response = "ERROR|Access denied - cannot list secrets";
                    LogAudit(keyEntry.Name, keyEntry.Id.ToString(), "LIST", null, false, "Access denied - cannot list secrets");
                    break;
                }

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

    private SecretEntry? FindAuthorizedSecret(string key, ApiKeyEntry? keyEntry)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        // Si es una API Key restringida
        if (RequireAuthentication && keyEntry != null && keyEntry.Permissions.Level == AccessLevel.Restricted)
        {
            var permissions = keyEntry.Permissions;

            // A. Buscar coincidencia exacta por FullKey
            if (_secrets.TryGetValue(key, out var directSecret))
            {
                if (HasPermissionToSecret(keyEntry, directSecret))
                {
                    return directSecret;
                }
            }

            // B. Si es una clave relativa (ej: "ConnectionStrings:cadena"), buscar dentro de las carpetas autorizadas de la API Key
            SecretEntry? resolvedCandidate = null;
            int matchCount = 0;

            foreach (var prefix in permissions.AllowedPrefixes)
            {
                var folder = prefix.TrimEnd('*').TrimEnd(':');
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    var scopedKey = $"{folder}:{key}";
                    if (_secrets.TryGetValue(scopedKey, out var scopedSecret))
                    {
                        resolvedCandidate = scopedSecret;
                        matchCount++;
                    }
                }
            }

            if (matchCount == 1)
            {
                return resolvedCandidate;
            }

            // C. Buscar dentro de AllowedSecrets individuales si alguno coincide con el sufijo :key
            foreach (var allowed in permissions.AllowedSecrets)
            {
                if (allowed.EndsWith($":{key}", StringComparison.OrdinalIgnoreCase))
                {
                    if (_secrets.TryGetValue(allowed, out var allowedSecret) && HasPermissionToSecret(keyEntry, allowedSecret))
                    {
                        return allowedSecret;
                    }
                }
            }

            return null;
        }

        // Modo Full Access o sin autenticación
        if (_secrets.TryGetValue(key, out var exactSecret))
        {
            return exactSecret;
        }

        // Si no se encontró por FullKey, buscar por clave relativa única
        var relativeMatches = _secrets.Values
            .Where(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (relativeMatches.Count == 1)
        {
            return relativeMatches[0];
        }

        return null;
    }

    private bool HasPermissionToSecret(ApiKeyEntry? keyEntry, SecretEntry secret)
    {
        if (!RequireAuthentication || keyEntry == null)
            return true;

        var permissions = keyEntry.Permissions;

        if (permissions.Level == AccessLevel.Full)
            return true;

        // 1. ¿Está explícitamente en AllowedSecrets por FullKey o Key?
        if (permissions.AllowedSecrets.Any(s =>
            s.Equals(secret.FullKey, StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(secret.Folder) && s.Equals(secret.Key, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        // 2. Si el secreto está en la raíz (Folder es nulo o vacío):
        // NO pertenece a ninguna carpeta. Solo se permite si estaba explícitamente en AllowedSecrets (evaluado arriba).
        if (string.IsNullOrWhiteSpace(secret.Folder))
            return false;

        // 3. ¿Coincide con alguno de los prefijos de carpeta permitidos?
        foreach (var prefix in permissions.AllowedPrefixes)
        {
            var folderPrefix = prefix.TrimEnd('*').TrimEnd(':');
            if (string.IsNullOrWhiteSpace(folderPrefix))
                continue;

            if (secret.Folder.Equals(folderPrefix, StringComparison.OrdinalIgnoreCase) ||
                secret.Folder.StartsWith($"{folderPrefix}:", StringComparison.OrdinalIgnoreCase) ||
                secret.Folder.StartsWith($"{folderPrefix}/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasPermissionToAccess(ApiKeyEntry keyEntry, string secretKey)
    {
        return FindAuthorizedSecret(secretKey, keyEntry) != null;
    }

    private bool CanListSecrets(ApiKeyEntry keyEntry)
    {
        return keyEntry.Permissions.CanList;
    }

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
            // Filtrar solo los secretos a los que la API Key tiene permiso
            keys = _secrets.Values
                .Where(s => HasPermissionToSecret(keyEntry, s))
                .Select(s => s.FullKey);
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
                var secretToGet = FindAuthorizedSecret(parts[1], null);
                if (secretToGet != null)
                {
                    LogAudit("Anonymous", "N/A", "GET", secretToGet.FullKey, true, null);
                    return $"OK|{secretToGet.Value}|{secretToGet.Description ?? ""}";
                }
                LogAudit("Anonymous", "N/A", "GET", parts[1], false, "Secret not found");
                return "NOTFOUND";

            case "EXISTS" when parts.Length >= 2:
                var exists = FindAuthorizedSecret(parts[1], null) != null;
                LogAudit("Anonymous", "N/A", "EXISTS", parts[1], true, null);
                return exists ? "TRUE" : "FALSE";

            case "LIST":
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
        var secret = FindAuthorizedSecret(key, null);
        if (secret != null)
        {
            return $"OK|{secret.Value}|{secret.Description ?? ""}";
        }

        return "NOTFOUND";
    }

    private string HandleExists(string key)
    {
        return FindAuthorizedSecret(key, null) != null ? "TRUE" : "FALSE";
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
