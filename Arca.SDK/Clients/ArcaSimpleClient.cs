using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Arca.SDK.Clients;

public sealed class ArcaSimpleClient : IArcaClient
{
    private readonly string _pipeName;
    private readonly int _timeoutMs;
    private readonly string? _apiKey;

    public ArcaSimpleClient(string? apiKey = null, TimeSpan? timeout = null)
    {
        _pipeName = $"{ArcaConstants.PipeName}-simple";
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

            // Si requiere autenticación, verificar que tengamos una API Key válida
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
            // Arca no está corriendo, vault bloqueado, timeout, etc. — se considera no disponible.
            Debug.WriteLine($"[ArcaSimpleClient] IsAvailableAsync: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

#if NET48
        using var pipeClient = new NamedPipeClientStream(
#else
        await using var pipeClient = new NamedPipeClientStream(
#endif
            serverName: ".",
            pipeName: _pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        try
        {
#if NET48
            await pipeClient.ConnectAsync(_timeoutMs).ConfigureAwait(false);
#else
            await pipeClient.ConnectAsync(_timeoutMs, cts.Token).ConfigureAwait(false);
#endif

            var commandBytes = Encoding.UTF8.GetBytes(command + "\n");
            await pipeClient.WriteAsync(commandBytes, 0, commandBytes.Length, cts.Token).ConfigureAwait(false);
            await pipeClient.FlushAsync(cts.Token).ConfigureAwait(false);

            var buffer = new byte[4096];
            var bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);

            return Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n');
        }
        catch (TimeoutException)
        {
            throw new ArcaException("Connection to Arca timed out. Is the application running?");
        }
        catch (OperationCanceledException)
        {
            throw new ArcaException("Operation was cancelled or timed out.");
        }
        catch (Exception ex)
        {
            throw new ArcaException($"Failed to connect to Arca: {ex.Message}", ex);
        }
    }

    // string.Contains(string, StringComparison) no existe en .NET Framework 4.8
    private static bool ContainsOrdinalIgnoreCase(string source, string value)
        => source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    public void Dispose()
    {
        // No hay recursos que liberar :v quien lo diria 
    }
}
