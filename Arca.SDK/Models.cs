namespace Arca.SDK;

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

public sealed class VaultStatus
{
    public bool IsUnlocked { get; init; }
    public string? VaultPath { get; init; }
    public int SecretCount { get; init; }
}
