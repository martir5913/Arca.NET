namespace Arca.Core.Entities;

/// <summary>
/// Metadatos del vault almacenados en el header del archivo .vlt
/// </summary>
public record VaultMetadata(
    byte[] Salt,
    int Version = 1,
    DateTime CreatedAt = default
);
