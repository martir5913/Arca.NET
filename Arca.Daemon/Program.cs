using Arca.Daemon.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Arca.Daemon;

public class Program
{
    public const string PipeName = "arca-vault";

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure Kestrel to use Named Pipes for gRPC IPC
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenNamedPipe(PipeName, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        // Add gRPC services
        builder.Services.AddGrpc();

        // Register application services
        builder.Services.AddSingleton<VaultStateService>();

        var app = builder.Build();

        // Map gRPC services
        app.MapGrpcService<VaultGrpcService>();

        app.Run();
    }
}
