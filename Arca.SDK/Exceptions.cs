namespace Arca.SDK;

public class ArcaException : Exception
{
    public ArcaException(string message) : base(message) { }
    public ArcaException(string message, Exception innerException) : base(message, innerException) { }
}

public class ArcaSecretNotFoundException : ArcaException
{
    public string Key { get; }

    public ArcaSecretNotFoundException(string key, string? details = null)
        : base($"Secret '{key}' not found in vault.{(details != null ? $" {details}" : "")}")
    {
        Key = key;
    }
}

public class ArcaVaultLockedException : ArcaException
{
    public ArcaVaultLockedException()
        : base("Vault is locked. Please unlock it using the Arca application.") { }
}

public class ArcaDaemonNotRunningException : ArcaException
{
    public ArcaDaemonNotRunningException()
        : base("Arca Daemon is not running. Please start the Arca application.") { }
}
