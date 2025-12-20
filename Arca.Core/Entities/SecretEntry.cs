namespace Arca.Core.Entities;

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
