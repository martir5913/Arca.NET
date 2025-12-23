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
        try
        {
            var response = await SendCommandAsync("STATUS", cancellationToken);
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArcaSimpleClient] GetStatusAsync error: {ex.Message}");
            return new VaultStatus { IsUnlocked = false };
        }
    }

    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return false;

        try
        {
            var response = await SendCommandAsync($"AUTH|{_apiKey}", cancellationToken);
            return response.StartsWith("OK");
        }
        catch
        {
            return false;
        }
    }

    public async Task<SecretResult> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var command = string.IsNullOrEmpty(_apiKey)
                ? $"GET|{key}"
                : $"GET|{_apiKey}|{key}";

            var response = await SendCommandAsync(command, cancellationToken);
            var parts = response.Split('|');

            return parts[0] switch
            {
                "OK" when parts.Length >= 2 => SecretResult.Found(parts[1], parts.Length > 2 ? parts[2] : null),
                "NOTFOUND" => SecretResult.NotFound(key),
                "ERROR" when parts.Length > 1 && parts[1].Contains("Access denied", StringComparison.OrdinalIgnoreCase)
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
        var result = await GetSecretAsync(key, cancellationToken);

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
            results[key] = await GetSecretAsync(key, cancellationToken);
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

            var response = await SendCommandAsync(command, cancellationToken);
            var parts = response.Split('|');

            if (parts[0] == "OK" && parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]))
            {
                return parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
            }

            if (parts[0] == "OK" && (parts.Length < 2 || string.IsNullOrEmpty(parts[1])))
            {
                // OK pero sin secretos 
                return [];
            }

            if (parts[0] == "ERROR")
            {
                var errorMessage = parts.Length > 1 ? parts[1] : "Unknown error";

                // Detectar error de acceso denegado
                if (errorMessage.Contains("Access denied", StringComparison.OrdinalIgnoreCase) ||
                    errorMessage.Contains("cannot list", StringComparison.OrdinalIgnoreCase))
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
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var command = string.IsNullOrEmpty(_apiKey)
                ? $"EXISTS|{key}"
                : $"EXISTS|{_apiKey}|{key}";

            var response = await SendCommandAsync(command, cancellationToken);
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
            var status = await GetStatusAsync(cancellationToken);

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

                return await AuthenticateAsync(cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArcaSimpleClient] IsAvailableAsync error: {ex.Message}");
            return false;
        }
    }

    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        await using var pipeClient = new NamedPipeClientStream(
            serverName: ".",
            pipeName: _pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        try
        {
            await pipeClient.ConnectAsync(_timeoutMs, cts.Token);

            // Enviar comando
            var commandBytes = Encoding.UTF8.GetBytes(command + "\n");
            await pipeClient.WriteAsync(commandBytes, 0, commandBytes.Length, cts.Token);
            await pipeClient.FlushAsync(cts.Token);

            // Leer respuesta
            var buffer = new byte[4096];
            var bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token);

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

    public void Dispose()
    {
        // No hay recursos que liberar :v
    }
}
