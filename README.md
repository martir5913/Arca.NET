# Arca.NET

<p align="center">
  <strong>Gestor de secretos local, seguro y de alto rendimiento para el ecosistema .NET</strong><br>
  <em>Alternativa On-Premise a Azure Key Vault y AWS Secrets Manager — 100% Air-Gapped y sin suscripciones en la nube.</em>
</p>

<p align="center">
  <a href="#vision-y-proposito">Visión</a> •
  <a href="#comparativa">Comparativa</a> •
  <a href="#caracteristicas-principales">Características</a> •
  <a href="#formatos-de-nombres-de-secretos">Formatos de Secretos</a> •
  <a href="#guia-de-inicio-rapido">Inicio Rápido</a> •
  <a href="#pruebas-de-integracion-entre-arcanet-y-el-sdk">Pruebas & Snippets</a> •
  <a href="#sdk-oficial">SDK</a> •
  <a href="#licencia">Licencia</a>
</p>

---

## Visión y Propósito

**Arca.NET** nace con la misión de proporcionar a desarrolladores, equipos de ingeniería y empresas un **baúl de secretos centralizado de grado militar (Argon2id + AES-256-GCM)** que opera **100% local en memoria** mediante Windows Named Pipes con latencias inferiores a **1 milisegundo**.

Evita exponer credenciales, cadenas de conexión SQL o llaves de API en archivos `appsettings.json`, variables de entorno expuestas o repositorios de código, sin requerir suscripciones mensuales ni dependencias de conexión a internet.

---

## Comparativa

| Característica | Arca.NET | Azure Key Vault | AWS Secrets Manager | HashiCorp Vault (On-Prem) |
|---|:---:|:---:|:---:|:---:|
| **Costo / Suscripción** | **$0 (Gratis y Open Source)** | Pago por consumo/clave | $0.40/secreto/mes + API calls | Complejo de mantener / Enterprise |
| **Latencia de Acceso** | **< 1 ms (Named Pipes en RAM)** | 15 - 80 ms (HTTP/TLS) | 20 - 90 ms (HTTP/TLS) | 5 - 20 ms (HTTP Local) |
| **Requiere Internet (Air-Gapped)** | **No (100% Offline)** | Sí | Sí | No |
| **Organización por Carpetas / Proyectos** | **Nativa en UI y SDK** | Plana (Convención de nombres) | Por prefijos de path | Por Paths |
| **API Keys con Alcance por Proyecto** | **Sí (Árbol granular en UI)** | RBAC complejo en Azure AD | Políticas IAM complejas | Políticas HCL |
| **Respaldos Automáticos y Cifrados** | **Integrados (Rotativos + .arcavault)** | Manual / Backup Vault | AWS Backup | Snapshot manual |
| **Soporte .NET 10 y .NET Framework 4.8** | **Nativo (Multi-Targeting)** | Sí (NuGet) | Sí (NuGet) | Vía API REST / VaultSharp |

---

## Características Principales

- **Seguridad Criptográfica:** Cifrado autenticado **AES-256-GCM** y derivación de clave con **Argon2id** (resistente a ataques de fuerza bruta por GPU/ASIC).
- **Organización por Proyectos / Carpetas:** Agrupa secretos por aplicación (`PortalWeb`, `ApiFacturacion`, `Finanzas`, etc.) y navega fácilmente en la interfaz.
- **Gestión Granular de API Keys con Árbol:** Emite llaves con permisos a carpetas completas (`PortalWeb:*`) o a secretos individuales específicos.
- **Generador Integrado de Secretos:** Crea contraseñas de alta entropía y llaves simétricas AES-256 (Base64) con un solo clic.
- **Registro de Auditoría en Tiempo Real:** Monitoreo y trazabilidad de cada solicitud (`GetSecret`, `GetFolderSecrets`, `ListKeys`) con **exportación a CSV**.
- **Auto-Backups Rotativos:** Instantáneas automáticas en segundo plano (cada 1h, 6h, 12h o 24h) con retención configurable de versiones.
- **Exportación e Importación Portátil (`.arcavault`):** Copias de seguridad completas protegidas con contraseña para migrar entre servidores o estaciones de trabajo.
- **Multilenguaje Dinámico (Español / English):** Cambio instantáneo de idioma en caliente desde el panel de Ajustes.
- **Servidor Named Pipes Ultrarrápido:** Comunicación inter-proceso protegida por ACLs de Windows con auto-descubrimiento de instancias y compatibilidad con **IIS** y Servicios de Windows.
- **Bandeja del Sistema (System Tray):** Minimiza la ventana a la bandeja para mantener el servidor disponible en segundo plano sin interrumpir el flujo de trabajo.

---

## Formatos de Nombres de Secretos

Para consumir secretos desde el SDK, puedes utilizar dos formatos según tu necesidad:

### 1. Formato Completo con Carpeta (Recomendado)
Estructura: `NombreCarpeta:NombreSecreto`
```csharp
// Recomendado para evitar colisiones entre proyectos con nombres de claves iguales:
string sqlConn = await client.GetSecretValueAsync("PortalWeb:ConnectionStrings:Sql");
string jwtKey  = await client.GetSecretValueAsync("PortalWeb:JwtSettings:SecretKey");
```

