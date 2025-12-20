# Arca SDK

SDK para acceder a credenciales almacenadas en **Arca Vault** de forma segura y ultra-rápida mediante Named Pipes.

## ?? Instalación

### Opción 1: Referencia al Proyecto (Desarrollo Local)

```bash
dotnet add reference ../Arca.SDK/Arca.SDK.csproj
```

### Opción 2: Referencia al DLL

1. **Obtener los DLLs** (después de compilar Arca.SDK):
   ```
   Arca.SDK\bin\Debug\net10.0\
   ??? Arca.SDK.dll
   ??? Arca.Core.dll
   ??? (dependencias de gRPC si usas ArcaClient)
   ```

2. **Agregar referencia en tu `.csproj`**:
   ```xml
   <ItemGroup>
     <Reference Include="Arca.SDK">
       <HintPath>C:\ruta\a\Arca.SDK.dll</HintPath>
     </Reference>
     <Reference Include="Arca.Core">
       <HintPath>C:\ruta\a\Arca.Core.dll</HintPath>
     </Reference>
   </ItemGroup>
   ```

3. **O copiar DLLs a una carpeta `libs` en tu proyecto**:
   ```xml
   <ItemGroup>
     <Reference Include="Arca.SDK">
       <HintPath>libs\Arca.SDK.dll</HintPath>
     </Reference>
     <Reference Include="Arca.Core">
       <HintPath>libs\Arca.Core.dll</HintPath>
     </Reference>
   </ItemGroup>
   ```

### Opción 3: Paquete NuGet (Cuando esté publicado)

```bash
dotnet add package Arca.SDK
```

---

## ?? Uso Rápido

```csharp
using Arca.SDK;
using Arca.SDK.Clients;

// Crear cliente
using var arca = new ArcaSimpleClient();

// Verificar que Arca esté disponible
if (await arca.IsAvailableAsync())
{
    // Obtener un secreto
    var connectionString = await arca.GetSecretValueAsync("ConnectionStrings:SqlServer");
    Console.WriteLine(connectionString);
}
```

---

## ?? Ejemplos por Tipo de Aplicación

### Aplicación de Consola

```csharp
using Arca.SDK;
using Arca.SDK.Clients;

using var arca = new ArcaSimpleClient();

if (!await arca.IsAvailableAsync())
{
    Console.WriteLine("? Arca no está disponible. Abre la app y desbloquea el vault.");
    return;
}

// Obtener secreto directamente
var apiKey = await arca.GetSecretValueAsync("ApiKeys:MiServicio");
Console.WriteLine($"API Key: {apiKey}");

// O con manejo de resultado
var result = await arca.GetSecretAsync("ConnectionStrings:Database");
if (result.Success)
{
    Console.WriteLine($"Connection: {result.Value}");
}
```

### ASP.NET Core Web API

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Registrar cliente Arca con DI
builder.Services.AddArcaSimpleClient();

var app = builder.Build();
app.Run();
```

```csharp
// En un Controller o Service
public class MiServicio
{
    private readonly IArcaClient _arca;
    
    public MiServicio(IArcaClient arca)
    {
        _arca = arca;
    }
    
    public async Task<string> ObtenerConnectionStringAsync()
    {
        return await _arca.GetSecretValueAsync("ConnectionStrings:SqlServer");
    }
}
```

### Entity Framework con Arca

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Obtener connection string de Arca
using var arca = new ArcaSimpleClient();
if (!await arca.IsAvailableAsync())
    throw new Exception("Arca no disponible");

var connectionString = await arca.GetSecretValueAsync("ConnectionStrings:DefaultConnection");

// Configurar DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));
```

### WPF / Windows Forms

```csharp
public partial class MainWindow : Window
{
    private readonly IArcaClient _arca = new ArcaSimpleClient();
    
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!await _arca.IsAvailableAsync())
        {
            MessageBox.Show("Abre Arca y desbloquea el vault primero.");
            return;
        }
        
        var config = await _arca.GetSecretValueAsync("Config:ApiEndpoint");
        // usar config...
    }
}
```

### Worker Service / Background Service

```csharp
public class MiWorker : BackgroundService
{
    private readonly IArcaClient _arca;
    
    public MiWorker(IArcaClient arca)
    {
        _arca = arca;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Esperar a que Arca esté disponible
        while (!await _arca.IsAvailableAsync() && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
        }
        
        var connectionString = await _arca.GetSecretValueAsync("ConnectionStrings:Worker");
        
        // Ejecutar trabajo...
    }
}
```

