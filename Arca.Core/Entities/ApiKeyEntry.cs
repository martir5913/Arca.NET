namespace Arca.Core.Entities;

/// <summary>
/// Representa una API Key con permisos de acceso a secretos específicos.
/// </summary>
public sealed record ApiKeyEntry(
    Guid Id,
    string Name,
    string KeyHash,           // Hash SHA256 de la API Key (nunca guardamos la key en texto plano)
    string? Description,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    bool IsActive,
    ApiKeyPermissions Permissions  // Permisos de acceso
);

/// <summary>
/// Define los permisos de acceso de una API Key.
/// </summary>
public sealed record ApiKeyPermissions(
    AccessLevel Level,              // Nivel de acceso: Full, Restricted, ReadOnly
    List<string> AllowedSecrets,    // Lista de secretos permitidos (si Level = Restricted)
    List<string> AllowedPrefixes,   // Prefijos permitidos (ej: "ConnectionStrings:*")
    bool CanList                    // Si puede listar secretos disponibles
);

/// <summary>
/// Nivel de acceso de una API Key.
/// </summary>
public enum AccessLevel
{
    /// <summary>
    /// Acceso completo a todos los secretos.
    /// </summary>
    Full = 0,
    
    /// <summary>
    /// Acceso solo a secretos específicos definidos en AllowedSecrets o AllowedPrefixes.
    /// </summary>
    Restricted = 1,
    
    /// <summary>
    /// Solo lectura, no puede listar secretos.
    /// </summary>
    ReadOnly = 2
}
