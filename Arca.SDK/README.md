# Arca SDK

SDK para acceder a credenciales almacenadas en **Arca Vault** de forma segura y ultra-rápida mediante Named Pipes.

## 📦 Instalación

### Opción 1: Referencia al Proyecto (Desarrollo Local)

```bash
dotnet add reference ../Arca.SDK/Arca.SDK.csproj
```

### Opción 2: Referencia al DLL

```xml
<ItemGroup>
  <Reference Include="Arca.SDK">
    <HintPath>path/to/Arca.SDK.dll</HintPath>
  </Reference>
  <Reference Include="Arca.Core">
    <HintPath>path/to/Arca.Core.dll</HintPath>
  </Reference>
</ItemGroup>
```

### Opción 3: Paquete NuGet (Cuando esté publicado)

```bash
dotnet add package Arca.SDK
```

---

## 🔐 Autenticación con API Key

Para acceder a los secretos, tu aplicación necesita una **API Key** generada desde Arca.

### Paso 1: Generar API Key en Arca

1. Abre **Arca.NET** y desbloquea el vault
2. Haz clic en **🔑 API Keys**
3. Ingresa un nombre (ej: "Mi Web API")
4. Haz clic en **➕ Generate Key**
5. **¡Copia la API Key!** Solo se muestra una vez

### Paso 2: Usar la API Key en tu aplicación

```csharp
using Arca.SDK;
using Arca.SDK.Clients;

// ✅ CORRECTO: API Key se configura UNA sola vez en el constructor
using var arca = new ArcaSimpleClient(apiKey: "arca_tu_api_key_aqui");

// Verificar conexión y autenticación
if (await arca.IsAvailableAsync())
{
    // ✅ Las llamadas NO requieren pasar el API Key nuevamente
    var secret = await arca.GetSecretValueAsync("ConnectionStrings:Database");
    Console.WriteLine(secret);
}
```

> **💡 Importante:** El API Key se configura **una sola vez** al crear el cliente. 
> Todas las llamadas posteriores (`GetSecretValueAsync`, `ListKeysAsync`, etc.) 
> usan internamente el API Key configurado.

---

## 🚀 Uso Rápido

```csharp
using Arca.SDK;
using Arca.SDK.Clients;

// Leer API Key desde variable de entorno (recomendado)
var apiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");

// Crear cliente - API Key se configura UNA vez aquí
using var arca = new ArcaSimpleClient(apiKey: apiKey);

if (await arca.IsAvailableAsync())
{
    // Obtener secretos - NO se pasa API Key en cada llamada
    var connectionString = await arca.GetSecretValueAsync("ConnectionStrings:SqlServer");
    var apiSecret = await arca.GetSecretValueAsync("ApiKeys:External");
}
```

---

## 📖 Ejemplos por Tipo de Aplicación

### ASP.NET Core Web API

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Leer API Key desde variable de entorno (NO guardar en appsettings.json)
var arcaApiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");

// Registrar cliente Arca - API Key se configura UNA vez aquí
builder.Services.AddArcaSimpleClient(apiKey: arcaApiKey);

var app = builder.Build();
app.Run();
```

```csharp
// En un Service - Se inyecta el cliente ya configurado
public class DatabaseService
{
    private readonly IArcaClient _arca;
    
    public DatabaseService(IArcaClient arca) => _arca = arca;
    
    public async Task<string> GetConnectionStringAsync()
    {
        // ✅ NO se pasa API Key aquí - ya está configurado en el cliente
        return await _arca.GetSecretValueAsync("ConnectionStrings:SqlServer");
    }
}
```

### Aplicación de Consola

```csharp
using Arca.SDK;
using Arca.SDK.Clients;

// Leer API Key desde variable de entorno
var apiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");

// Crear cliente - API Key se configura UNA vez
using var arca = new ArcaSimpleClient(apiKey: apiKey);

if (!await arca.IsAvailableAsync())
{
    Console.WriteLine("❌ Arca no disponible o API Key inválida");
    return;
}

