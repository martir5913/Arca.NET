# Arca.SDK

SDK para acceder a credenciales almacenadas en **Arca Vault** de forma segura mediante Named Pipes.

## Compatibilidad

| Framework | Versión mínima |
|---|---|
| .NET | 10.0 o superior |
| .NET Framework | 4.8 o superior |

## Requisitos

- **Arca.NET** ejecutándose en la misma máquina
- **API Key** generada desde la aplicación Arca.NET

## Instalación

**.NET CLI**
```bash
dotnet add package Arca.SDK
```

**Package Manager Console (Visual Studio)**
```powershell
Install-Package Arca.SDK
```

---

## Uso en .NET (10+)

### Uso directo

```csharp
using Arca.SDK.Clients;

var apiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");
using var arca = new ArcaSimpleClient(apiKey: apiKey);

if (await arca.IsAvailableAsync())
{
    var connectionString = await arca.GetSecretValueAsync("ConnectionStrings:Database");
    // Usar connectionString...
}
```

### Manejo de errores

```csharp
try
{
    var secret = await arca.GetSecretValueAsync("MiClave");
}
catch (ArcaAccessDeniedException)
{
    // La API Key no tiene permiso para este secreto
}
catch (ArcaSecretNotFoundException ex)
{
    // El secreto no existe en el vault
    Console.WriteLine($"Clave no encontrada: {ex.Key}");
}
catch (ArcaException ex)
{
    // Error de conexión, timeout u otro problema con Arca
    Console.WriteLine($"Error Arca: {ex.Message}");
}
```

### Dependency Injection (ASP.NET Core)

```csharp
// Program.cs
builder.Services.AddArcaClient(
    apiKey: Environment.GetEnvironmentVariable("ARCA_API_KEY")
);

// En un servicio
public class MiServicio(IArcaClient arca)
{
    public async Task<string> GetConnectionAsync()
        => await arca.GetSecretValueAsync("ConnectionStrings:Database");
}
```

### Configuración con opciones

```csharp
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");
    options.Timeout = TimeSpan.FromSeconds(10);
});
```

---

## Uso en .NET Framework 4.8

### Patrón recomendado — capa de datos

El patrón correcto es verificar el estado del vault explícitamente con `GetStatusAsync`
(que propaga cualquier excepción) antes de pedir el secreto. Para llamar código async
desde un contexto sincrónico, usá `Task.Run` para evitar deadlocks con el
`SynchronizationContext` de .NET Framework.

```csharp
using Arca.SDK;
using Arca.SDK.Clients;
using System;
using System.Configuration;
using System.Threading.Tasks;

public class CapaDatos
{
    private readonly string apiKey = ConfigurationManager.AppSettings["ArcaApiKey"];

    public async Task<string> GetArcaSecretAsync(string key)
    {
        using (var arca = new ArcaSimpleClient(apiKey: apiKey))
        {
            VaultStatus status;
            try
            {
                status = await arca.GetStatusAsync();
            }
            catch (ArcaException ex)
            {
                throw new InvalidOperationException(
                    "No se pudo conectar a Arca Vault: " + ex.Message, ex);
            }

            if (!status.IsUnlocked)
                throw new InvalidOperationException(
                    "Arca Vault está bloqueado. Abrí Arca.NET y desbloquealo.");

            if (status.RequiresAuthentication && string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException(
                    "Arca requiere autenticación pero no hay API Key en App.config (ArcaApiKey).");

            return await arca.GetSecretValueAsync(key);
        }
    }
}
```

Llamada desde código sincrónico (WinForms, botón de evento, etc.):

```csharp
// Task.Run evita el deadlock con el SynchronizationContext de .NET Framework
string conn = Task.Run(() => capaDatos.GetArcaSecretAsync("ConnectionStrings:Database"))
                  .GetAwaiter().GetResult();
```

> Si toda tu cadena de llamadas puede ser `async`, es preferible `await` directo
> sin `Task.Run`. Solo usá `Task.Run` cuando necesitás llamar async desde un método
> sincrónico que no podés cambiar.

### Captura de errores en el llamador

