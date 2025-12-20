namespace Arca.Core.Entities;

public sealed record AuditLogEntry(
    Guid Id,
    DateTime Timestamp,
    string ApiKeyName,       // Nombre de la API Key usada (ej: "WebAPI-Produccion")
    string ApiKeyId,         // ID de la API Key (para correlación)
    string Action,           // Acción: GET, LIST, EXISTS, AUTH, STATUS
    string? SecretKey,       // Clave del secreto solicitado (null para LIST/STATUS)
    bool Success,            // Si la operación fue exitosa
    string? ErrorMessage     // Mensaje de error si falló
);
