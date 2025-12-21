namespace Arca.SDK;

public interface IArcaClient : IDisposable
{
    Task<VaultStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<SecretResult> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    Task<string> GetSecretValueAsync(string key, CancellationToken cancellationToken = default);

    Task<Dictionary<string, SecretResult>> GetSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListKeysAsync(string? filter = null, CancellationToken cancellationToken = default);

    Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
