# Informe Técnico de Seguridad: Hallazgos en Arca.NET y Plan de Mitigación

**Fecha:** 26 de Octubre de 2026  
**Sistema Evaluado:** Baúl de Secretos Corporativo `Arca.NET` (SDK & Daemon IPC)  
**Proyecto:** Portal de Pagos (`PortalPagos`)  
**Clasificación:** Confidencial / Seguridad de la Información  

---

## 1. Resumen Ejecutivo

Durante el proceso de validación e integración del baúl de secretos **Arca.NET** con el aplicativo **PortalPagos**, se identificó una deficiencia crítica en el modelo de control de acceso y autorización (*Access Control & Scoping*) del servicio daemon de Arca. 

Específicamente, una API Key destinada a operar de manera aislada dentro de una carpeta específica (`Portal_Pagos`) tuvo la capacidad técnica de listar y recuperar secretos huérfanos ubicados en la **Raíz del baúl**, permitiendo la fuga no intencionada de configuraciones críticas (como cadenas de conexión a otras bases de datos) ante un simple error tipográfico en la configuración del cliente.

```
                              ┌───────────────────────────────────┐
                              │     Baúl Arca.NET (31 Secretos)   │
                              └─────────────────┬─────────────────┘
                                                │
                 ┌──────────────────────────────┴──────────────────────────────┐
                 ▼                                                             ▼
     ┌───────────────────────┐                                     ┌───────────────────────┐
     │  Raíz (Sin Carpeta)   │                                     │  Carpeta Portal_Pagos │
     │  10 Secretos Huérfanos│                                     │  10 Secretos Aislados │
     └───────────┬───────────┘                                     └───────────┬───────────┘
                 │                                                             │
                 │ ⚠️ VULNERABILIDAD:                                          │
                 │ La API Key lee la raíz si hay error tipográfico             │ ✔️ Lectura autorizada
                 │                                                             │
                 └──────────────────────────────┬──────────────────────────────┘
                                                ▼
                               ┌─────────────────────────────────┐
                               │  API Key: arca_I0AWaebwZT...    │
                               │  (Recuperó 20 secretos)         │
                               └─────────────────────────────────┘
```

---

## 2. Descripción Técnica del Hallazgo

### 2.1. Falla de Aislamiento en el Ámbito de la API Key (*Scope Bleed*)
En un esquema de bóveda seguro, una credencial o API Key asignada a un espacio de nombres (*Namespace / Folder*) debe regirse bajo el principio de **Denegación por Defecto (*Default Deny*)**: cualquier recurso fuera de su carpeta autorizada debe retornar un error `403 Forbidden` o aparecer inexistente.

En la prueba de campo con el daemon de Arca.NET se constató lo siguiente:
1. El baúl reportó un total de **31 secretos físicos** (`SecretCount: 31`).
2. Al invocar `client.GetFolderSecretsAsync("Portal_Pagos")`, Arca respondió correctamente con los **10 secretos** propios de la carpeta.
3. Al invocar `client.GetFolderSecretsAsync("PortalAutorizaciones")`, el acceso fue denegado (0 secretos).
4. Sin embargo, al invocar el método global `client.ListKeysAsync()`, Arca entregó **20 llaves autorizadas**:
   - 10 llaves correspondientes a `Portal_Pagos:*`.
   - 10 llaves correspondientes a la **Raíz global** (`ConnectionStrings:cadena`, `JwtSettings:SecretKey`, etc.).
5. Todas las 10 llaves de la raíz pudieron ser desencriptadas y leídas con éxito por la misma API Key.

### 2.2. Riesgo de Exposición por Error Tipográfico (*Typo Fallback Risk*)
En la configuración local del cliente existía una discrepancia de nomenclatura:
- Archivo `appsettings.json`: `"Folder": "PortalPagos"` (sin guion bajo).
- Baúl Arca.NET: `"Portal_Pagos"` (con guion bajo).

Al no coincidir el nombre exacto de la carpeta, la consulta por carpeta retornó vacío. El código cliente, diseñado con un mecanismo de tolerancia/fallback, procedió a consultar las llaves disponibles mediante `ListKeysAsync()`. Como resultado:
- El aplicativo cargó en memoria la cadena de conexión de la raíz, la cual apuntaba a `DB_Autorizaciones_Dev` (perteneciente a otro proyecto), en lugar de abortar la ejecución.

---

## 3. Matriz de Riesgo e Impacto

| Factor de Riesgo | Nivel | Impacto Potencial |
| :--- | :---: | :--- |
| **Confusión de Ambientes y Datos** | **Alto** | Transacciones de pagos podrían ejecutarse contra bases de datos de otros aplicativos si se toma la cadena de conexión de la raíz por error. |
| **Ruptura del Principio de Menor Privilegio** | **Alto** | Desarrolladores o aplicaciones con acceso a un proyecto pueden inspeccionar llaves globales o de infraestructura compartida. |
| **Fuga de Secretos Criptográficos** | **Medio** | Exposición de `JwtSettings:SecretKey` o llaves de cifrado que deberían estar segregadas por producto. |

