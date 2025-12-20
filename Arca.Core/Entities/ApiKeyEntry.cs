namespace Arca.Core.Entities;

public sealed record ApiKeyEntry(
    Guid Id,
    string Name,
    string KeyHash,        // Hash SHA256 de la API Key (nunca guardamos la key en texto plano)
    string? Description,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    bool IsActive
);
