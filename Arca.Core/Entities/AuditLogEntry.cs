namespace Arca.Core.Entities;

public sealed record AuditLogEntry(
    Guid Id,
    DateTime Timestamp,
    string ApiKeyName,       // Nombre de la API Key usada
    string ApiKeyId,         // ID de la API Key 
    string Action,           // Acción: GET, LIST, EXISTS, AUTH, STATUS
    string? SecretKey,       // Clave del secreto solicitado 
    bool Success,            // Si la operación fue exitosa
    string? ErrorMessage     // Mensaje de error si falló
);
