# Arca.SDK

SDK oficial para acceder a credenciales almacenadas en **Arca Vault** de forma segura, local y ultra-rápida mediante Named Pipes (< 1ms de latencia).

## Novedades en v1.3.0
- 🗂️ **Gestión por Carpetas / Proyectos**: Organiza tus secretos por proyecto (`PortalClientes`, `BridgeSap`, etc.) y recupéralos agrupados o individuales.
- 🔑 **API Keys con alcance por Proyecto**: Asigna permisos a carpetas completas mediante prefijos (`PortalClientes:*`).
- 🔄 **Auto-descubrimiento y Resiliencia de Pipes**: Descubrimiento inteligente de pipes activos y compatibilidad transparente con IIS y servicios de Windows.
- ⚡ **Método `GetFolderSecretsAsync`**: Descarga y mapea todos los secretos de un proyecto en una sola llamada.

---

## Compatibilidad

| Framework | Versión mínima |
|---|---|
| .NET | 10.0 o superior |
| .NET Framework | 4.8 o superior |

---

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

## Uso en .NET (ASP.NET Core / .NET 10+)

### 1. Inyección de Dependencias (Recomendado)

En tu `Program.cs`:

```csharp
using Arca.SDK;

var builder = WebApplication.CreateBuilder(args);

// Registrar cliente de Arca
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = builder.Configuration["Arca:ApiKey"]; // o Environment.GetEnvironmentVariable("ARCA_API_KEY")
    // Opcional para IIS o servicios bajo otra identidad:
    // options.TargetUser = "tu_usuario_windows";
    options.Timeout = TimeSpan.FromSeconds(5);
});
```

### 2. Consumo en Servicios / Controladores

```csharp
public class FacturacionService
{
    private readonly IArcaClient _arca;

    public FacturacionService(IArcaClient arca)
    {
        _arca = arca;
    }

    public async Task ProcesarAsync()
    {
        // Obtener un secreto específico
        string sqlConn = await _arca.GetSecretValueAsync("ConnectionStrings:cadena");
        string jwtKey = await _arca.GetSecretValueAsync("JwtSettings:SecretKey");

        // O recuperar todos los secretos de un proyecto/carpeta:
        Dictionary<string, string> sapSecrets = await _arca.GetFolderSecretsAsync("BridgeSap");
        string apiKey = sapSecrets["BridgeSap:ApiKey"];
    }
}
```

---

## Uso Directo / Scripts / Consola

```csharp
using Arca.SDK.Clients;

// Si Arca corre en tu usuario, el SDK auto-descubre el pipe activo
using var arca = new ArcaSimpleClient(apiKey: "arca_tu_api_key_aqui");

if (await arca.IsAvailableAsync())
{
    var status = await arca.GetStatusAsync();
    Console.WriteLine($"Baúl abierto con {status.SecretCount} secretos.");

    var secret = await arca.GetSecretValueAsync("PortalClientes:ConnectionStrings:cadena");
    Console.WriteLine($"Conexión: {secret}");
}
```

---

## Manejo de Excepciones

```csharp
try
{
    var secret = await arca.GetSecretValueAsync("MiProyecto:ApiKey");
}
catch (ArcaAccessDeniedException)
{
    // La API Key no tiene permisos para este secreto o carpeta
}
catch (ArcaSecretNotFoundException ex)
{
    // El secreto no existe en el baúl
    Console.WriteLine($"Clave no encontrada: {ex.Key}");
}
catch (ArcaException ex)
{
    // El baúl está bloqueado, cerrado o hubo error de comunicación
    Console.WriteLine($"Error de comunicación con Arca: {ex.Message}");
}
```

---

## Soporte para IIS / Multi-Usuario / Servicios

Si tu aplicación corre en **IIS** (bajo identidades como `IIS APPPOOL\DefaultAppPool`), **IIS Express** o un **Servicio de Windows**:

1. En la aplicación Arca Desktop, el baúl está abierto bajo tu usuario (ej. `fmartir`).
2. En la aplicación cliente (IIS), simplemente especifica `TargetUser`:

```csharp
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = "arca_tu_api_key";
    options.TargetUser = "fmartir"; // Usuario donde corre Arca Desktop
});
```
*(Nota: Gracias al auto-descubrimiento en Windows, si solo hay una instancia abierta de Arca en la máquina, el SDK se conectará automáticamente incluso si no especificas `TargetUser`).*

---

## Licencia

Este SDK está distribuido bajo la licencia [MIT](LICENSE.txt).
