# Arca.NET

<p align="center">
  <strong>Gestor de secretos seguro y local para aplicaciones .NET</strong>
</p>

<p align="center">
  <a href="#características">Características</a> •
  <a href="#instalación">Instalación</a> •
  <a href="#sdk">SDK</a> •
  <a href="#decisiones-técnicas">ADR</a> •
  <a href="#licencia">Licencia</a>
</p>

---

## ¿Qué es Arca.NET?

Gestor de secretos **100% local** para Windows. Almacena credenciales, API keys y connection strings de forma cifrada, accesibles via SDK para tus aplicaciones .NET.

| Problema | Solución |
|----------|----------|
| Credenciales en código fuente | Vault cifrado externo |
| Sin control de acceso | API Keys con permisos granulares |
| Sin auditoría | Log de cada acceso |
| Dependencia cloud | Local, <1ms latencia |

---

## Características

- **AES-256-GCM** + **Argon2id** para cifrado
- **API Keys** con permisos por secreto
- **Auditoría** completa de accesos
- **Export/Import** entre servidores
- **Named Pipes** (<1ms latencia)
- **System Tray** (segundo plano)

---

## Casos de Uso

### Servidor con múltiples aplicaciones

```
+------------------------------------------------+
¦              Servidor Windows                  ¦
¦                                                ¦
¦  Arca.NET --> vault.vlt (cifrado)              ¦
¦     ¦                                          ¦
¦     +-- SAP App    (API Key: solo SAP_*)       ¦
¦     +-- Web API    (API Key: solo DB_*)        ¦
¦     +-- Worker     (API Key: solo SMTP_*)      ¦
+------------------------------------------------+
```

### Múltiples servidores

```
Servidor SAP              Servidor Automatizaciones
   vault.vlt    --Export/Import--?    vault.vlt
```

---

## Instalación

**Requisitos:** Windows 10+ / Server 2016+ • .NET 10

```powershell
git clone https://github.com/martir5913/Arca.NET.git
cd Arca.NET
dotnet run --project Arca.NET
```

### Ubicación del Vault

```
%LOCALAPPDATA%\Arca\
+-- vault.vlt      # Secretos cifrados
+-- vault.keys     # API Keys
+-- Logs\          # Auditoría
```

---

## SDK

### Instalación

```bash
dotnet add package Arca.SDK
```

### Uso

```csharp
using Arca.SDK.Clients;

var apiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");
using var arca = new ArcaSimpleClient(apiKey);

if (await arca.IsAvailableAsync())
{
    var connString = await arca.GetSecretValueAsync("ConnectionStrings:DB");
}
```

### Manejo de errores

```csharp
try {
    var secret = await arca.GetSecretValueAsync("MiClave");
}
catch (ArcaAccessDeniedException) { /* Sin permiso */ }
catch (ArcaSecretNotFoundException) { /* No existe */ }
```

?? **Documentación completa:** [Arca.SDK/README.md](Arca.SDK/README.md)

---

## Decisiones Técnicas (ADR)

| Decisión | Justificación |
|----------|---------------|
| **AES-256-GCM** | AEAD: cifrado + autenticación en una operación |
| **Argon2id** | Memory-hard, resistente a GPU/ASIC (OWASP recommended) |
| **Named Pipes** | Solo local, <1ms, sin configuración de red |
| **API Keys granulares** | Mínimo privilegio, revocación sin afectar otras apps |
| **Formato binario** | Validación rápida, versionado, mínimo overhead |
| **100% local** | Sin telemetría, funciona air-gapped |

---

## Estructura

```
Arca.NET/
+-- Arca.Core/           # Entidades, interfaces
+-- Arca.Infrastructure/ # Cifrado, persistencia
+-- Arca.SDK/            # Cliente para apps externas
+-- Arca.NET/            # UI WPF + servidor
+-- Arca.Daemon/         # Windows Service (opcional)
```

---

## Licencia

**Source Available License**

| ? Permitido | ? No permitido |
|--------------|-----------------|
| Uso personal | Venta |
| Uso interno corporativo | Redistribución comercial |
| Modificación propia | Sublicenciar |

**Archivo:** [LICENSE](LICENSE)

---

## Roadmap v2.0

- Expiración de API Keys
- Backup automático
- Tags/Categorías
- gRPC remoto (mTLS)

---

<p align="center">
  <b>Autor:</b> Martir_Dev • 
  <b>GitHub:</b> <a href="https://github.com/martir5913/Arca.NET">martir5913/Arca.NET</a> • 
  <b>Email:</b> martir.dev@gmail.com
</p>
