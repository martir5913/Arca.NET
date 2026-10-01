# Arca.SDK

**Arca.SDK** es el cliente oficial y de alto rendimiento para interactuar con **Arca.NET Vault**. Permite recuperar secretos y configuraciones confidenciales en aplicaciones .NET en tiempo real a través de Windows Named Pipes con latencia inferior a **1 milisegundo**.

---

## Características del SDK

- **Latencia Ultra-Baja (< 1ms):** Comunicación local en memoria vía Named Pipes sin sobrecarga HTTP/TLS.
- **Gestión por Carpetas / Proyectos:** Recupera secretos agrupados (`GetFolderSecretsAsync("MiProyecto")`) o individuales (`GetSecretValueAsync`).
- **Autenticación por API Key:** Validación criptográfica segura contra el servidor con soporte para alcances restringidos.
- **Auto-Descubrimiento Inteligente:** Detecta automáticamente la instancia activa del servidor en Windows.
- **Soporte IIS y Servicios Windows:** Funciona fluidamente entre identidades (`IIS APPPOOL`, `NETWORK SERVICE`) especificando `TargetUser`.
- **Inyección de Dependencias Nativa:** Integración limpia en ASP.NET Core y Worker Services mediante `AddArcaClient()`.
- **Multi-Targeting:** Compatible con **.NET 10+** y **.NET Framework 4.8+**.

---

## Instalación

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

## Formato de Nombres de Secretos

Al solicitar secretos mediante el SDK, puedes utilizar cualquiera de los siguientes formatos:

### 1. Formato Completo con Carpeta (Recomendado)
Estructura: `NombreCarpeta:NombreSecreto`
```csharp
// Ideal para evitar confusiones o colisiones entre proyectos:
string sqlConn = await client.GetSecretValueAsync("PortalWeb:ConnectionStrings:Sql");
string jwtKey  = await client.GetSecretValueAsync("PortalWeb:JwtSettings:SecretKey");
```

### 2. Formato Corto / Relativo
Estructura: `NombreSecreto` (sin el prefijo de la carpeta)
```csharp
// Válido cuando la API Key tiene permisos sobre la carpeta del secreto:
string sqlConn = await client.GetSecretValueAsync("ConnectionStrings:Sql");
string jwtKey  = await client.GetSecretValueAsync("JwtSettings:SecretKey");
```

### 3. Recuperación de Carpeta Completa
Descarga todos los secretos de un proyecto en una sola llamada de memoria:
```csharp
Dictionary<string, string> secretos = await client.GetFolderSecretsAsync("PortalWeb");

string sql = secretos["PortalWeb:ConnectionStrings:Sql"];
```

---

## Ejemplos de Código Completos

### Ejemplo 1: Los 3 Escenarios Principales con Manejo de Errores

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arca.SDK.Clients;
using Arca.SDK.Exceptions;

class Program
{
    static async Task Main(string[] args)
    {
        string apiKey = "arca_TU_API_KEY_AQUI";

        using var client = new ArcaSimpleClient(apiKey: apiKey);

        // 1. Validar conexión y estado
        if (!await client.IsAvailableAsync())
        {
            Console.WriteLine("No se pudo conectar: Arca está cerrado o la API Key no es válida.");
            return;
        }

        var status = await client.GetStatusAsync();
        Console.WriteLine($"Baúl conectado: {status.SecretCount} secretos cargados.");

        // =========================================================================
        // ESCENARIO 1: OBTENER UN SECRETO INDIVIDUAL PUNTUAL
        // =========================================================================
        try
        {
            string dbSql = await client.GetSecretValueAsync("PortalWeb:ConnectionStrings:Sql");
            Console.WriteLine($"[Secreto Individual]: {dbSql}");
        }
        catch (ArcaAccessDeniedException)
        {
            Console.WriteLine("La API Key no tiene permisos para este secreto.");
        }
        catch (ArcaSecretNotFoundException)
        {
            Console.WriteLine("El secreto no existe en el baúl.");
        }

        // =========================================================================
        // ESCENARIO 2: OBTENER TODOS LOS SECRETOS DE UNA CARPETA / PROYECTO
        // =========================================================================
        try
        {
            Dictionary<string, string> carpeta = await client.GetFolderSecretsAsync("PortalWeb");
            Console.WriteLine($"Se recuperaron {carpeta.Count} secretos de la carpeta:");
            foreach (var (clave, valor) in carpeta)
            {
                Console.WriteLine($" - [{clave}] = {valor}");
            }
        }
        catch (ArcaAccessDeniedException)
        {
            Console.WriteLine("La API Key no tiene permisos para acceder a esta carpeta.");
        }

        // =========================================================================
        // ESCENARIO 3: LISTAR TODAS LAS CLAVES DISPONIBLES PARA ESTA API KEY
        // =========================================================================
        try
        {
            IReadOnlyList<string> listaClaves = await client.ListKeysAsync();
            Console.WriteLine($"Claves autorizadas ({listaClaves.Count}):");
            foreach (var clave in listaClaves)
            {
                Console.WriteLine($" • {clave}");
            }
        }
        catch (ArcaAccessDeniedException)
        {
            Console.WriteLine("Esta API Key no tiene habilitado el permiso de listar secretos (CanList).");
        }
    }
}
```

---

### Ejemplo 2: ASP.NET Core & Inyección de Dependencias (.NET 10 / .NET 8 / .NET 6)

```csharp
// Program.cs
using Arca.SDK;

var builder = WebApplication.CreateBuilder(args);

// Registro de Arca en el contenedor de servicios
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = builder.Configuration["Arca:ApiKey"] 
                     ?? Environment.GetEnvironmentVariable("ARCA_API_KEY");
    options.Timeout = TimeSpan.FromSeconds(5);
    
    // Opcional para IIS con ApplicationPoolIdentity:
    // options.TargetUser = "usuario_windows";
});

var app = builder.Build();
```

---

### Ejemplo 3: Aplicaciones en IIS / Windows Services (Multi-Identidad)

Cuando tu aplicativo web corre bajo IIS con un Application Pool dedicado (ej. `IIS APPPOOL\MiSitio`) y la aplicación de escritorio Arca.NET está ejecutándose en la sesión de tu usuario:

```csharp
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = "arca_tu_api_key";
    options.TargetUser = "usuario_windows"; // Usuario de Windows donde está abierto Arca Desktop
});
```

---

## Excepciones y Diagnóstico

| Excepción | Causa | Acción Recomendada |
|---|---|---|
| `ArcaAccessDeniedException` | La API Key no tiene asignada la carpeta o el secreto solicitado. | Editar la API Key en Arca.NET y marcar la carpeta o secreto en el árbol. |
| `ArcaSecretNotFoundException` | La clave solicitada no existe en el baúl. | Verificar el nombre de la clave en el panel de Arca.NET. |
| `ArcaAuthenticationException` | API Key inválida, inactiva o revocada. | Generar una nueva API Key en Arca.NET. |
| `ArcaException` | El baúl está cerrado, bloqueado o el servidor no ha sido iniciado. | Abrir y desbloquear Arca.NET con la contraseña maestra. |

---

## Licencia

Distribuido bajo la **[Licencia MIT](../LICENSE.txt)**.

Copyright (c) 2026 Ing. Fredy Martir.
