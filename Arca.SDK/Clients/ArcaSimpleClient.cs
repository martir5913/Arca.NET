using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Arca.SDK.Clients;

public sealed class ArcaSimpleClient : IArcaClient
{
    private readonly string _pipeName;
    private readonly int _timeoutMs;
    private readonly string? _apiKey;

    public ArcaSimpleClient()
        : this(null, null, null, null)
    {
    }

    public ArcaSimpleClient(
        string? apiKey = null,
        TimeSpan? timeout = null,
        string? targetUser = null,
        string? customPipeName = null)
    {
        if (!string.IsNullOrWhiteSpace(customPipeName))
        {
            _pipeName = customPipeName!;
        }
        else
        {
            _pipeName = ArcaConstants.GetUserPipeName(targetUser);
        }

        _timeoutMs = (int)(timeout ?? TimeSpan.FromMilliseconds(ArcaConstants.DefaultTimeoutMs)).TotalMilliseconds;
        _apiKey = apiKey;
    }

    public async Task<VaultStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync("STATUS", cancellationToken).ConfigureAwait(false);
        var parts = response.Split('|');

        if (parts[0] == "OK" && parts.Length >= 3)
        {
            return new VaultStatus
            {
                IsUnlocked = parts[1] == "UNLOCKED",
                SecretCount = int.TryParse(parts[2], out var count) ? count : 0,
                RequiresAuthentication = parts.Length > 3 && parts[3] == "AUTH_REQUIRED"
            };
        }

        return new VaultStatus { IsUnlocked = false };
    }

    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return false;

        try
        {
            var response = await SendCommandAsync($"AUTH|{_apiKey}", cancellationToken).ConfigureAwait(false);
            return response.StartsWith("OK");
        }
        catch
        {
            return false;
        }
    }

    public async Task<SecretResult> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
