namespace Arca.Core.Entities;

public sealed record ApiKeyEntry(
    Guid Id,
    string Name,
    string KeyHash,           // Hash SHA256 de la API Key
    string? Description,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    bool IsActive,
    ApiKeyPermissions Permissions  // Permisos de acceso
);

public sealed record ApiKeyPermissions(
    AccessLevel Level,              // Nivel de acceso: Full, Restricted, ReadOnly
    List<string> AllowedSecrets,    // Lista de secretos permitidos (si Level = Restricted)
    List<string> AllowedPrefixes,   // Prefijos permitidos
    bool CanList                    // Si puede listar secretos disponibles
);

public enum AccessLevel
{
    Full = 0,
    Restricted = 1,
    ReadOnly = 2
}