// Obtener secretos - NO se pasa API Key en cada llamada
var dbConnection = await arca.GetSecretValueAsync("ConnectionStrings:Database");
var apiSecret = await arca.GetSecretValueAsync("ApiKeys:External");
```

### Worker Service

```csharp
public class MiWorker : BackgroundService
{
    private readonly IArcaClient _arca;
    
    // El cliente ya viene configurado con API Key via DI
    public MiWorker(IArcaClient arca) => _arca = arca;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Esperar a que Arca esté disponible
        while (!await _arca.IsAvailableAsync() && !stoppingToken.IsCancellationRequested)
        {
            Console.WriteLine("Esperando conexión con Arca...");
            await Task.Delay(5000, stoppingToken);
        }
        
        // ✅ NO se pasa API Key - ya está configurado
        var connectionString = await _arca.GetSecretValueAsync("ConnectionStrings:Worker");
        
        // Ejecutar trabajo...
    }
}
```

### Con Dependency Injection y Configuración

```csharp
// Program.cs
builder.Services.AddArcaClient(options =>
{
    // ✅ API Key se configura UNA vez aquí
    options.ApiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");
    options.UseSimpleClient = true;
    options.Timeout = TimeSpan.FromSeconds(10);
});
```

---

## 🔄 Patrón de Uso Correcto

```
┌─────────────────────────────────────────────────────────────┐
│                 PATRÓN DE USO DEL SDK                       │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  1️⃣ CONFIGURACIÓN (una sola vez):                          │
│                                                             │
│     var apiKey = Environment.GetEnvironmentVariable(...)    │
│     using var arca = new ArcaSimpleClient(apiKey: apiKey);  │
│                                                             │
│     ───────────────────────────────────────────────────     │
│                                                             │
│  2️⃣ USO (múltiples llamadas sin API Key):                  │
│                                                             │
│     var secret1 = await arca.GetSecretValueAsync("Key1");   │
│     var secret2 = await arca.GetSecretValueAsync("Key2");   │
│     var keys = await arca.ListKeysAsync();                  │
│     var exists = await arca.KeyExistsAsync("Key3");         │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 📋 API Completa

| Método | Descripción |
|--------|-------------|
| `IsAvailableAsync()` | Verifica disponibilidad Y autenticación |
| `AuthenticateAsync()` | Verifica si la API Key es válida |
| `GetStatusAsync()` | Obtiene estado del vault |
| `GetSecretAsync(key)` | Obtiene secreto con resultado |
| `GetSecretValueAsync(key)` | Obtiene valor (lanza excepción si no existe) |
| `GetSecretsAsync(keys)` | Obtiene múltiples secretos |
| `ListKeysAsync(filter?)` | Lista claves disponibles |
| `KeyExistsAsync(key)` | Verifica si existe una clave |

---

## 🔑 Gestión de API Keys

### Desde la UI de Arca

| Acción | Descripción |
|--------|-------------|
| **Generar** | Crea una nueva API Key con nombre descriptivo |
| **Revocar** | Elimina una API Key (las apps que la usen perderán acceso) |
| **Ver uso** | Muestra cuándo fue usada por última vez |

### Mejores Prácticas

1. **Una API Key por aplicación** - Facilita revocar acceso si es necesario
2. **No guardar en código** - Usar variables de entorno o secrets manager
3. **Rotar periódicamente** - Generar nuevas keys y revocar las antiguas
4. **Nombres descriptivos** - "WebAPI-Produccion", "Worker-Reportes", etc.

---

## ⚠️ Manejo de Errores

```csharp
try
{
    var secret = await arca.GetSecretValueAsync("MiClave");
}
catch (ArcaSecretNotFoundException ex)
{
    // La clave no existe
    Console.WriteLine($"Clave no encontrada: {ex.Key}");
}
catch (ArcaException ex)
{
    // Error de conexión, autenticación, etc.
    Console.WriteLine($"Error: {ex.Message}");
}
```

---

## 📁 Ubicación del Vault

El vault se almacena en la carpeta de datos locales del usuario:

