# Changelog

Todos los cambios notables de este proyecto se documentan en este archivo.

El formato está basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/),
y este proyecto adhiere a [Semantic Versioning](https://semver.org/lang/es/).

## [1.0.0] - 2025-01-XX

### Agregado

#### Seguridad
- Cifrado AES-256-GCM para todos los secretos
- Derivación de clave con Argon2id (64MB, 3 iteraciones)
- API Keys con hash SHA256 (nunca se almacena la key original)
- Sistema de permisos granulares (Full Access / Restricted)
- Protección anti-fuerza bruta (5 intentos, 5 min lockout)
- Protección contra fuga de información en operación EXISTS
- Advertencia en modo sin autenticación

#### Funcionalidad Core
- Vault cifrado con formato binario personalizado
- Gestión de secretos (crear, editar, eliminar)
- Gestión de API Keys con permisos por secreto
- Auditoría completa de todos los accesos
- Comunicación via Named Pipes (<1ms latencia)

#### Interfaz de Usuario
- Aplicación WPF con tema oscuro moderno
- Sistema de notificaciones personalizado
- Diálogos de confirmación integrados
- Ejecución en System Tray (segundo plano)
- Abrir vault existente desde cualquier ubicación

#### Export/Import
- Exportar vault a archivo `.arcavault` cifrado
- Importar con opción de merge o sobrescribir
- Compatibilidad hacia atrás (v1 PBKDF2 ? v2 Argon2id)

#### SDK
- `ArcaSimpleClient` para integración en aplicaciones .NET
- Interfaz `IArcaClient` para dependency injection
- Manejo de errores con excepciones específicas:
  - `ArcaAccessDeniedException`
  - `ArcaSecretNotFoundException`
  - `ArcaException`
- Documentación completa en README

### Seguridad

- **Cifrado:** AES-256-GCM
- **KDF:** Argon2id (OWASP recommended)
- **Hash API Keys:** SHA256
- **Comunicación:** Named Pipes (solo local)
- **Auditoría:** Logging de todas las operaciones

### Arquitectura

```
Arca.Core           ? Entidades e interfaces
Arca.Infrastructure ? Implementaciones de seguridad y persistencia
Arca.SDK            ? Cliente para aplicaciones externas
Arca.NET            ? UI WPF + servidor embebido
Arca.Daemon         ? Windows Service (opcional)
```

---

## [Unreleased] - v2.0

### Planificado
- Expiración de API Keys
- Backup automático del vault
- Tags/Categorías para organizar secretos
- gRPC como alternativa para acceso remoto (con mTLS)

### En evaluación
- Soporte multiplataforma (Avalonia)
- Sincronización entre vaults
