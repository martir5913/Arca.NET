using Arca.SDK;
using Arca.SDK.Clients;

Console.WriteLine("===========================================");
Console.WriteLine("       Arca.SDK - Test de Conexión");
Console.WriteLine("===========================================\n");

// Obtener API Key desde variable de entorno o solicitarla
var apiKey = Environment.GetEnvironmentVariable("ARCA_API_KEY");

if (string.IsNullOrEmpty(apiKey))
{
    Console.Write("Ingresa tu API Key (o presiona Enter para modo sin autenticación): ");
    apiKey = Console.ReadLine();
}

// Crear cliente
using var arca = new ArcaSimpleClient(apiKey: apiKey);

Console.WriteLine("\n[1] Verificando disponibilidad del servidor...\n");

try
{
    var isAvailable = await arca.IsAvailableAsync();

    if (!isAvailable)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("? El servidor Arca NO está disponible.");
        Console.WriteLine("\nPosibles causas:");
        Console.WriteLine("  - Arca.NET no está ejecutándose");
        Console.WriteLine("  - El vault no está desbloqueado");
        Console.WriteLine("  - La API Key es inválida");
        Console.ResetColor();
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("? Servidor Arca disponible y autenticado!\n");
    Console.ResetColor();

    // Obtener estado del vault
    var status = await arca.GetStatusAsync();
    Console.WriteLine($"[2] Estado del Vault:");
    Console.WriteLine($"    - Desbloqueado: {status.IsUnlocked}");
    Console.WriteLine($"    - Secretos disponibles: {status.SecretCount}");
    Console.WriteLine($"    - Requiere autenticación: {status.RequiresAuthentication}\n");

    // Listar secretos disponibles
    Console.WriteLine("[3] Listando secretos disponibles...\n");
    
    try
    {
        var keys = await arca.ListKeysAsync();

        if (keys.Count == 0)
        {
            Console.WriteLine("    (No hay secretos o no tienes permiso para listarlos)\n");
        }
        else
        {
            Console.WriteLine($"    Secretos encontrados ({keys.Count}):");
            foreach (var key in keys.Take(10)) // Mostrar máximo 10
            {
                Console.WriteLine($"      • {key}");
            }
            if (keys.Count > 10)
            {
                Console.WriteLine($"      ... y {keys.Count - 10} más\n");
            }
            Console.WriteLine();
        }
    }
    catch (ArcaAccessDeniedException)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("    ?? Tu API Key no tiene permiso para listar secretos.\n");
        Console.ResetColor();
    }

    // Solicitar un secreto específico
    Console.Write("[4] Ingresa el nombre de un secreto para obtener (o Enter para omitir): ");
    var secretKey = Console.ReadLine();

    if (!string.IsNullOrWhiteSpace(secretKey))
    {
        Console.WriteLine($"\n    Obteniendo secreto: '{secretKey}'...\n");

        var result = await arca.GetSecretAsync(secretKey);

        if (result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    ? Secreto encontrado!");
            Console.ResetColor();
            Console.WriteLine($"    - Clave: {secretKey}");
            Console.WriteLine($"    - Valor: {MaskValue(result.Value!)}");
            if (!string.IsNullOrEmpty(result.Description))
            {
                Console.WriteLine($"    - Descripción: {result.Description}");
            }
        }
        else if (result.IsAccessDenied)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    ?? Acceso denegado al secreto '{secretKey}'");
            Console.WriteLine("       Tu API Key no tiene permiso para este secreto.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    ? Secreto no encontrado: '{secretKey}'");
            if (!string.IsNullOrEmpty(result.Error))
            {
                Console.WriteLine($"       Error: {result.Error}");
            }
            Console.ResetColor();
        }
    }

    Console.WriteLine("\n===========================================");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("       ? Prueba completada con éxito");
    Console.ResetColor();
    Console.WriteLine("===========================================");
}
catch (ArcaException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"? Error de Arca: {ex.Message}");
    Console.ResetColor();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"? Error inesperado: {ex.Message}");
    Console.ResetColor();
}

Console.WriteLine("\nPresiona cualquier tecla para salir...");
Console.ReadKey();

// Función auxiliar para enmascarar valores sensibles
static string MaskValue(string value)
{
    if (string.IsNullOrEmpty(value))
        return "(vacío)";
    
    if (value.Length <= 4)
        return new string('*', value.Length);
    
    return value[..2] + new string('*', Math.Min(value.Length - 4, 20)) + value[^2..];
}
