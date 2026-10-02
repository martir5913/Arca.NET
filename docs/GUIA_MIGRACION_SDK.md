# Guia de Migracion e Implementacion: Arca.SDK v1.3.1

Esta guia proporciona las instrucciones paso a paso para actualizar proyectos .NET que utilizaban versiones heredadas de Arca.SDK (v1.0.x) hacia la version oficial **Arca.SDK v1.3.1**, aprovechando el nuevo protocolo de alto rendimiento por Named Pipes (< 1ms) y la gestion granular por proyectos y carpetas.

---

## 1. Por que actualizar a Arca.SDK v1.3.1?

Las versiones v1.0.x utilizaban un protocolo previo que requeria servicios adicionales en segundo plano. La version **v1.3.1** introduce mejoras criticas:

- **Comunicacion directa en memoria:** Named Pipes nativos de Windows (< 1ms) sin dependencias externas.
- **Auto-descubrimiento:** El cliente detecta automaticamente la instancia activa de Arca.NET en el sistema.
- **Organizacion por Carpetas / Proyectos:** Soporte para scopes como `PortalWeb:*` o claves individuales.
- **Recuperacion en lote (`GetFolderSecretsAsync`):** Descarga toda la configuracion de un proyecto en una sola llamada.
- **Compatibilidad total con IIS y Servicios de Windows:** Soporte multi-identidad mediante el parametro `TargetUser`.

---

## 2. Paso 1: Actualizar el Paquete NuGet

### Opcion A: Mediante .NET CLI (Terminal)
Ejecuta en la raiz del proyecto cliente:
```powershell
dotnet add package Arca.SDK --version 1.3.1
```

### Opcion B: Mediante Visual Studio Package Manager
1. Clic derecho en el proyecto > **Administrar paquetes NuGet**.
2. Ve a la pestaña **Actualizaciones**.
3. Selecciona **Arca.SDK** y actualiza a la version **1.3.1**.

### Opcion C: Editando directamente el archivo `.csproj`
Reemplaza la linea del paquete en tu `.csproj`:
```xml
<ItemGroup>
  <PackageReference Include="Arca.SDK" Version="1.3.1" />
</ItemGroup>
```

---

## 3. Paso 2: Configuracion en la Aplicacion de Escritorio Arca.NET

Antes de ejecutar tu aplicacion cliente:

1. **Abrir y desbloquear Arca.NET:** Inicia Arca.NET e ingresa tu contraseña maestra.
2. **Crear o verificar la Carpeta del Proyecto:**
   - Haz clic en `Nueva Carpeta` (ej. `PortalWeb`).
   - Mueve o crea los secretos necesarios dentro de esa carpeta.
3. **Generar la API Key para la Aplicacion:**
   - Haz clic en `API Keys` (arriba a la derecha).
   - Asigna un nombre (ej. `PortalWeb-Produccion`).
   - En el árbol de permisos, marca la carpeta de tu proyecto (o selecciona *Acceso Total*).
   - Haz clic en `Generar API Key` y **copia la clave generada** (`arca_...`).

---

## 4. Paso 3: Implementacion en Codigo C#

### Caso 1: Aplicaciones Web / ASP.NET Core (.NET 10 / .NET 8 / .NET 6)

#### A. Configuracion en `appsettings.json` o Variables de Entorno:
```json
{
  "Arca": {
    "ApiKey": "arca_PEGA_AQUI_TU_API_KEY_REAL"
  }
}
```

#### B. Registro en `Program.cs`:
```csharp
using Arca.SDK;

var builder = WebApplication.CreateBuilder(args);

// Registrar cliente de Arca en el contenedor de dependencias
builder.Services.AddArcaClient(options =>
{
    // Obtener la API Key de configuracion o variable de entorno
    options.ApiKey = builder.Configuration["Arca:ApiKey"] 
                     ?? Environment.GetEnvironmentVariable("ARCA_API_KEY");
    
    options.Timeout = TimeSpan.FromSeconds(5);

    // Opcional: Si el sitio corre en IIS bajo ApplicationPoolIdentity
    // y Arca.NET esta abierto en la sesion de otro usuario de Windows:
    // options.TargetUser = "usuario_windows_servidor";
});

var app = builder.Build();
```

