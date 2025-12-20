using Arca.SDK;
using Arca.SDK.Clients;

Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║     Arca SDK Test Console            ║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.WriteLine();

using var arca = new ArcaSimpleClient();

// Verificar disponibilidad
Console.WriteLine("Verificando conexión con Arca...");

if (!await arca.IsAvailableAsync())
{
    Console.WriteLine();
    Console.WriteLine("❌ Arca no está disponible.");
    Console.WriteLine("   Asegúrate de que Arca.NET esté corriendo y el vault desbloqueado.");
    Console.WriteLine();
    Console.WriteLine("Presiona cualquier tecla para salir...");
    Console.ReadKey();
    return;
}

Console.WriteLine("✅ Conectado a Arca!");
Console.WriteLine();

// Obtener estado
var status = await arca.GetStatusAsync();
Console.WriteLine($"📊 Vault: {status.SecretCount} secreto(s) disponibles");
Console.WriteLine();

// ============================================
// FORMA 1: Obtener un secreto específico
// ============================================
Console.WriteLine("━━━ FORMA 1: Obtener secreto específico ━━━");

try
{
    var devSecret = await arca.GetSecretValueAsync("dev");
    Console.WriteLine($"✅ Secreto 'dev': {devSecret}");
}
catch (ArcaSecretNotFoundException)
{
    Console.WriteLine("❌ El secreto 'dev' no existe");
}

Console.WriteLine();

// ============================================
// FORMA 2: Obtener con manejo de resultado
// ============================================
Console.WriteLine("━━━ FORMA 2: Obtener con resultado ━━━");

var devResult = await arca.GetSecretAsync("dev");
if (devResult.Success)
{
    Console.WriteLine($"✅ Valor: {devResult.Value}");
    Console.WriteLine($"   Descripción: {devResult.Description ?? "(sin descripción)"}");
}
else
{
    Console.WriteLine($"❌ Error: {devResult.Error}");
}

Console.WriteLine();

// ============================================
// FORMA 3: Listar todas las claves
// ============================================
Console.WriteLine("━━━ FORMA 3: Listar todas las claves ━━━");

var allKeys = await arca.ListKeysAsync();
Console.WriteLine($"Claves disponibles ({allKeys.Count}):");

foreach (var key in allKeys)
{
    var secretValue = await arca.GetSecretAsync(key);
    if (secretValue.Success)
    {
        Console.WriteLine($"  🔑 {key} = {secretValue.Value}");
    }
}

Console.WriteLine();

// ============================================
// FORMA 4: Obtener múltiples secretos
// ============================================
Console.WriteLine("━━━ FORMA 4: Obtener múltiples secretos ━━━");

var keysToGet = new[] { "dev", "ConnectionStrings:SqlServer", "ApiKey" };
var multipleSecrets = await arca.GetSecretsAsync(keysToGet);

foreach (var kvp in multipleSecrets)
{
    if (kvp.Value.Success)
        Console.WriteLine($"  ✅ {kvp.Key} = {kvp.Value.Value}");
    else
        Console.WriteLine($"  ❌ {kvp.Key} = No encontrado");
}

Console.WriteLine();
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine("Presiona cualquier tecla para salir...");
Console.ReadKey();
