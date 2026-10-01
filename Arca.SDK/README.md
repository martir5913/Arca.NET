# Arca.SDK 🔐

**Arca.SDK** es el cliente oficial y de alto rendimiento para interactuar con **Arca.NET Vault**. Permite recuperar secretos y configuraciones confidenciales en aplicaciones .NET en tiempo real a través de Windows Named Pipes con latencia inferior a **1 milisegundo**.

---

## 🚀 Características del SDK

- ⚡ **Latencia Ultra-Baja (< 1ms):** Comunicación local en memoria vía Named Pipes sin sobrecarga HTTP/TLS.
- 🗂️ **Gestión por Carpetas / Proyectos:** Recupera secretos agrupados (`GetFolderSecretsAsync("MiProyecto")`) o individuales (`GetSecretValueAsync`).
- 🔑 **Autenticación por API Key:** Validación criptográfica segura contra el servidor con soporte para alcances restringidos.
- 🔄 **Auto-Descubrimiento Inteligente:** Detecta automáticamente la instancia activa del servidor en Windows.
- 🏢 **Soporte IIS y Servicios Windows:** Funciona fluidamente entre identidades (`IIS APPPOOL`, `NETWORK SERVICE`) especificando `TargetUser`.
- 🧩 **Inyección de Dependencias Nativa:** Integración limpia en ASP.NET Core y Worker Services mediante `AddArcaClient()`.
- 📦 **Multi-Targeting:** Compatible con **.NET 10+** y **.NET Framework 4.8+**.

---

## 📦 Instalación

### .NET CLI
```bash
dotnet add package Arca.SDK
```

### Visual Studio Package Manager Console
```powershell
Install-Package Arca.SDK
```

### Referencia directa de proyecto
```xml
<ItemGroup>
  <ProjectReference Include="..\Arca.SDK\Arca.SDK.csproj" />
</ItemGroup>
```

---

## 💻 Ejemplos de Integración y Pruebas

### 1. ASP.NET Core & Inyección de Dependencias (.NET 10 / .NET 8 / .NET 6)

```csharp
// Program.cs
using Arca.SDK;

var builder = WebApplication.CreateBuilder(args);

// Registro de Arca en el contenedor de servicios
builder.Services.AddArcaClient(options =>
{
    // API Key generada desde la interfaz de Arca.NET
    options.ApiKey = builder.Configuration["Arca:ApiKey"] 
                     ?? Environment.GetEnvironmentVariable("ARCA_API_KEY");
    
    // Timeout de conexión opcional (por defecto: 5 segundos)
    options.Timeout = TimeSpan.FromSeconds(5);

    // Opcional para IIS con ApplicationPoolIdentity:
    // options.TargetUser = "usuario_windows_servidor";
});

var app = builder.Build();
```

#### Inyección en Servicios / Controladores:

```csharp
using Arca.SDK;
using Arca.SDK.Exceptions;

public class OrderProcessingService
{
    private readonly IArcaClient _arca;
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(IArcaClient arca, ILogger<OrderProcessingService> logger)
    {
        _arca = arca;
        _logger = logger;
    }

    public async Task ProcessOrderAsync()
    {
        try
        {
            // 1. Obtener un secreto específico:
            string paymentApiKey = await _arca.GetSecretValueAsync("Pagos:StripeApiKey");

            // 2. Obtener todos los secretos de una carpeta en una sola llamada:
            Dictionary<string, string> dbConfigs = await _arca.GetFolderSecretsAsync("BasesDeDatos");
            string connectionString = dbConfigs["BasesDeDatos:SqlConnectionString"];

            _logger.LogInformation("Secretos obtenidos exitosamente.");
        }
        catch (ArcaAccessDeniedException)
        {
            _logger.LogError("La API Key no tiene permisos para acceder a estos secretos.");
            throw;
        }
        catch (ArcaSecretNotFoundException ex)
        {
            _logger.LogError("El secreto {SecretKey} no existe en el baúl.", ex.Key);
            throw;
        }
        catch (ArcaException ex)
        {
            _logger.LogError("El baúl está bloqueado o el servidor Arca no está corriendo: {Message}", ex.Message);
            throw;
        }
    }
}
```

---

### 2. Uso Directo / Scripts de Consola / Background Workers

```csharp
using System;
using System.Threading.Tasks;
using Arca.SDK.Clients;
using Arca.SDK.Exceptions;

class Program
{
    static async Task Main()
    {
        // Crear cliente autónomo
        using var client = new ArcaSimpleClient(apiKey: "arca_tu_api_key");

        // 1. Verificar si el servidor Arca está activo
        bool isOnline = await client.IsAvailableAsync();
        if (!isOnline)
        {
            Console.WriteLine("⚠️ Arca no está disponible o la API Key no es válida.");
            return;
        }

        // 2. Obtener estado del baúl
        var status = await client.GetStatusAsync();
        Console.WriteLine($"✅ Baúl activo: {status.SecretCount} secretos cargados.");

        // 3. Listar claves expuestas para esta API Key
        var keys = await client.ListKeysAsync();
        Console.WriteLine($"Claves autorizadas ({keys.Count}):");
        foreach (var key in keys)
        {
            Console.WriteLine($" - {key}");
        }

        // 4. Recuperar valor de un secreto
        string dbSecret = await client.GetSecretValueAsync("PortalWeb:Database:Password");
        Console.WriteLine($"Valor obtenido correctamente: {dbSecret.Length} caracteres.");
    }
}
```

---

### 3. Aplicaciones en IIS / Windows Services (Multi-Identidad)

Cuando tu aplicativo web corre bajo IIS con un Application Pool dedicado (ej. `IIS APPPOOL\MiSitio`) y la aplicación de escritorio Arca.NET está ejecutándose en la sesión de tu usuario:

```csharp
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = "arca_tu_api_key";
    options.TargetUser = "fmartir"; // Usuario de Windows donde está abierto Arca
});
```

---

## 🛡️ Excepciones y Diagnóstico

| Excepción | Causa | Acción Recomendada |
|---|---|---|
| `ArcaAccessDeniedException` | La API Key no tiene asignada la carpeta o el secreto solicitado. | Editar la API Key en Arca.NET y marcar el secreto en el árbol. |
| `ArcaSecretNotFoundException` | La clave solicitada no existe en el baúl. | Verificar el nombre de la clave en el panel de Arca.NET. |
| `ArcaAuthenticationException` | API Key inválida, inactiva o revocada. | Generar una nueva API Key en Arca.NET. |
| `ArcaException` | El baúl está cerrado, bloqueado o el servidor no ha sido iniciado. | Abrir y desbloquear Arca.NET con la contraseña maestra. |

---

## 📄 Licencia

Distribuido bajo la **[Licencia MIT](../LICENSE.txt)**.

Copyright (c) 2026 Ing. Fredy Martir.
