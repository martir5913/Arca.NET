using System.Security.Cryptography;
using System.Text;

namespace Arca.Core.Security;

/// <summary>
/// Servicio para generar y validar API Keys.
/// </summary>
public static class ApiKeyService
{
    private const string Prefix = "arca_";
    private const int KeyLength = 32; // 256 bits

    /// <summary>
    /// Genera una nueva API Key.
    /// </summary>
    /// <returns>Tuple con la API Key en texto plano (para mostrar al usuario) y su hash (para almacenar)</returns>
    public static (string apiKey, string keyHash) GenerateApiKey()
    {
        // Generar bytes aleatorios
        var keyBytes = new byte[KeyLength];
        RandomNumberGenerator.Fill(keyBytes);
        
        // Convertir a Base64 URL-safe
        var keyBase64 = Convert.ToBase64String(keyBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
        
        // Agregar prefijo para identificar fácilmente
        var apiKey = $"{Prefix}{keyBase64}";
        
        // Calcular hash para almacenamiento
        var keyHash = ComputeHash(apiKey);
        
        return (apiKey, keyHash);
    }

    /// <summary>
    /// Calcula el hash SHA256 de una API Key.
    /// </summary>
    public static string ComputeHash(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifica si una API Key tiene el formato correcto.
    /// </summary>
    public static bool IsValidFormat(string apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey) && apiKey.StartsWith(Prefix);
    }
}
