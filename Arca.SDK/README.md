# Arca.SDK

SDK para acceder a credenciales almacenadas en **Arca Vault** de forma segura mediante Named Pipes.

## Requisitos

- **.NET 10** o superior
- **Arca.NET** ejecutándose en la misma máquina
- **API Key** generada desde Arca.NET

## Instalación

```bash
dotnet add package Arca.SDK
```

## Uso Rápido

```csharp
using Arca.SDK;
using Arca.SDK.Clients;

var apiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");
using var arca = new ArcaSimpleClient(apiKey: apiKey);

if (await arca.IsAvailableAsync())
{
    var connectionString = await arca.GetSecretValueAsync("ConnectionStrings:Database");
}
```

## API Reference

| Método | Descripción |
|--------|-------------|
| `IsAvailableAsync()` | Verifica conexión y autenticación |
| `GetSecretValueAsync(key)` | Obtiene el valor de un secreto |
| `GetSecretAsync(key)` | Obtiene secreto con metadata |
| `GetSecretsAsync(keys)` | Obtiene múltiples secretos |
| `ListKeysAsync(filter?)` | Lista secretos disponibles |
| `KeyExistsAsync(key)` | Verifica si existe un secreto |

## Manejo de Errores

```csharp
try
{
    var secret = await arca.GetSecretValueAsync("MiClave");
}
catch (ArcaAccessDeniedException ex)
{
    // Sin permiso para este secreto
}
catch (ArcaSecretNotFoundException ex)
{
    // El secreto no existe
}
catch (ArcaException ex)
{
    // Error de conexión u otro
}
```

### Verificación sin excepciones

```csharp
var result = await arca.GetSecretAsync("MiClave");

if (result.IsAccessDenied)
    Console.WriteLine("Acceso denegado");
else if (result.Success)
    Console.WriteLine($"Valor: {result.Value}");
else
    Console.WriteLine($"Error: {result.Error}");
```

## Dependency Injection (ASP.NET Core)

```csharp
// Program.cs
builder.Services.AddArcaClient(
    apiKey: Environment.GetEnvironmentVariable("ARCA_API_KEY")
);

// En servicios
public class MiServicio(IArcaClient arca)
{
    public async Task<string> GetConnectionAsync()
        => await arca.GetSecretValueAsync("ConnectionStrings:Database");
}
```

## Configuración con Opciones

```csharp
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");
    options.Timeout = TimeSpan.FromSeconds(10);
});
```

## Configuración de API Key

```powershell
# Variable de entorno (recomendado)
[Environment]::SetEnvironmentVariable("ARCA_API_KEY", "arca_xxx...", "User")
```

## Características

- **Zero dependencias externas** - Solo usa APIs nativas de .NET
- **Named Pipes** - Comunicación local ultra-rápida (< 1ms de latencia)
- **Autenticación** - API Keys con permisos granulares
- **Thread-safe** - Seguro para uso concurrente
