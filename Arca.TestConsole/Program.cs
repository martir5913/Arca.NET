using Arca.SDK;
using Arca.SDK.Clients;

Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Arca SDK Test Console                          ║");
Console.WriteLine("║           Con Autenticación por API Key                  ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.WriteLine();


var apiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");

if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("⚠️  No se encontró la variable de entorno ARCA_API_KEY");
    Console.WriteLine();
    Console.WriteLine("Opciones:");
    Console.WriteLine("  1. Configurar variable de entorno:");
    Console.WriteLine("     $env:ARCA_API_KEY = \"arca_tu_api_key_aqui\"");
    Console.WriteLine();
    Console.WriteLine("  2. Ingresar API Key manualmente ahora");
    Console.WriteLine();
    Console.Write("Ingresa tu API Key (o Enter para probar sin autenticación): ");
    apiKey = Console.ReadLine();
    Console.WriteLine();
}

// ============================================
// PASO 2: Crear cliente con API Key
// ============================================
using var arca = new ArcaSimpleClient(apiKey: apiKey);

Console.WriteLine("🔌 Conectando a Arca...");
Console.WriteLine();

// ============================================
// PASO 3: Verificar estado del servidor
// ============================================
var status = await arca.GetStatusAsync();

Console.WriteLine("📊 Estado del Servidor:");
Console.WriteLine($"   Vault desbloqueado: {(status.IsUnlocked ? "✅ Sí" : "❌ No")}");
Console.WriteLine($"   Secretos disponibles: {status.SecretCount}");
Console.WriteLine($"   Requiere autenticación: {(status.RequiresAuthentication ? "🔐 Sí" : "⚠️ No")}");
Console.WriteLine();

if (!status.IsUnlocked)
{
    Console.WriteLine("❌ El vault no está desbloqueado.");
    Console.WriteLine("   Abre Arca.NET e ingresa tu contraseña maestra.");
    Console.ReadKey();
    return;
}

// ============================================
// PASO 4: Verificar autenticación
// ============================================
Console.WriteLine("🔐 Verificando autenticación...");

var isAvailable = await arca.IsAvailableAsync();

if (!isAvailable)
{
    if (status.RequiresAuthentication && string.IsNullOrEmpty(apiKey))
    {
        Console.WriteLine("❌ El servidor requiere API Key pero no se proporcionó ninguna.");
        Console.WriteLine();
        Console.WriteLine("Para generar una API Key:");
        Console.WriteLine("  1. Abre Arca.NET");
        Console.WriteLine("  2. Haz clic en '🔑 API Keys'");
        Console.WriteLine("  3. Genera una nueva key");
        Console.WriteLine("  4. Configura la variable de entorno ARCA_API_KEY");
    }
    else if (status.RequiresAuthentication)
    {
        Console.WriteLine("❌ API Key inválida o revocada.");
        Console.WriteLine("   Verifica que la API Key sea correcta.");
    }
    else
    {
        Console.WriteLine("❌ No se pudo conectar a Arca.");
    }
    Console.ReadKey();
    return;
}

Console.WriteLine("✅ Autenticación exitosa!");
Console.WriteLine();

// ============================================
// PASO 5: Listar secretos disponibles
// ============================================
Console.WriteLine("━━━ Secretos Disponibles ━━━");

try
{
    var keys = await arca.ListKeysAsync();

    if (keys.Count == 0)
    {
        Console.WriteLine("   (No hay secretos disponibles para esta API Key)");
    }
    else
    {
        foreach (var key in keys)
        {
            Console.WriteLine($"   🔑 {key}");
        }
    }
}
catch (ArcaAccessDeniedException)
{
    Console.WriteLine("   ⚠️  Tu API Key no tiene permiso para listar secretos.");
    Console.WriteLine("   💡 Contacta al administrador para habilitar 'Can list available secrets'.");
    Console.WriteLine("   📝 Aún puedes obtener secretos específicos si tienes permiso.");
}
catch (ArcaException ex)
{
    Console.WriteLine($"   ❌ Error al listar: {ex.Message}");
}

Console.WriteLine();

// ============================================
// PASO 6: Obtener un secreto específico
// ============================================
Console.WriteLine("━━━ Obtener Secreto ━━━");
Console.Write("Ingresa el nombre del secreto a obtener (o Enter para saltar): ");
var secretName = Console.ReadLine();

if (!string.IsNullOrWhiteSpace(secretName))
{
    try
    {
        // Método 1: GetSecretValueAsync (lanza excepción si no existe o no tiene permiso)
        var secretValue = await arca.GetSecretValueAsync(secretName);
        Console.WriteLine($"✅ Valor: {secretValue}");
    }
    catch (ArcaAccessDeniedException)
    {
        Console.WriteLine($"🚫 Acceso denegado al secreto '{secretName}'.");
        Console.WriteLine("   Tu API Key no tiene permiso para acceder a este secreto.");
    }
    catch (ArcaSecretNotFoundException)
    {
        Console.WriteLine($"❌ El secreto '{secretName}' no existe.");
    }
    catch (ArcaException ex)
    {
        Console.WriteLine($"❌ Error: {ex.Message}");
    }

    Console.WriteLine();

    // Método 2: GetSecretAsync (retorna resultado con Success/Error/IsAccessDenied)
    Console.WriteLine("Usando GetSecretAsync (sin excepciones):");
    var result = await arca.GetSecretAsync(secretName);
    Console.WriteLine($"   Success: {result.Success}");
    Console.WriteLine($"   IsAccessDenied: {result.IsAccessDenied}");
    Console.WriteLine($"   Value: {result.Value ?? "(null)"}");
    Console.WriteLine($"   Description: {result.Description ?? "(sin descripción)"}");
    Console.WriteLine($"   Error: {result.Error ?? "(ninguno)"}");
}

Console.WriteLine();

// ============================================
// PASO 7: Ejemplo de uso real
// ============================================
Console.WriteLine("━━━ Ejemplo de Uso Real ━━━");
Console.WriteLine();
Console.WriteLine("// Código de ejemplo para tu aplicación:");
Console.WriteLine();
Console.WriteLine("```csharp");
Console.WriteLine("// En Program.cs o Startup.cs");
Console.WriteLine("var apiKey = Environment.GetEnvironmentVariable(\"ARCA_API_KEY\");");
Console.WriteLine("using var arca = new ArcaSimpleClient(apiKey: apiKey);");
Console.WriteLine();
Console.WriteLine("if (await arca.IsAvailableAsync())");
Console.WriteLine("{");
Console.WriteLine("    try");
Console.WriteLine("    {");
Console.WriteLine("        var connectionString = await arca.GetSecretValueAsync(\"ConnectionStrings:Database\");");
Console.WriteLine("        // Usar connectionString...");
Console.WriteLine("    }");
Console.WriteLine("    catch (ArcaAccessDeniedException)");
Console.WriteLine("    {");
Console.WriteLine("        // Tu API Key no tiene permiso para este secreto");
Console.WriteLine("    }");
Console.WriteLine("}");
Console.WriteLine("```");

Console.WriteLine();
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine("Presiona cualquier tecla para salir...");
Console.ReadKey();