```
%LOCALAPPDATA%\Arca\
├── vault.vlt    ← Secretos cifrados (AES-256-GCM)
└── vault.keys   ← API Keys cifradas
```

**Ruta completa típica:**
```
C:\Users\TuUsuario\AppData\Local\Arca\vault.vlt
```

### Abrir la carpeta del Vault

**PowerShell:**
```powershell
explorer "$env:LOCALAPPDATA\Arca"
```

**CMD:**
```cmd
explorer %LOCALAPPDATA%\Arca
```

---

## 🔒 Seguridad y Recuperación

### Diseño Zero-Knowledge

Arca utiliza un diseño de **Zero-Knowledge**, lo que significa que:

- ✅ Tu contraseña maestra **NUNCA** se almacena
- ✅ Los secretos están cifrados con **AES-256-GCM**
- ✅ La clave de cifrado se deriva con **Argon2id**
- ✅ Solo tú puedes descifrar tus secretos

### ⚠️ Si pierdes la contraseña maestra

**No hay forma de recuperar la contraseña maestra ni los secretos.**

Esto es **intencional** por seguridad. Si olvidas tu contraseña:

| Opción | Descripción |
|--------|-------------|
| **Intentar recordar** | Prueba variaciones de contraseñas que suelas usar |
| **Empezar de nuevo** | Eliminar el vault y crear uno nuevo |
| **Restaurar backup** | Si tienes una copia de seguridad de tus secretos |

### Eliminar el Vault y empezar de nuevo

```powershell
# ⚠️ ADVERTENCIA: Esto eliminará TODOS tus secretos y API Keys
Remove-Item "$env:LOCALAPPDATA\Arca\vault.vlt" -Force
Remove-Item "$env:LOCALAPPDATA\Arca\vault.keys" -Force
```

### Recomendaciones para NO perder acceso

1. **Guarda tu contraseña maestra en otro administrador de contraseñas**
   - Bitwarden, 1Password, KeePass, LastPass, etc.

2. **Escríbela en papel y guárdala en lugar seguro**
   - Caja fuerte, sobre sellado, etc.

3. **Haz backup de tus secretos**
   - Exporta tus secretos periódicamente a un lugar seguro

---

## 🏗️ Arquitectura de Seguridad

