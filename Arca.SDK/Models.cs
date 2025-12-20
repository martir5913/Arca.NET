namespace Arca.SDK;

/// <summary>
/// Resultado de una operación de obtención de secreto.
/// </summary>
public sealed class SecretResult
{
    public bool Success { get; init; }
    public string? Value { get; init; }
    public string? Description { get; init; }
    public string? Error { get; init; }

    public static SecretResult Found(string value, string? description = null)
        => new() { Success = true, Value = value, Description = description };

    public static SecretResult NotFound(string key)
        => new() { Success = false, Error = $"Secret '{key}' not found." };

    public static SecretResult Failed(string error)
        => new() { Success = false, Error = error };
}

/// <summary>
/// Estado del vault.
/// </summary>
public sealed class VaultStatus
{
    public bool IsUnlocked { get; init; }
    public string? VaultPath { get; init; }
    public int SecretCount { get; init; }
    
    /// <summary>
    /// Indica si el servidor requiere autenticación via API Key.
    /// </summary>
    public bool RequiresAuthentication { get; init; }
}
