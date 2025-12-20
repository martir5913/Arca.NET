namespace Arca.Core.Entities;

/// <summary>
/// Representa una API Key autorizada para acceder a los secretos.
/// </summary>
public sealed record ApiKeyEntry(
    Guid Id,
    string Name,
    string KeyHash,        // Hash SHA256 de la API Key (nunca guardamos la key en texto plano)
    string? Description,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    bool IsActive
);
