# Arca.NET 🔐

<p align="center">
  <strong>Gestor de secretos local, seguro y de alto rendimiento para el ecosistema .NET</strong><br>
  <em>Alternativa On-Premise a Azure Key Vault y AWS Secrets Manager — 100% Air-Gapped y sin suscripciones en la nube.</em>
</p>

<p align="center">
  <a href="#-visión-y-propósito">Visión</a> •
  <a href="#-comparativa">Comparativa</a> •
  <a href="#-características-principales">Características</a> •
  <a href="#-guía-de-inicio-rápido">Inicio Rápido</a> •
  <a href="#-pruebas-de-integración-entre-arcanet-y-el-sdk">Pruebas & Snippets</a> •
  <a href="#-sdk-oficial">SDK</a> •
  <a href="#-licencia">Licencia</a>
</p>

---

## 🎯 Visión y Propósito

**Arca.NET** nace con la misión de proporcionar a desarrolladores, equipos de ingeniería y empresas un **baúl de secretos centralizado de grado militar (Argon2id + AES-256-GCM)** que opera **100% local en memoria** mediante Windows Named Pipes con latencias inferiores a **1 milisegundo**.

Evita exponer credenciales, cadenas de conexión SQL o llaves de API en archivos `appsettings.json`, variables de entorno expuestas o repositorios de código, sin requerir suscripciones mensuales ni dependencias de conexión a internet.

---

## 📊 Comparativa

| Característica | Arca.NET | Azure Key Vault | AWS Secrets Manager | HashiCorp Vault (On-Prem) |
|---|:---:|:---:|:---:|:---:|
| **Costo / Suscripción** | 🟢 **$0 (Gratis y Open Source)** | 🔴 Pago por consumo/clave | 🔴 $0.40/secreto/mes + API calls | 🟡 Complejo de mantener / Enterprise |
| **Latencia de Acceso** | 🟢 **< 1 ms (Named Pipes en RAM)** | 🟡 15 - 80 ms (HTTP/TLS) | 🟡 20 - 90 ms (HTTP/TLS) | 🟡 5 - 20 ms (HTTP Local) |
| **Requiere Internet (Air-Gapped)** | 🟢 **No (100% Offline)** | 🔴 Sí | 🔴 Sí | 🟢 No |
| **Organización por Carpetas / Proyectos** | 🟢 **Nativa en UI y SDK** | 🔴 Plana (Convención de nombres) | 🟡 Por prefijos de path | 🟢 Por Paths |
| **API Keys con Alcance por Proyecto** | 🟢 **Sí (Árbol granular en UI)** | 🟡 RBAC complejo en Azure AD | 🟡 Políticas IAM complejas | 🟢 Políticas HCL |
| **Respaldos Automáticos y Cifrados** | 🟢 **Integrados (Rotativos + .arcavault)** | 🟡 Manual / Backup Vault | 🟡 AWS Backup | 🟡 Snapshot manual |
| **Soporte .NET 10 y .NET Framework 4.8** | 🟢 **Nativo (Multi-Targeting)** | 🟢 Sí (NuGet) | 🟢 Sí (NuGet) | 🟡 Vía API REST / VaultSharp |

---

## 🚀 Características Principales

- 🛡️ **Seguridad Criptográfica:** Cifrado autenticado **AES-256-GCM** y derivación de clave con **Argon2id** (resistente a ataques de fuerza bruta por GPU/ASIC).
- 🗂️ **Organización por Proyectos / Carpetas:** Agrupa secretos por aplicación (`PortalClientes`, `SAP_Integration`, `Finanzas`, etc.) y navega fácilmente en la interfaz.
- 🔑 **Gestión Granular de API Keys con Árbol:** Emite llaves con permisos a carpetas completas (`PortalClientes:*`) o a secretos individuales específicos.
- 🎲 **Generador Integrado de Secretos:** Crea contraseñas de alta entropía y llaves simétricas AES-256 (Base64) con un solo clic.
- 📋 **Registro de Auditoría en Tiempo Real:** Monitoreo y trazabilidad de cada solicitud (`GetSecret`, `GetFolderSecrets`, `ListKeys`) con **exportación a CSV**.
- 🛡️ **Auto-Backups Rotativos:** Instantáneas automáticas en segundo plano (cada 1h, 6h, 12h o 24h) con retención configurable de versiones.
- 📦 **Exportación e Importación Portátil (`.arcavault`):** Copias de seguridad completas protegidas con contraseña para migrar entre servidores o estaciones de trabajo.
- 🌐 **Multilenguaje Dinámico (Español / English):** Cambio instantáneo de idioma en caliente desde el panel de Ajustes.
- ⚡ **Servidor Named Pipes Ultrarrápido:** Comunicación inter-proceso protegida por ACLs de Windows con auto-descubrimiento de instancias y compatibilidad con **IIS** y Servicios de Windows.
- 🖥️ **Bandeja del Sistema (System Tray):** Minimiza la ventana a la bandeja para mantener el servidor disponible en segundo plano sin interrumpir el flujo de trabajo.

