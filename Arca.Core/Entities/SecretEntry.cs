namespace Arca.Core.Entities;

public record SecretEntry(
    Guid Id,
    string Key,
    string Value,
    string? Folder = null,
    string? Description = null,
    string? Environment = null,
    List<string>? Tags = null,
    DateTime CreatedAt = default,
    DateTime? ModifiedAt = null
)
{
    public SecretEntry() : this(Guid.NewGuid(), string.Empty, string.Empty) { }

    /// <summary>
    /// Retorna la clave completa incluyendo la carpeta si está definida (ej: "PortalClientes:ConnectionStrings:cadena").
    /// </summary>
    public string FullKey => string.IsNullOrWhiteSpace(Folder) ? Key : $"{Folder}:{Key}";
}
