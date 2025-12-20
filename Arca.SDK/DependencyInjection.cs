using Arca.SDK.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Arca.SDK;

/// <summary>
/// Extensiones para configurar Arca SDK en aplicaciones .NET.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Agrega el cliente Arca gRPC al contenedor de servicios (recomendado para mejor rendimiento).
    /// </summary>
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

    /// <summary>
    /// Agrega el cliente Arca con configuración personalizada.
    /// </summary>
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

/// <summary>
/// Opciones de configuración para el cliente Arca.
/// </summary>
public class ArcaClientOptions
{
    /// <summary>
    /// Timeout para operaciones (default: 5 segundos).
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Usar cliente simple en lugar de gRPC (default: false).
    /// </summary>
    public bool UseSimpleClient { get; set; }

    /// <summary>
    /// API Key para autenticación. Obtener desde la UI de Arca.
    /// </summary>
    public string? ApiKey { get; set; }
}
