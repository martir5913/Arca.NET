using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Arca.Core.Common;

namespace Arca.SDK.Clients;

/// <summary>
/// Cliente simple basado en Named Pipes (sin gRPC).
/// Útil para aplicaciones ligeras o que no quieren la dependencia de gRPC.
/// </summary>
public sealed class ArcaSimpleClient : IArcaClient
{
    private readonly string _pipeName;
    private readonly int _timeoutMs;

    public ArcaSimpleClient(TimeSpan? timeout = null)
    {
        _pipeName = $"{ArcaConstants.PipeName}-simple";
        _timeoutMs = (int)(timeout ?? TimeSpan.FromMilliseconds(ArcaConstants.DefaultTimeoutMs)).TotalMilliseconds;
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
                    SecretCount = int.TryParse(parts[2], out var count) ? count : 0
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

    public async Task<SecretResult> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var response = await SendCommandAsync($"GET|{key}", cancellationToken);
            var parts = response.Split('|');

            return parts[0] switch
            {
                "OK" when parts.Length >= 2 => SecretResult.Found(parts[1], parts.Length > 2 ? parts[2] : null),
                "NOTFOUND" => SecretResult.NotFound(key),
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
            var command = string.IsNullOrWhiteSpace(filter) ? "LIST" : $"LIST|{filter}";
            var response = await SendCommandAsync(command, cancellationToken);
            var parts = response.Split('|');

            if (parts[0] == "OK" && parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]))
            {
                return parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
            }

            return [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArcaSimpleClient] ListKeysAsync error: {ex.Message}");
            return [];
        }
    }

    public async Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var response = await SendCommandAsync($"EXISTS|{key}", cancellationToken);
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
            return status.IsUnlocked;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArcaSimpleClient] IsAvailableAsync error: {ex.Message}");
            return false;
        }
    }

    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        Debug.WriteLine($"[ArcaSimpleClient] Connecting to pipe: {_pipeName}");
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        await using var pipeClient = new NamedPipeClientStream(
            serverName: ".",
            pipeName: _pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        try
        {
            // Conectar con timeout
            await pipeClient.ConnectAsync(_timeoutMs, cts.Token);
            Debug.WriteLine($"[ArcaSimpleClient] Connected! Sending command: {command}");

            // Enviar comando como bytes
            var commandBytes = Encoding.UTF8.GetBytes(command + "\n");
            await pipeClient.WriteAsync(commandBytes, 0, commandBytes.Length, cts.Token);
            await pipeClient.FlushAsync(cts.Token);

            Debug.WriteLine("[ArcaSimpleClient] Command sent, waiting for response...");

            // Leer respuesta
            var buffer = new byte[4096];
            var bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n');
            Debug.WriteLine($"[ArcaSimpleClient] Response received: {response}");
            
            return response;
        }
        catch (TimeoutException)
        {
            Debug.WriteLine("[ArcaSimpleClient] Connection timeout");
            throw new ArcaException("Connection to Arca timed out. Is the application running?");
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[ArcaSimpleClient] Operation cancelled");
            throw new ArcaException("Operation was cancelled or timed out.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArcaSimpleClient] Error: {ex.GetType().Name} - {ex.Message}");
            throw new ArcaException($"Failed to connect to Arca: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        // No hay recursos que liberar
    }
}
