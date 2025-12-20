namespace Arca.Core.Entities;

/// <summary>
/// Representa una entrada de secreto en el vault.
/// </summary>
public record SecretEntry(
    Guid Id,
    string Key,
    string Value,
    string? Description = null,
    DateTime CreatedAt = default,
    DateTime? ModifiedAt = null
)
{
    public SecretEntry() : this(Guid.NewGuid(), string.Empty, string.Empty) { }
}
