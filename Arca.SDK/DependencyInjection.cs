using Arca.SDK.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Arca.SDK;

public static class DependencyInjection
{
    public static IServiceCollection AddArcaClient(
        this IServiceCollection services,
        string? apiKey = null,
        TimeSpan? timeout = null)
    {
        services.AddSingleton<IArcaClient>(_ => new ArcaSimpleClient(apiKey, timeout));
        return services;
    }

    public static IServiceCollection AddArcaClient(
        this IServiceCollection services,
        Action<ArcaClientOptions> configure)
    {
        var options = new ArcaClientOptions();
        configure(options);

        services.AddSingleton<IArcaClient>(_ => new ArcaSimpleClient(options.ApiKey, options.Timeout));

        return services;
    }
}

public class ArcaClientOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public string? ApiKey { get; set; }
}
