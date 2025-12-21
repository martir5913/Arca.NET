using Arca.SDK.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Arca.SDK;

public static class DependencyInjection
{
    // Agrega el cliente Arca gRPC al contenedor de servicios
    public static IServiceCollection AddArcaClient(
        this IServiceCollection services,
        string? apiKey = null,
        TimeSpan? timeout = null)
    {
        services.AddSingleton<IArcaClient>(_ => new ArcaClient(timeout));
        return services;
    }

    /// <summary>
    /// Agrega el cliente Arca simple (sin gRPC) al contenedor de servicios.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="apiKey">API Key para autenticación (obtener desde Arca UI)</param>
    /// <param name="timeout">Timeout para operaciones</param>
    public static IServiceCollection AddArcaSimpleClient(
        this IServiceCollection services,
        string? apiKey = null,
        TimeSpan? timeout = null)
    {
        services.AddSingleton<IArcaClient>(_ => new ArcaSimpleClient(apiKey, timeout));
        return services;
    }

    // Agrega el cliente Arca con configuración personalizada.
    public static IServiceCollection AddArcaClient(
        this IServiceCollection services,
        Action<ArcaClientOptions> configure)
    {
        var options = new ArcaClientOptions();
        configure(options);

        services.AddSingleton<IArcaClient>(_ => options.UseSimpleClient
            ? new ArcaSimpleClient(options.ApiKey, options.Timeout)
            : new ArcaClient(options.Timeout));

        return services;
    }
}

// Opciones de configuración para el cliente Arca.
public class ArcaClientOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool UseSimpleClient { get; set; }
    public string? ApiKey { get; set; }
}
