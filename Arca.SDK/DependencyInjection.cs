using Arca.SDK.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Arca.SDK;

public static class DependencyInjection
{
    public static IServiceCollection AddArcaClient(
        this IServiceCollection services,
        string? apiKey = null,
        TimeSpan? timeout = null,
        string? targetUser = null,
        string? customPipeName = null)
    {
        services.AddSingleton<IArcaClient>(_ => new ArcaSimpleClient(apiKey, timeout, targetUser, customPipeName));
        return services;
    }

    public static IServiceCollection AddArcaClient(
        this IServiceCollection services,
        Action<ArcaClientOptions> configure)
    {
        var options = new ArcaClientOptions();
        configure(options);

        services.AddSingleton<IArcaClient>(_ => new ArcaSimpleClient(
            options.ApiKey,
            options.Timeout,
            options.TargetUser,
            options.CustomPipeName));

        return services;
    }
}

public class ArcaClientOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public string? ApiKey { get; set; }
    public string? TargetUser { get; set; }
    public string? CustomPipeName { get; set; }
}
