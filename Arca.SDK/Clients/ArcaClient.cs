using System.IO.Pipes;
using Arca.Core.Common;
using Arca.Grpc;
using Grpc.Net.Client;

namespace Arca.SDK.Clients;

/// <summary>
/// Cliente gRPC para comunicarse con Arca Daemon via Named Pipes.
/// Proporciona comunicación ultra-rápida para obtener credenciales.
/// </summary>
public sealed class ArcaClient : IArcaClient
{
    private readonly GrpcChannel _channel;
    private readonly VaultService.VaultServiceClient _client;
    private readonly TimeSpan _timeout;
    private bool _disposed;

    public ArcaClient(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMilliseconds(ArcaConstants.DefaultTimeoutMs);

        // Configurar conexión via Named Pipes para máximo rendimiento
        var connectionFactory = new NamedPipeConnectionFactory(ArcaConstants.PipeName);
        var socketsHandler = new SocketsHttpHandler
        {
            ConnectCallback = connectionFactory.ConnectAsync
        };

        _channel = GrpcChannel.ForAddress(ArcaConstants.PipeUri, new GrpcChannelOptions
        {
            HttpHandler = socketsHandler,
            DisposeHttpClient = true
        });

        _client = new VaultService.VaultServiceClient(_channel);
    }

    public async Task<VaultStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CreateTimeoutCts(cancellationToken);

        try
        {
            var response = await _client.GetStatusAsync(new StatusRequest(), cancellationToken: cts.Token);

            return new VaultStatus
            {
                IsUnlocked = response.IsUnlocked,
                VaultPath = response.VaultPath,
                SecretCount = response.SecretCount
            };
        }
        catch (Exception ex)
        {
            throw new ArcaException("Failed to get vault status. Is Arca Daemon running?", ex);
        }
    }

    public async Task<SecretResult> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var cts = CreateTimeoutCts(cancellationToken);

        try
        {
            var response = await _client.GetSecretAsync(
                new GetSecretRequest { Key = key },
                cancellationToken: cts.Token);

            if (!response.Found)
            {
                return string.IsNullOrEmpty(response.Error)
                    ? SecretResult.NotFound(key)
                    : SecretResult.Failed(response.Error);
            }

            return SecretResult.Found(response.Value, response.Description);
        }
        catch (Exception ex)
        {
            throw new ArcaException($"Failed to get secret '{key}'.", ex);
        }
    }

    public async Task<string> GetSecretValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await GetSecretAsync(key, cancellationToken);

        if (!result.Success)
        {
            throw new ArcaSecretNotFoundException(key, result.Error);
        }

        return result.Value!;
    }

    public async Task<Dictionary<string, SecretResult>> GetSecretsAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var keyList = keys.ToList();
        if (keyList.Count == 0)
            return [];

        using var cts = CreateTimeoutCts(cancellationToken);

        try
        {
            var request = new GetSecretsRequest();
            request.Keys.AddRange(keyList);

            var response = await _client.GetSecretsAsync(request, cancellationToken: cts.Token);

            var results = new Dictionary<string, SecretResult>();

            // Agregar secretos encontrados
            foreach (var secret in response.Secrets)
            {
                results[secret.Key] = SecretResult.Found(secret.Value, secret.Description);
            }

            // Agregar claves no encontradas
            foreach (var notFoundKey in response.NotFoundKeys)
            {
                results[notFoundKey] = SecretResult.NotFound(notFoundKey);
            }

            return results;
        }
        catch (Exception ex)
        {
            throw new ArcaException("Failed to get secrets.", ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        using var cts = CreateTimeoutCts(cancellationToken);

        try
        {
            var response = await _client.ListKeysAsync(
                new ListKeysRequest { Filter = filter ?? "" },
                cancellationToken: cts.Token);

            if (!string.IsNullOrEmpty(response.Error))
            {
                throw new ArcaException(response.Error);
            }

            return response.Keys.ToList();
        }
        catch (ArcaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ArcaException("Failed to list keys.", ex);
        }
    }

    public async Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var cts = CreateTimeoutCts(cancellationToken);

        try
        {
            var response = await _client.KeyExistsAsync(
                new KeyExistsRequest { Key = key },
                cancellationToken: cts.Token);

            return response.Exists;
        }
        catch (Exception ex)
        {
            throw new ArcaException($"Failed to check if key '{key}' exists.", ex);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await GetStatusAsync(cancellationToken);
            return status.IsUnlocked;
        }
        catch
        {
            return false;
        }
    }

    private CancellationTokenSource CreateTimeoutCts(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);
        return cts;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Dispose();
    }
}

/// <summary>
/// Factory para conexiones Named Pipe.
/// </summary>
internal sealed class NamedPipeConnectionFactory
{
    private readonly string _pipeName;

    public NamedPipeConnectionFactory(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        var clientStream = new NamedPipeClientStream(
            serverName: ".",
            pipeName: _pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.WriteThrough | PipeOptions.Asynchronous,
            impersonationLevel: System.Security.Principal.TokenImpersonationLevel.Anonymous);

        try
        {
            await clientStream.ConnectAsync(cancellationToken);
            return clientStream;
        }
        catch
        {
            await clientStream.DisposeAsync();
            throw;
        }
    }
}
