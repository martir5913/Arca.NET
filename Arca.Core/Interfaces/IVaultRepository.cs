using Arca.Core.Entities;

namespace Arca.Core.Interfaces;

public interface IVaultRepository
{
    bool VaultExists();
    Task CreateVaultAsync(VaultMetadata metadata, byte[] derivedKey);
    Task<VaultMetadata?> LoadMetadataAsync();
    Task<IReadOnlyList<SecretEntry>> LoadSecretsAsync(byte[] derivedKey);
    Task SaveSecretsAsync(IEnumerable<SecretEntry> secrets, byte[] derivedKey);

    // Carga las API Keys del vault.
    Task<IReadOnlyList<ApiKeyEntry>> LoadApiKeysAsync(byte[] derivedKey);

    /// Guarda las API Keys en el vault.
    Task SaveApiKeysAsync(IEnumerable<ApiKeyEntry> apiKeys, byte[] derivedKey);

    string GetVaultPath();
}