#if NET48
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(key));
#else
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
#endif

        try
        {
            var command = string.IsNullOrEmpty(_apiKey)
                ? $"GET|{key}"
                : $"GET|{_apiKey}|{key}";

            var response = await SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
            var parts = response.Split('|');

            return parts[0] switch
            {
                "OK" when parts.Length >= 2 => SecretResult.Found(parts[1], parts.Length > 2 ? parts[2] : null),
                "NOTFOUND" => SecretResult.NotFound(key),
                "ERROR" when parts.Length > 1 && ContainsOrdinalIgnoreCase(parts[1], "Access denied")
                    => SecretResult.AccessDenied(key),
                "ERROR" => SecretResult.Failed(parts.Length > 1 ? parts[1] : "Unknown error"),
                _ => SecretResult.Failed(response)
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArcaSimpleClient] GetSecretAsync error: {ex.Message}");
            return SecretResult.Failed(ex.Message);
        }
    }

    public async Task<string> GetSecretValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await GetSecretAsync(key, cancellationToken).ConfigureAwait(false);

        if (result.IsAccessDenied)
            throw new ArcaAccessDeniedException(key, "PSY");

        if (!result.Success)
            throw new ArcaSecretNotFoundException(key, result.Error);

        return result.Value!;
    }

    public async Task<Dictionary<string, SecretResult>> GetSecretsAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, SecretResult>();

        foreach (var key in keys)
        {
            results[key] = await GetSecretAsync(key, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string command;
            if (string.IsNullOrEmpty(_apiKey))
            {
                command = string.IsNullOrWhiteSpace(filter) ? "LIST" : $"LIST|{filter}";
            }
            else
            {
                command = string.IsNullOrWhiteSpace(filter) ? $"LIST|{_apiKey}" : $"LIST|{_apiKey}|{filter}";
            }

            var response = await SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
            var parts = response.Split('|');

            if (parts[0] == "OK" && parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]))
            {
                return parts[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }

            if (parts[0] == "OK" && (parts.Length < 2 || string.IsNullOrEmpty(parts[1])))
            {
                // OK pero sin secretos
                return [];
            }

            if (parts[0] == "ERROR")
            {
                var errorMessage = parts.Length > 1 ? parts[1] : "Unknown error";

                if (ContainsOrdinalIgnoreCase(errorMessage, "Access denied") ||
                    ContainsOrdinalIgnoreCase(errorMessage, "cannot list"))
                {
                    throw new ArcaAccessDeniedException(
                        "Your API Key does not have permission to list secrets. " +
                        "Contact your administrator to enable 'Can list available secrets' permission.",
                        resource: null,
                        operation: "LIST");
                }

                throw new ArcaException(errorMessage);
            }

            return [];
        }
        catch (ArcaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArcaSimpleClient] ListKeysAsync error: {ex.Message}");
            throw new ArcaException($"Failed to list secrets: {ex.Message}", ex);
        }
    }

    public async Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default)
    {
#if NET48
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(key));
#else
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
#endif

        try
        {
            var command = string.IsNullOrEmpty(_apiKey)
                ? $"EXISTS|{key}"
                : $"EXISTS|{_apiKey}|{key}";

            var response = await SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
            return response == "TRUE";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArcaSimpleClient] KeyExistsAsync error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);

            if (!status.IsUnlocked)
                return false;

            // Si requiere autenticacion, verificar que tengamos una API Key valida
            if (status.RequiresAuthentication)
            {
                if (string.IsNullOrEmpty(_apiKey))
                {
                    Debug.WriteLine("[ArcaSimpleClient] Server requires authentication but no API Key provided");
                    return false;
                }

                return await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (ArcaException ex)
        {
            // Arca no esta corriendo, vault bloqueado, timeout, etc. -> se considera no disponible.
            Debug.WriteLine($"[ArcaSimpleClient] IsAvailableAsync: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private string? _resolvedPipeName;

    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        var (pipeClient, connectedPipe) = await ConnectPipeAsync(cts.Token).ConfigureAwait(false);
#if NET48
        using (pipeClient)
#else
        await using (pipeClient)
#endif
        {
            try
            {
                var commandBytes = Encoding.UTF8.GetBytes(command + "\n");
                await pipeClient.WriteAsync(commandBytes, 0, commandBytes.Length, cts.Token).ConfigureAwait(false);
                await pipeClient.FlushAsync(cts.Token).ConfigureAwait(false);

                var buffer = new byte[4096];
                var bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);

                return Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n');
            }
            catch (Exception ex)
            {
                _resolvedPipeName = null;
                throw new ArcaException($"Failed to communicate with Arca on pipe '{connectedPipe}': {ex.Message}", ex);
            }
        }
    }

    private async Task<(NamedPipeClientStream Stream, string PipeName)> ConnectPipeAsync(CancellationToken cancellationToken)
    {
        // 1. Si ya se resolvió un pipe previamente, intentar conectar a él primero
        if (!string.IsNullOrEmpty(_resolvedPipeName))
        {
            try
            {
                var stream = new NamedPipeClientStream(".", _resolvedPipeName!, PipeDirection.InOut, PipeOptions.Asynchronous);
#if NET48
                await stream.ConnectAsync(Math.Min(_timeoutMs, 1000)).ConfigureAwait(false);
#else
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probeCts.CancelAfter(Math.Min(_timeoutMs, 1000));
                await stream.ConnectAsync(probeCts.Token).ConfigureAwait(false);
#endif
                return (stream, _resolvedPipeName!);
            }
            catch
            {
                _resolvedPipeName = null;
            }
        }

        // 2. Construir lista de candidatos prioritarios
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(_pipeName))
            candidates.Add(_pipeName);

        if (!candidates.Contains(ArcaConstants.LegacyPipeName, StringComparer.OrdinalIgnoreCase))
            candidates.Add(ArcaConstants.LegacyPipeName);

        if (!candidates.Contains(ArcaConstants.PipeName, StringComparer.OrdinalIgnoreCase))
            candidates.Add(ArcaConstants.PipeName);

        // Auto-descubrimiento en Windows si ningún candidato básico responde
        if (IsWindowsPlatform())
        {
            try
            {
                var discovered = Directory.GetFiles(@"\\.\pipe\", "arca-vault*")
                    .Select(Path.GetFileName)
                    .Where(p => !string.IsNullOrWhiteSpace(p));

                foreach (var pipe in discovered)
                {
                    if (!candidates.Contains(pipe!, StringComparer.OrdinalIgnoreCase))
                    {
                        candidates.Add(pipe!);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArcaSimpleClient] Pipe discovery warning: {ex.Message}");
            }
        }

        var probeTimeoutMs = candidates.Count > 1 ? Math.Max(350, _timeoutMs / candidates.Count) : _timeoutMs;

        foreach (var candidate in candidates)
        {
            try
            {
                var stream = new NamedPipeClientStream(".", candidate, PipeDirection.InOut, PipeOptions.Asynchronous);
#if NET48
                await stream.ConnectAsync(probeTimeoutMs).ConfigureAwait(false);
#else
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probeCts.CancelAfter(probeTimeoutMs);
                await stream.ConnectAsync(probeCts.Token).ConfigureAwait(false);
#endif
                _resolvedPipeName = candidate;
                Debug.WriteLine($"[ArcaSimpleClient] Connected successfully to Arca pipe: {candidate}");
                return (stream, candidate);
            }
            catch
            {
                // Intentar con el siguiente candidato
            }
        }

        throw new ArcaException($"Connection to Arca timed out. Could not connect to any active pipe ({string.Join(", ", candidates)}). Is the Arca desktop application running and unlocked?");
    }

    private static bool IsWindowsPlatform()
    {
#if NET48
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
        return OperatingSystem.IsWindows();
#endif
    }

    // string.Contains(string, StringComparison) no existe en .NET Framework 4.8
    private static bool ContainsOrdinalIgnoreCase(string source, string value)
        => source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    public void Dispose()
    {
        // No hay recursos que liberar
    }
}
