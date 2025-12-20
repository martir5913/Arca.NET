using Arca.Core.Entities;

namespace Arca.Core.Interfaces;

public interface IVaultRepository
{
    bool VaultExists();
    Task CreateVaultAsync(VaultMetadata metadata, byte[] derivedKey);
    Task<VaultMetadata?> LoadMetadataAsync();
    Task<IReadOnlyList<SecretEntry>> LoadSecretsAsync(byte[] derivedKey);
    Task SaveSecretsAsync(IEnumerable<SecretEntry> secrets, byte[] derivedKey);
    
    /// <summary>
    /// Carga las API Keys del vault.
    /// </summary>
    Task<IReadOnlyList<ApiKeyEntry>> LoadApiKeysAsync(byte[] derivedKey);
    
    /// <summary>
    /// Guarda las API Keys en el vault.
    /// </summary>
    Task SaveApiKeysAsync(IEnumerable<ApiKeyEntry> apiKeys, byte[] derivedKey);
    
    string GetVaultPath();
}
