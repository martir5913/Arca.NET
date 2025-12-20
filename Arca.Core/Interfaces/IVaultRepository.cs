using Arca.Core.Entities;

namespace Arca.Core.Interfaces;

public interface IVaultRepository
{
    bool VaultExists();
    Task CreateVaultAsync(VaultMetadata metadata, byte[] derivedKey);
    Task<VaultMetadata?> LoadMetadataAsync();
    Task<IReadOnlyList<SecretEntry>> LoadSecretsAsync(byte[] derivedKey);
    Task SaveSecretsAsync(IEnumerable<SecretEntry> secrets, byte[] derivedKey);
    string GetVaultPath();
}
