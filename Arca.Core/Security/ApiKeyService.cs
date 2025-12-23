using System.Security.Cryptography;
using System.Text;

namespace Arca.Core.Security;

public static class ApiKeyService
{
    private const string Prefix = "arca_";
    private const int KeyLength = 32; // 256 bits

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

    public static string ComputeHash(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static bool IsValidFormat(string apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey) && apiKey.StartsWith(Prefix);
    }
}