```
┌─────────────────────────────────────────────────────────────┐
│                         SEGURIDAD                           │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────┐                                        │
│  │    Arca.NET     │  ← Contraseña maestra (Argon2id)       │
│  │   (WPF App)     │  ← API Keys (SHA256 hash)              │
│  └────────┬────────┘                                        │
│           │                                                 │
│           │ Named Pipe (local only)                         │
│           │                                                 │
│  ┌────────┴─────────────────────────────────────────────┐   │
│  │                                                       │   │
│  │  App 1: arca_abc123... ✅ Autorizada                 │   │
│  │  App 2: arca_xyz789... ✅ Autorizada                 │   │
│  │  App 3: (sin key)      ❌ Rechazada                  │   │
│  │                                                       │   │
│  └───────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Capas de Seguridad

| Capa | Tecnología | Descripción |
|------|------------|-------------|
| **Cifrado** | AES-256-GCM | Cifrado autenticado de grado militar |
| **Derivación** | Argon2id | Resistente a ataques de GPU/ASIC |
| **API Keys** | SHA256 | Solo se almacena el hash |
| **Comunicación** | Named Pipes | Solo local, no expuesto a red |
| **Autenticación** | Por request | Cada solicitud requiere API Key |

---

## 🔒 Seguridad de API Keys

- ✅ Las API Keys se generan con 256 bits de entropía
- ✅ Solo se almacena el hash SHA256 (no la key original)
- ✅ Las keys empiezan con `arca_` para fácil identificación
- ✅ Se puede revocar acceso instantáneamente
- ✅ Se registra el último uso de cada key
- ✅ Las keys NO expiran automáticamente (debes revocarlas manualmente)

---

## 📝 Variables de Entorno

Recomendamos usar variables de entorno para la API Key:

**Windows (PowerShell - Sesión actual):**
```powershell
$env:ARCA_API_KEY = "arca_tu_api_key_aqui"
```

**Windows (Permanente - Usuario):**
```powershell
[Environment]::SetEnvironmentVariable("ARCA_API_KEY", "arca_xxx...", "User")
```

**Windows (Permanente - Sistema):**
```powershell
# Requiere permisos de administrador
[Environment]::SetEnvironmentVariable("ARCA_API_KEY", "arca_xxx...", "Machine")
```

**En tu código:**
```csharp
var apiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");
using var arca = new ArcaSimpleClient(apiKey: apiKey);
```

---

## 📋 Sistema de Auditoría

Arca incluye un sistema completo de auditoría que registra todos los accesos a secretos.

### Información Registrada

| Campo | Descripción |
|-------|-------------|
| **Timestamp** | Fecha y hora UTC de la solicitud |
| **API Key Name** | Nombre de la API Key usada (ej: "WebAPI-Produccion") |
| **Action** | Tipo de operación: GET, LIST, EXISTS, AUTH, STATUS |
| **Secret Key** | Nombre del secreto solicitado |
| **Success** | Si la operación fue exitosa (✅/❌) |
| **Error** | Mensaje de error si la operación falló |

### Ver Audit Log

1. Abre Arca.NET y desbloquea el vault
2. Haz clic en **📋 Audit** en la barra de herramientas
3. Verás:
   - **Estadísticas**: Total de solicitudes, exitosas, fallidas, clientes únicos
   - **Lista de logs**: Todas las solicitudes recientes

### Ubicación de Logs en Disco

Los logs se guardan en archivos JSON diarios:

```
%LOCALAPPDATA%\Arca\Logs\
├── audit-2025-01-15.json
├── audit-2025-01-16.json
└── audit-2025-01-17.json
```

### Formato del Log

```json
{
  "Id": "guid",
  "Timestamp": "2025-01-17T10:30:00Z",
  "ApiKeyName": "WebAPI-Produccion",
  "ApiKeyId": "guid",
  "Action": "GET",
  "SecretKey": "ConnectionStrings:Database",
  "Success": true,
  "ErrorMessage": null
}
```

### Consultar Logs por PowerShell

```powershell
# Ver logs del día actual
Get-Content "$env:LOCALAPPDATA\Arca\Logs\audit-$(Get-Date -Format 'yyyy-MM-dd').json"

# Filtrar por API Key específica
Get-Content "$env:LOCALAPPDATA\Arca\Logs\audit-*.json" | 
    ConvertFrom-Json | 
    Where-Object { $_.ApiKeyName -eq "WebAPI-Produccion" }

# Ver solo errores
Get-Content "$env:LOCALAPPDATA\Arca\Logs\audit-*.json" | 
    ConvertFrom-Json | 
    Where-Object { $_.Success -eq $false }
```

---

## ❓ FAQ

### ¿Puedo usar la misma API Key en varias aplicaciones?
Sí, pero **no es recomendado**. Es mejor crear una API Key por aplicación para poder revocar acceso de forma individual.

### ¿Las API Keys expiran?
No, las API Keys son válidas indefinidamente hasta que las revoques manualmente.

### ¿Qué pasa si pierdo mi API Key?
Puedes generar una nueva desde Arca UI. La key anterior seguirá funcionando.

### ¿Qué pasa si alguien obtiene mi API Key?
Revócala inmediatamente desde Arca UI y genera una nueva.

### ¿Funciona en red?
No, Arca solo funciona localmente mediante Named Pipes. Las aplicaciones deben ejecutarse en la misma máquina que Arca.

### ¿Puedo tener múltiples vaults?
Actualmente no. Solo hay un vault por usuario en `%LOCALAPPDATA%\Arca\`.

---

## 🔗 Links

- [Repositorio GitHub](https://github.com/martir5913/Arca.NET)
- [Reportar Issues](https://github.com/martir5913/Arca.NET/issues)