---

## 🏁 Guía de Inicio Rápido

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
   - Haz clic en `➕ Nueva Carpeta` (ej. `PortalWeb`).
   - Haz clic en `➕ Nuevo Secreto`, asigna la carpeta `PortalWeb`, la clave `ConnectionStrings:Sql` y el valor confidencial.
3. **Generar API Key para tus Aplicaciones:**
   - Haz clic en `🔑 API Keys`.
   - Asigna un nombre (ej. `PortalWeb-Dev`) y marca la carpeta `PortalWeb` en el árbol.
   - Copia la API Key generada (ej. `arca_a1b2c3d4...`).

---

## 🧪 Pruebas de Integración entre Arca.NET y el SDK

### Caso 1: Consumo en ASP.NET Core / .NET 10 (Inyección de Dependencias)

```csharp
// Program.cs
using Arca.SDK;

var builder = WebApplication.CreateBuilder(args);

// Registrar el cliente oficial de Arca
builder.Services.AddArcaClient(options =>
{
    // Obtener API Key de variable de entorno o configuración local
    options.ApiKey = builder.Configuration["Arca:ApiKey"] 
                     ?? Environment.GetEnvironmentVariable("ARCA_API_KEY");
    options.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

// Endpoint de prueba que consume secretos del baúl en tiempo real
app.MapGet("/api/config-test", async (IArcaClient arca) =>
{
    // 1. Obtener un secreto puntual
    string dbConnection = await arca.GetSecretValueAsync("PortalWeb:ConnectionStrings:Sql");

    // 2. Obtener todos los secretos de la carpeta del proyecto en un solo viaje (<1ms)
    Dictionary<string, string> portalSecrets = await arca.GetFolderSecretsAsync("PortalWeb");

    return Results.Ok(new
    {
        Status = "Conectado a Arca.NET",
        TotalSecretsInFolder = portalSecrets.Count,
        HasDbKey = portalSecrets.ContainsKey("PortalWeb:ConnectionStrings:Sql")
    });
});

app.Run();
```

---

### Caso 2: Consola Interactiva o Script Rápido (.NET 10 o .NET Framework 4.8)

```csharp
using System;
using System.Threading.Tasks;
using Arca.SDK.Clients;
using Arca.SDK.Exceptions;

class Program
{
    static async Task Main(string[] args)
    {
        string apiKey = "arca_tu_api_key_aqui";

        // ArcaSimpleClient auto-descubre el Named Pipe activo en la máquina
        using var client = new ArcaSimpleClient(apiKey: apiKey);

        // 1. Verificar disponibilidad y estado del baúl
        if (!await client.IsAvailableAsync())
        {
            Console.WriteLine("⚠️ Arca.NET no está activo o la API Key es inválida.");
            return;
        }

        var status = await client.GetStatusAsync();
        Console.WriteLine($"✅ Baúl activo con {status.SecretCount} secretos disponibles.");

        // 2. Recuperar secreto individual
        try
        {
            string jwtSecret = await client.GetSecretValueAsync("PortalWeb:JwtSettings:SecretKey");
            Console.WriteLine($"🔑 Secreto recuperado exitosamente.");
        }
        catch (ArcaAccessDeniedException)
        {
            Console.WriteLine("❌ Acceso denegado: La API Key no tiene permisos para este secreto.");
        }
        catch (ArcaSecretNotFoundException ex)
        {
            Console.WriteLine($"❌ Secreto no encontrado: {ex.Key}");
        }

        // 3. Recuperar todos los secretos de una carpeta
        var projectSecrets = await client.GetFolderSecretsAsync("PortalWeb");
        foreach (var (key, value) in projectSecrets)
        {
            Console.WriteLine($"  - [{key}] = {new string('*', value.Length)}");
        }
    }
}
```

---

### Caso 3: Aplicaciones bajo IIS / Servicios de Windows (Multi-Usuario)

Si tu aplicación corre bajo identidades como `IIS APPPOOL\DefaultAppPool` o `NETWORK SERVICE` y el baúl Arca.NET está abierto bajo la sesión de un usuario de Windows (ej. `fmartir`):

```csharp
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = "arca_tu_api_key";
    options.TargetUser = "fmartir"; // Sesión de Windows donde corre Arca Desktop
    options.Timeout = TimeSpan.FromSeconds(5);
});
```

*(Nota: Gracias al motor de auto-descubrimiento en Windows, si solo existe una instancia de Arca en la máquina, el SDK se conectará automáticamente).*

---

## 📦 SDK Oficial

El paquete de cliente ligero **Arca.SDK** está disponible con soporte Multi-Targeting para:
- **.NET 10, .NET 8, .NET 6** (`net10.0`)
- **.NET Framework 4.8 / 4.8.1** (`net48`)

Para más detalles, consulta la [Documentación Completa del SDK](Arca.SDK/README.md).

---

## 📄 Licencia

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
