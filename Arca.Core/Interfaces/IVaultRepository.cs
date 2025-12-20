using Arca.Core.Entities;

namespace Arca.Core.Interfaces;

/// <summary>
/// Repositorio para el archivo vault (.vlt).
/// </summary>
public interface IVaultRepository
{
    /// <summary>
    /// Verifica si existe un vault en la ubicación por defecto.
    /// </summary>
    bool VaultExists();

    /// <summary>
    /// Crea un nuevo vault vacío con la metadata especificada.
    /// </summary>
    Task CreateVaultAsync(VaultMetadata metadata, byte[] derivedKey);

    /// <summary>
    /// Carga el vault y retorna la metadata (sin descifrar los secretos).
    /// </summary>
    Task<VaultMetadata?> LoadMetadataAsync();

    /// <summary>
    /// Carga todos los secretos del vault (requiere la clave derivada).
    /// </summary>
    Task<IReadOnlyList<SecretEntry>> LoadSecretsAsync(byte[] derivedKey);

    /// <summary>
    /// Guarda todos los secretos en el vault.
    /// </summary>
    Task SaveSecretsAsync(IEnumerable<SecretEntry> secrets, byte[] derivedKey);

    /// <summary>
    /// Obtiene la ruta del archivo vault.
    /// </summary>
    string GetVaultPath();
}