---

## ?? API Completa

| Método | Descripción |
|--------|-------------|
| `IsAvailableAsync()` | Verifica si Arca está corriendo y desbloqueado |
| `GetStatusAsync()` | Obtiene estado del vault (IsUnlocked, SecretCount) |
| `GetSecretAsync(key)` | Obtiene secreto con resultado (Success, Value, Error) |
| `GetSecretValueAsync(key)` | Obtiene valor directamente (lanza excepción si no existe) |
| `GetSecretsAsync(keys)` | Obtiene múltiples secretos en un diccionario |
| `ListKeysAsync(filter?)` | Lista todas las claves (con filtro opcional) |
| `KeyExistsAsync(key)` | Verifica si una clave existe |

---

## ?? Estructura Recomendada de Claves

```
ConnectionStrings:SqlServer
ConnectionStrings:Redis
ConnectionStrings:MongoDB

ApiKeys:OpenAI
ApiKeys:SendGrid
ApiKeys:Stripe

Credentials:FTP:Host
Credentials:FTP:User
Credentials:FTP:Password

Config:JwtSecret
Config:EncryptionKey
```

---

## ?? Manejo de Errores

```csharp
try
{
    var secret = await arca.GetSecretValueAsync("MiClave");
}
catch (ArcaSecretNotFoundException ex)
{
    // La clave no existe en el vault
    Console.WriteLine($"Clave no encontrada: {ex.Key}");
}
catch (ArcaException ex)
{
    // Error de conexión (Arca no está corriendo o vault bloqueado)
    Console.WriteLine($"Error: {ex.Message}");
}
```

---

## ??? Arquitectura

```
???????????????????????????????????????????????????????????????
?                    SERVIDOR / PC DE DESARROLLO              ?
???????????????????????????????????????????????????????????????
?                                                             ?
?  ???????????????????                                        ?
?  ?    Arca.NET     ?  ? Usuario desbloquea con contraseña   ?
?  ?   (WPF App)     ?                                        ?
?  ???????????????????                                        ?
?           ?                                                 ?
?           ? Named Pipe: arca-vault-simple                   ?
?           ?                                                 ?
?  ????????????????????????????????????????????????????????   ?
?  ?                                                       ?   ?
?  ?  ?????????????  ?????????????  ????????????????????? ?   ?
?  ?  ? Web API   ?  ? Console   ?  ? Windows Service   ? ?   ?
?  ?  ? (IIS)     ?  ?   App     ?  ?    (Worker)       ? ?   ?
?  ?  ?????????????  ?????????????  ????????????????????? ?   ?
?  ?                                                       ?   ?
?  ?  using var arca = new ArcaSimpleClient();            ?   ?
?  ?  var secret = await arca.GetSecretValueAsync("key"); ?   ?
?  ?                                                       ?   ?
?  ?????????????????????????????????????????????????????????   ?
?                                                             ?
???????????????????????????????????????????????????????????????
```

---

## ?? Seguridad

- ? **Cifrado AES-256-GCM** - Secretos cifrados en disco
- ? **Argon2id** - Derivación de clave segura
- ? **Named Pipes** - Comunicación local, no expuesta a red
- ? **Zero-Knowledge** - Solo el usuario conoce la contraseña maestra
- ? **Memoria protegida** - Claves se borran al bloquear/cerrar

---

## ?? Ubicación del Vault

El archivo vault se guarda en:
```
%LOCALAPPDATA%\Arca\vault.vlt
```

Por ejemplo:
```
C:\Users\TuUsuario\AppData\Local\Arca\vault.vlt
```

---

## ??? Requisitos

- **.NET 8.0** o superior
- **Windows** (Named Pipes son específicos de Windows)
- **Arca.NET** debe estar ejecutándose con el vault desbloqueado

---

## ?? Flujo de Trabajo Típico

1. **Iniciar el día**: Abrir Arca.NET y desbloquear con tu contraseña maestra
2. **Desarrollar**: Tus aplicaciones obtienen secretos automáticamente via SDK
3. **Terminar el día**: Cerrar Arca.NET (los secretos se protegen automáticamente)

---

## ?? Links

- [Repositorio GitHub](https://github.com/martir5913/Arca.NET)
- [Reportar Issues](https://github.com/martir5913/Arca.NET/issues)
