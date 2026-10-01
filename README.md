# Arca.NET

<p align="center">
  <strong>Gestor de secretos seguro, moderno y local para aplicaciones .NET</strong>
</p>

<p align="center">
  <a href="#características">Características</a> •
  <a href="#instalación">Instalación</a> •
  <a href="#organización-por-carpetas--proyectos">Carpetas & Proyectos</a> •
  <a href="#sdk">SDK</a> •
  <a href="#decisiones-técnicas">ADR</a> •
  <a href="#licencia">Licencia</a>
</p>

---

## ¿Qué es Arca.NET?

Gestor de secretos **100% local** para Windows. Almacena credenciales, API keys y connection strings de forma cifrada en tu máquina, accesibles vía SDK con latencia ultrarrápida (< 1ms) para tus aplicaciones .NET.

| Problema | Solución de Arca.NET |
|---|---|
| Credenciales en código fuente / config | Baúl local cifrado con Argon2id + AES-GCM |
| Colisión de claves entre múltiples proyectos | **Organización por Carpetas / Proyectos** |
| Acceso indiscriminado a todos los secretos | **API Keys con alcance por Carpeta** o Secreto |
| Sin trazabilidad de accesos | Registro de auditoría detallado en tiempo real |
| Dependencia de servicios en la nube | 100% local, funciona sin internet (air-gapped) |

---

## Características

- 🛡️ **AES-256-GCM** + **Argon2id** para cifrado de grado militar.
- 🗂️ **Explorador por Carpetas / Proyectos**: Organiza tus secretos de forma limpia sin colisiones de nombres.
- 🔑 **API Keys Granulares**: Asigna permisos a carpetas completas (`PortalClientes:*`) o a claves específicas.
- 🎲 **Generador Integrado**: Generador de contraseñas seguras y llaves criptográficas AES-256 (Base64).
- 📋 **Auditoría Completa**: Monitorea qué aplicación y API Key consumió cada secreto.
- 📦 **Copia de Seguridad (Backup/Restore)**: Exportación e importación cifrada entre servidores.
- ⚡ **Named Pipes Ultrarrápidos (<1ms)** con auto-descubrimiento y soporte para IIS (ApplicationPoolIdentity) y Servicios de Windows.
- 🖥️ **Bandeja del Sistema (System Tray)**: Permanece activo en segundo plano mientras tus aplicaciones lo consumen.

---

## Organización por Carpetas / Proyectos

Con Arca.NET puedes clasificar tus secretos por aplicación o contexto (ej. `PortalClientes`, `BridgeSap`, `Finanzas`):

```
┌─────────────────────────┬────────────────────────────────────────────────────────┐
│  PROYECTOS / CARPETAS   │  🗂️ PortalClientes (3 secretos)        [+ Nuevo] [🔍] │
├─────────────────────────┼────────────────────────────────────────────────────────┤
│  📁 Todos (8)           │  🔑 ConnectionStrings:cadena                           │
│  📂 Sin Carpeta (2)     │     Data Source=sql.prod...             👁️ 📋 ✏️ 🗑️     │
│  ──────────────────     │  ────────────────────────────────────────────────────  │
│  🗂️ PortalClientes (3)  │  🔑 JwtSettings:SecretKey                              │
│  🗂️ BridgeSap (3)       │     ********************                👁️ 📋 ✏️ 🗑️     │
└─────────────────────────┴────────────────────────────────────────────────────────┘
```

---

## Instalación

**Requisitos:** Windows 10+ / Server 2016+ • .NET 10

```powershell
git clone https://github.com/martir5913/Arca.NET.git
cd Arca.NET
dotnet run --project Arca.NET
```

### Ubicación del Baúl

```
%LOCALAPPDATA%\Arca\
+-- vault.vlt      # Secretos cifrados
+-- vault.keys     # API Keys
+-- Logs\          # Auditoría
```

---

## SDK (.NET 10+ y .NET Framework 4.8+)

### Instalación

```bash
dotnet add package Arca.SDK
```

### Consumo Rápido

```csharp
using Arca.SDK;

// Inyección de dependencias en Program.cs
builder.Services.AddArcaClient(options =>
{
    options.ApiKey = builder.Configuration["Arca:ApiKey"];
    options.Timeout = TimeSpan.FromSeconds(5);
});

// En tus servicios:
public class MiServicio(IArcaClient arca)
{
    public async Task IniciarAsync()
    {
        // Secreto individual:
        string sqlConn = await arca.GetSecretValueAsync("ConnectionStrings:cadena");

        // Todos los secretos de una carpeta:
        Dictionary<string, string> sapSecrets = await arca.GetFolderSecretsAsync("BridgeSap");
    }
}
```

**Documentación completa del SDK:** [Arca.SDK/README.md](Arca.SDK/README.md)

---

## Decisiones Técnicas (ADR)

| Decisión | Justificación |
|---|---|
| **AES-256-GCM** | AEAD: cifrado + autenticación de integridad en una sola operación |
| **Argon2id** | Memory-hard, resistente a ataques por GPU/ASIC (OWASP recomendado) |
| **Named Pipes Aislados** | Comunicación inter-proceso local en memoria (<1ms), sin abrir puertos de red |
| **API Keys por Carpeta** | Principio de mínimo privilegio para microservicios y sistemas empresariales |
| **Compatibilidad Total** | Deserialización JSON retrocompatible con baúles de versiones previas |

---

## Licencia

**Source Available License** (Consulte [LICENSE.txt](LICENSE.txt)).  
El **Arca.SDK** se distribuye bajo licencia de código abierto **MIT** (Consulte [LICENSE.SDK](LICENSE.SDK)).

---

<p align="center">
  <b>Autor:</b> Martir_Dev • 
  <b>GitHub:</b> <a href="https://github.com/martir5913/Arca.NET">martir5913/Arca.NET</a> • 
  <b>Email:</b> martir.dev@gmail.com
</p>