```csharp
try
{
    string conn = Task.Run(() => capaDatos.GetArcaSecretAsync("ConnectionStrings:Database"))
                      .GetAwaiter().GetResult();
}
catch (InvalidOperationException ex)
{
    // Error de conexión o vault bloqueado — mensaje descriptivo
    MessageBox.Show(
        ex.Message + (ex.InnerException != null ? "\n\nDetalle: " + ex.InnerException.Message : ""),
        "Arca Vault no disponible",
        MessageBoxButtons.OK,
        MessageBoxIcon.Warning);
}
catch (ArcaAccessDeniedException ex)
{
    MessageBox.Show("Acceso denegado al secreto.\n" + ex.Message,
        "Sin Permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
catch (ArcaSecretNotFoundException ex)
{
    MessageBox.Show("El secreto no existe en el vault.\nClave: " + ex.Key,
        "Secreto No Encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
```

### ASP.NET MVC / Web API (OWIN)

```csharp
using Arca.SDK;
using Arca.SDK.Clients;
using System.Configuration;
using System.Threading.Tasks;
using System.Web.Mvc;

public class HomeController : Controller
{
    // Singleton: se crea una sola vez, no en cada request
    private static readonly ArcaSimpleClient _arca = new ArcaSimpleClient(
        apiKey: ConfigurationManager.AppSettings["ArcaApiKey"]);

    public async Task<ActionResult> Index()
    {
        if (!await _arca.IsAvailableAsync())
            return HttpNotFound("Arca Vault no disponible");

        var secret = await _arca.GetSecretValueAsync("ConnectionStrings:Database");
        // Usar secret...

        return View();
    }
}
```

> Para diagnóstico de errores en MVC usá `GetStatusAsync()` en lugar de `IsAvailableAsync()`
> y manejá las excepciones con un filtro de errores global o en el action directamente.

### Con Microsoft.Extensions.DependencyInjection en .NET Framework

```csharp
using Arca.SDK;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddArcaClient(options =>
{
    options.ApiKey = System.Environment.GetEnvironmentVariable("ARCA_API_KEY");
    options.Timeout = System.TimeSpan.FromSeconds(10);
});

var provider = services.BuildServiceProvider();
var arca = provider.GetRequiredService<IArcaClient>();
```

---

## API Reference

| Método | Descripción |
|--------|-------------|
| `IsAvailableAsync()` | Retorna `true` si el vault está desbloqueado y la autenticación es válida. Nunca lanza excepción. |
| `GetStatusAsync()` | Retorna el estado del vault. **Lanza `ArcaException`** si no puede conectar. |
| `GetSecretValueAsync(key)` | Obtiene el valor de un secreto. Lanza excepción si no existe o sin permiso. |
| `GetSecretAsync(key)` | Obtiene un secreto con su metadata y estado sin lanzar excepción. |
| `GetSecretsAsync(keys)` | Obtiene múltiples secretos en una sola llamada. |
| `ListKeysAsync(filter?)` | Lista las claves disponibles (requiere permiso). |
| `KeyExistsAsync(key)` | Verifica si existe un secreto sin lanzar excepción. |

## Excepciones

| Excepción | Lanzada por | Cuándo |
|---|---|---|
| `ArcaException` | `GetStatusAsync`, `GetSecretValueAsync`, `ListKeysAsync` | Error de conexión, timeout u otro fallo de comunicación |
| `ArcaSecretNotFoundException` | `GetSecretValueAsync` | La clave no existe en el vault |
| `ArcaAccessDeniedException` | `GetSecretValueAsync`, `ListKeysAsync` | La API Key no tiene permiso para ese secreto u operación |
| `ArcaVaultLockedException` | — | El vault está bloqueado (disponible para lanzar manualmente) |
| `ArcaDaemonNotRunningException` | — | Arca.NET no está corriendo (disponible para lanzar manualmente) |

> `IsAvailableAsync` y `GetSecretAsync` nunca lanzan excepción — encapsulan los errores
> en el valor de retorno (`bool` o `SecretResult`).

## Configuración de API Key

```powershell
# Establecer variable de entorno de usuario (recomendado)
[Environment]::SetEnvironmentVariable("ARCA_API_KEY", "arca_xxx...", "User")
```

> Nunca guardes la API Key en `App.config`, `Web.config` ni en el código fuente.
> Usá siempre variables de entorno o un gestor de secretos del sistema operativo.

## Características

- **Multi-framework** — Compatible con .NET 10+ y .NET Framework 4.8+
- **Named Pipes** — Comunicación local ultra-rápida (< 1ms de latencia)
- **Autenticación** — API Keys con permisos granulares por secreto
- **Thread-safe** — Seguro para uso concurrente

## License

### Arca.SDK
Arca.SDK is licensed under the MIT License and may be freely used,
modified, and redistributed.

### Arca.NET
The main Arca.NET application is Source-Available.
See LICENSE-ARCA-NET.txt for details.