### 2. Formato Corto / Relativo
Estructura: `NombreSecreto` (sin prefijo de carpeta)
```csharp
// Válido cuando la API Key tiene permisos sobre la carpeta correspondiente:
string sqlConn = await client.GetSecretValueAsync("ConnectionStrings:Sql");
string jwtKey  = await client.GetSecretValueAsync("JwtSettings:SecretKey");
```

### 3. Carpeta Completa
Descarga todos los secretos de un proyecto en un `Dictionary<string, string>` en un solo viaje de memoria (< 1ms):
```csharp
Dictionary<string, string> carpeta = await client.GetFolderSecretsAsync("PortalWeb");

string sqlConn = carpeta["PortalWeb:ConnectionStrings:Sql"];
```

---

## Guía de Inicio Rápido

### 1. Compilar y Ejecutar Arca.NET

```powershell
# Clonar repositorio
git clone https://github.com/martir5913/Arca.NET.git
cd Arca.NET

# Compilar solución
dotnet build

# Ejecutar la aplicación de escritorio
dotnet run --project Arca.NET
```

### 2. Configurar el Baúl
1. **Crear / Desbloquear Baúl:** Ingresa una contraseña maestra para inicializar el baúl cifrado.
2. **Crear Carpetas y Secretos:**
   - Haz clic en `Nueva Carpeta` (ej. `PortalWeb`).
   - Haz clic en `Nuevo Secreto`, asigna la carpeta `PortalWeb`, la clave `ConnectionStrings:Sql` y el valor confidencial.
3. **Generar API Key para tus Aplicaciones:**
   - Haz clic en `API Keys`.
   - Asigna un nombre (ej. `PortalWeb-Dev`) y marca la carpeta `PortalWeb` en el árbol.
   - Copia la API Key generada (ej. `arca_a1b2c3d4...`).

---

## Pruebas de Integración entre Arca.NET y el SDK

### Caso 1: Los 3 Escenarios de Consumo con Manejo de Excepciones

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

### Caso 2: Inyección de Dependencias en ASP.NET Core (.NET 10 / .NET 8 / .NET 6)

```csharp
// Program.cs
using Arca.SDK;

var builder = WebApplication.CreateBuilder(args);

// Registrar cliente de Arca en el contenedor de servicios
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = builder.Configuration["Arca:ApiKey"] 
                     ?? Environment.GetEnvironmentVariable("ARCA_API_KEY");
    options.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

// Endpoint de prueba que consume secretos del baúl en tiempo real
app.MapGet("/api/config-test", async (IArcaClient arca) =>
{
    string dbConnection = await arca.GetSecretValueAsync("PortalWeb:ConnectionStrings:Sql");
    Dictionary<string, string> portalSecrets = await arca.GetFolderSecretsAsync("PortalWeb");

    return Results.Ok(new
    {
        Status = "Conectado a Arca.NET",
        TotalSecretsInFolder = portalSecrets.Count
    });
});

app.Run();
```

---

### Caso 3: Aplicaciones bajo IIS / Servicios de Windows (Multi-Usuario)

Si tu aplicación corre bajo identidades como `IIS APPPOOL\DefaultAppPool` o `NETWORK SERVICE` y el baúl Arca.NET está abierto bajo la sesión de un usuario de Windows (ej. `usuario_windows`):

```csharp
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = "arca_tu_api_key";
    options.TargetUser = "usuario_windows"; // Usuario de Windows donde corre Arca Desktop
    options.Timeout = TimeSpan.FromSeconds(5);
});
```

*(Nota: Gracias al motor de auto-descubrimiento en Windows, si solo existe una instancia de Arca en la máquina, el SDK se conectará automáticamente).*

---

## SDK Oficial

El paquete de cliente ligero **Arca.SDK** está disponible en **[NuGet.org](https://www.nuget.org/packages/Arca.SDK)** con soporte Multi-Targeting para:
- **.NET 10, .NET 8, .NET 6** (`net10.0`)
- **.NET Framework 4.8 / 4.8.1** (`net48`)

Para más detalles, consulta la [Documentación Completa del SDK](Arca.SDK/README.md).

---

## Licencia

Este proyecto completo (**Arca.NET Suite** y **Arca.SDK**) es software libre de código abierto distribuido bajo la **[Licencia MIT](LICENSE.txt)**.

```
MIT License
Copyright (c) 2026 Ing. Fredy Martir
```

Puedes usarlo, adaptarlo e implementarlo libremente en tus proyectos personales, de equipo o corporativos.

---

<p align="center">
  <b>Autor:</b> Ing. Fredy Martir • 
  <b>GitHub:</b> <a href="https://github.com/martir5913/Arca.NET">https://github.com/martir5913/Arca.NET</a> • 
  <b>Email:</b> <a href="mailto:martir.dev@gmail.com">martir.dev@gmail.com</a>
</p>
