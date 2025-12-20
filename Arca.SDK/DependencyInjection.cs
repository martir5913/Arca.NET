using Arca.SDK.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Arca.SDK
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddArcaClient(
            this IServiceCollection services,
            TimeSpan? timeout = null)
        {
            services.AddSingleton<IArcaClient>(_ => new ArcaClient(timeout));
            return services;
        }

        // agrega el cliente simple
        public static IServiceCollection AddArcaSimpleClient(
            this IServiceCollection services,
            TimeSpan? timeout = null)
        {
            services.AddSingleton<IArcaClient>(_ => new ArcaSimpleClient(timeout));
            return services;
        }

        // agrega el cliente Arca con opciones de configuración
        public static IServiceCollection AddArcaClient(
            this IServiceCollection services,
            Action<ArcaClientOptions> configure)
        {
            var options = new ArcaClientOptions();
            configure(options);

            services.AddSingleton<IArcaClient>(_ => options.UseSimpleClient
                ? new ArcaSimpleClient(options.Timeout)
                : new ArcaClient(options.Timeout));

            return services;
        }
    }

    // opciones de configuración para el cliente Arca
    public class ArcaClientOptions
    {
        // tiempo de espera para las solicitudes (default: 5 segundos).
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        // indica si se debe usar el cliente simple
        public bool UseSimpleClient { get; set; }
    }
}
