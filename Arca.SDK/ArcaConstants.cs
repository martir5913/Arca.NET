namespace Arca.SDK;

internal static class ArcaConstants
{
    public const string PipeName = "arca-vault";
    public static readonly Uri PipeUri = new($"http://localhost/{PipeName}");
    public const string DaemonProcessName = "Arca.Daemon";
    public const int DefaultTimeoutMs = 5000;
}
