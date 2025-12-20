namespace Arca.Daemon.Services;

/// <summary>
/// gRPC service implementation for vault operations.
/// This is a placeholder until the vault.proto file is defined.
/// </summary>
public sealed class VaultGrpcService
{
    private readonly VaultStateService _vaultState;
    private readonly ILogger<VaultGrpcService> _logger;

    public VaultGrpcService(VaultStateService vaultState, ILogger<VaultGrpcService> logger)
    {
        _vaultState = vaultState;
        _logger = logger;
    }

    // TODO: Implement gRPC methods once vault.proto is defined
    // Expected methods:
    // - Unlock(password) -> derives key using Argon2id, stores in VaultStateService
    // - Lock() -> clears the key from memory
    // - GetSecret(key) -> retrieves and decrypts a secret
    // - SetSecret(key, value) -> encrypts and stores a secret
    // - DeleteSecret(key) -> removes a secret from the vault
    // - ListSecrets() -> returns all secret keys (not values)
}
