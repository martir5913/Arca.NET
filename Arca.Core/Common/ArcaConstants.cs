namespace Arca.Core.Common;

public static class ArcaConstants
{
    public const string PipeName = "arca-vault";
    public const string LegacyPipeName = "arca-vault-simple";
    public static readonly Uri PipeUri = new($"http://localhost/{PipeName}");
    public const string DaemonProcessName = "Arca.Daemon";
    public const int DefaultTimeoutMs = 5000;

    /// <summary>
    /// Obtiene el nombre del Named Pipe aislado para un usuario específico o el usuario de Windows actual.
    /// </summary>
    public static string GetUserPipeName(string? username = null)
    {
        var rawUser = string.IsNullOrWhiteSpace(username) ? Environment.UserName : username!;
        var user = (rawUser ?? "default").Trim().ToLowerInvariant();

        return $"{PipeName}-{user}-simple";
    }
}