---

## 4. Estrategia de Mitigación y Buenas Prácticas

La mitigación debe implementarse en dos niveles: en el **Cliente consumidor (`PortalPagos`)** y en el **Servidor (`Arca.NET Daemon`)**.

### 4.1. Nivel 1: Blindaje en el Aplicativo Consumidor (`PortalPagos.Web`)

Se debe erradicar cualquier lógica de *fallback* permisivo hacia la raíz o adivinación de llaves sueltas.

#### Reglas de Implementación en `ArcaConfigurationExtensions.cs`:
1. **Modo Estricto de Carpeta (*Folder-Strict Mode*):** Si la configuración especifica una carpeta (ej. `Portal_Pagos`), el cliente **únicamente** debe invocar `GetFolderSecretsAsync(folder)`.
2. **Prohibición de Fallback a Raíz:** Si la carpeta retorna 0 secretos, el sistema **no debe** llamar a `ListKeysAsync()` ni intentar buscar llaves sin prefijo. Debe registrar una excepción fatal o advertencia explícita y detener el inicio si los secretos son mandatorios.
3. **Limpieza de Llaves en Memoria:** Solo registrar en `IConfiguration` los secretos que provengan directamente de la carpeta autorizada.

```csharp
// Implementación recomendada para la carga estricta
public static async Task AddArcaSecretsStrictAsync(this WebApplicationBuilder builder)
{
    var arcaConfig = builder.Configuration.GetSection("Arca");
    string folder = arcaConfig["Folder"] ?? throw new InvalidOperationException("Debe configurar la carpeta de Arca.");
    string apiKey = arcaConfig["ApiKey"] ?? throw new InvalidOperationException("Debe configurar la API Key de Arca.");

    var client = new ArcaSimpleClient(apiKey, TimeSpan.FromSeconds(3));

    // Consulta estricta y exclusiva por carpeta
    var secrets = await client.GetFolderSecretsAsync(folder, stripFolderPrefix: true);

    if (secrets == null || secrets.Count == 0)
    {
        throw new SecurityException($"[Arca Security Alert] No se encontraron secretos en la carpeta autorizada '{folder}'. " +
                                  "Verifique que el nombre de la carpeta coincida exactamente y que la API Key posea permisos.");
    }

    // Inyección controlada en memoria
    builder.Configuration.AddInMemoryCollection(secrets);
}
```

---

### 4.2. Nivel 2: Corrección en el Servicio Daemon (`Arca.NET`)

Si el equipo de desarrollo tiene gobernanza sobre el código fuente de `Arca.NET`, se deben aplicar los siguientes controles en el backend del servicio:

1. **Denegación por Defecto en Raíz (*Default Deny on Root*):**
   - Cuando una API Key tenga asignado un `FolderId` o alcance acotado, las consultas `ListKeysAsync()` y `GetSecretValueAsync()` deben aplicar un filtro SQL/BD estricto:
     ```sql
     WHERE Folder = @ApiKeyAssignedFolder
     ```
   - Si una llave tiene `Folder IS NULL` (secreto en raíz), solo debe ser visible para llaves con rol de **SuperAdmin / Global**.
2. **Validación de Prefijo en Named Pipes:**
   - Si la API Key intenta consultar un secreto mediante su nombre directo (ej. `ConnectionStrings:cadena`), el daemon debe rechazar la solicitud si dicho secreto no pertenece a la carpeta concedida a esa API Key.
3. **Depuración de Secretos en el Baúl:**
   - Mover o eliminar todos los secretos huérfanos que residen actualmente en la raíz hacia sus respectivas carpetas funcionales.

---

## 5. Lista de Acciones Inmediatas (Checklist)

- [x] Identificar los 10 secretos correctos pertenecientes a `Portal_Pagos`.
- [x] Corregir el nombre de carpeta en `appsettings.json` y `appsettings.Development.json` a `"Portal_Pagos"`.
- [x] Implementar aislamiento estricto y denegación por defecto en `EmbeddedSecretServer.cs` (Arca.NET Server).
- [ ] Eliminar en `ArcaConfigurationExtensions.cs` la llamada a `ListKeysAsync()` y la consulta de claves raíz sin prefijo.
- [ ] Depurar en la interfaz de Arca.NET los secretos sueltos en la raíz para evitar que queden accesibles a otras API Keys.
- [ ] Actualizar en Arca.NET el valor de `Portal_Pagos:ConnectionStrings:cadena` con la cadena de conexión definitiva hacia `DB_PortalPagos_Dev`.

---

**Estado de Mitigación Backend:** ✅ **COMPLETADO**  
- `EmbeddedSecretServer.cs`: Indexación única por `FullKey`, eliminación del bypass por sufijo `EndsWith`, denegación por defecto para secretos en raíz y resolución segura de nombres relativos únicamente dentro de las carpetas autorizadas de la API Key.

**Elaborado por:** Antigravity AI Assistant  
**Aprobado para:** Arquitectura de Software y Seguridad de la Información

