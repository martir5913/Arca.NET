namespace Arca.Core.Entities;

public record VaultMetadata(
    byte[] Salt,
    int Version = 1,
    DateTime CreatedAt = default
);