#### C. Inyeccion en Servicios / Controladores:
```csharp
using Arca.SDK;
using Arca.SDK.Exceptions;

public class FacturacionService
{
    private readonly IArcaClient _arca;
    private readonly ILogger<FacturacionService> _logger;

    public FacturacionService(IArcaClient arca, ILogger<FacturacionService> logger)
    {
        _arca = arca;
        _logger = logger;
    }

    public async Task IniciarProcesoAsync()
    {
        try
        {
            // Forma 1: Recuperar un secreto individual puntual
            string sqlConnection = await _arca.GetSecretValueAsync("PortalWeb:ConnectionStrings:Sql");

            // Forma 2: Recuperar todos los secretos de la carpeta del proyecto en una sola llamada (<1ms)
            Dictionary<string, string> config = await _arca.GetFolderSecretsAsync("PortalWeb");
            string jwtKey = config["PortalWeb:JwtSettings:SecretKey"];

            _logger.LogInformation("Secretos obtenidos exitosamente desde Arca.NET.");
        }
        catch (ArcaAccessDeniedException)
        {
            _logger.LogError("La API Key no tiene permisos para acceder a los secretos de PortalWeb.");
            throw;
        }
        catch (ArcaSecretNotFoundException ex)
        {
            _logger.LogError("El secreto '{SecretKey}' no fue encontrado en el baul.", ex.Key);
            throw;
        }
        catch (ArcaException ex)
        {
            _logger.LogError("El baul Arca.NET esta cerrado o inaccesible: {Message}", ex.Message);
            throw;
        }
    }
}
```

---

### Caso 2: Consolas, Workers o Scripts Directos (.NET 10 / .NET Framework 4.8)

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
        string apiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY") 
                        ?? "arca_TU_API_KEY_AQUI";

        using var client = new ArcaSimpleClient(apiKey: apiKey);

        // 1. Validar conexion con el baul
        if (!await client.IsAvailableAsync())
        {
            Console.WriteLine("Arca.NET no esta activo o la API Key no es valida.");
            return;
        }

        var status = await client.GetStatusAsync();
        Console.WriteLine($"Baul conectado exitosamente: {status.SecretCount} secretos disponibles.");

        // 2. Obtener un secreto puntual
        try
        {
            string dbSql = await client.GetSecretValueAsync("PortalWeb:ConnectionStrings:Sql");
            Console.WriteLine($"[Secreto Individual]: {dbSql}");
        }
        catch (ArcaAccessDeniedException)
        {
            Console.WriteLine("Acceso denegado: API Key sin permisos.");
        }
        catch (ArcaSecretNotFoundException ex)
        {
            Console.WriteLine($"Secreto no encontrado: {ex.Key}");
        }

        // 3. Obtener todos los secretos de la carpeta
        try
        {
            Dictionary<string, string> carpeta = await client.GetFolderSecretsAsync("PortalWeb");
            Console.WriteLine($"Se recuperaron {carpeta.Count} secretos de la carpeta:");
            foreach (var (clave, valor) in carpeta)
            {
                Console.WriteLine($" - [{clave}] = {valor}");
            }
        }
        catch (ArcaException ex)
        {
            Console.WriteLine($"Error al recuperar carpeta: {ex.Message}");
        }
    }
}
```

---

## 5. Tabla de Comparacion de Metodos (v1.0.x vs v1.3.1)

| Tarea | Codigo Anterior (v1.0.x) | Codigo Nuevo (v1.3.1) |
|---|---|---|
| **Registro DI** | `services.AddArcaClient()` (requeria gRPC daemon) | `services.AddArcaClient(options => options.ApiKey = "...")` |
| **Obtener secreto** | `client.GetSecretAsync("clave")` | `await client.GetSecretValueAsync("Carpeta:Clave")` |
| **Obtener carpeta** | No soportado | `await client.GetFolderSecretsAsync("Carpeta")` |
| **Listar claves** | No filtraba por permisos | `IReadOnlyList<string> keys = await client.ListKeysAsync()` |
| **Manejo de errores** | Excepciones genericas | Excepciones tipadas: `ArcaAccessDeniedException`, `ArcaSecretNotFoundException`, `ArcaException` |

---

## 6. Lista de Verificacion de Despliegue

1. [ ] El proyecto cliente referencia `Arca.SDK` version `1.3.1`.
2. [ ] La aplicacion `Arca.NET` (v1.3.1) esta iniciada y desbloqueada en la maquina.
3. [ ] La API Key configurada en el cliente fue generada en la version actual de Arca.NET y tiene permisos sobre la carpeta correspondiente.
4. [ ] Si el cliente corre bajo IIS o Servicio de Windows, se especifico `options.TargetUser = "usuario_windows"` si es necesario.
