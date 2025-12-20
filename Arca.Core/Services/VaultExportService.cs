using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arca.Core.Entities;

namespace Arca.Core.Services;

/// <summary>
/// Datos exportados del vault.
/// </summary>
public sealed record VaultExportData
{
    public required int Version { get; init; }
    public required DateTime ExportedAt { get; init; }
    public required string ExportedFrom { get; init; }
    public required List<ExportedSecret> Secrets { get; init; }
    public required List<ExportedApiKey> ApiKeys { get; init; }
}

public sealed record ExportedSecret
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public string? Description { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record ExportedApiKey
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string AccessLevel { get; init; }
    public required List<string> AllowedSecrets { get; init; }
    public required bool CanList { get; init; }
}

/// <summary>
/// Opciones para importar un vault.
/// </summary>
public sealed class ImportOptions
{
    /// <summary>
    /// Si es true, reemplaza secretos existentes con el mismo nombre.
    /// Si es false, salta secretos que ya existen.
    /// </summary>
    public bool OverwriteExisting { get; set; } = false;

    /// <summary>
    /// Si es true, importa también las API Keys.
    /// </summary>
    public bool ImportApiKeys { get; set; } = true;
}

/// <summary>
/// Resultado de la importación.
/// </summary>
public sealed class ImportResult
{
    public bool Success { get; init; }
    public int SecretsImported { get; init; }
    public int SecretsSkipped { get; init; }
    public int ApiKeysImported { get; init; }
    public int ApiKeysSkipped { get; init; }
    public string? Error { get; init; }

    public static ImportResult Succeeded(int secretsImported, int secretsSkipped, int apiKeysImported, int apiKeysSkipped)
        => new()
        {
            Success = true,
            SecretsImported = secretsImported,
            SecretsSkipped = secretsSkipped,
            ApiKeysImported = apiKeysImported,
            ApiKeysSkipped = apiKeysSkipped
        };

    public static ImportResult Failed(string error)
        => new() { Success = false, Error = error };
}

/// <summary>
/// Servicio para exportar e importar vaults.
/// </summary>
public sealed class VaultExportService
{
    private const string MagicHeader = "ARCAEXPORT";
    private const int CurrentVersion = 1;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100000;

    /// <summary>
    /// Exporta los secretos y API Keys a un archivo cifrado.
    /// </summary>
    /// <param name="secrets">Secretos a exportar</param>
    /// <param name="apiKeys">API Keys a exportar</param>
    /// <param name="exportPassword">Contraseña para cifrar el archivo de exportación</param>
    /// <param name="filePath">Ruta donde guardar el archivo</param>
    public async Task ExportAsync(
        IEnumerable<SecretEntry> secrets,
        IEnumerable<ApiKeyEntry> apiKeys,
        string exportPassword,
        string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Preparar datos de exportación
        var exportData = new VaultExportData
        {
            Version = CurrentVersion,
            ExportedAt = DateTime.UtcNow,
            ExportedFrom = Environment.MachineName,
            Secrets = secrets.Select(s => new ExportedSecret
            {
                Key = s.Key,
                Value = s.Value,
                Description = s.Description,
                CreatedAt = s.CreatedAt
            }).ToList(),
            ApiKeys = apiKeys.Select(k => new ExportedApiKey
            {
                Name = k.Name,
                Description = k.Description,
                CreatedAt = k.CreatedAt,
                AccessLevel = k.Permissions.Level.ToString(),
                AllowedSecrets = k.Permissions.AllowedSecrets,
                CanList = k.Permissions.CanList
            }).ToList()
        };

        // Serializar a JSON
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(exportData, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        // Comprimir
        using var compressedStream = new MemoryStream();
        await using (var gzip = new GZipStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            await gzip.WriteAsync(jsonBytes);
        }
        var compressedData = compressedStream.ToArray();

        // Generar salt y derivar clave
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(exportPassword, salt);

        // Cifrar con AES-GCM
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[compressedData.Length];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, compressedData, ciphertext, tag);

        // Escribir archivo
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await using var writer = new BinaryWriter(fileStream);

        // Header
        writer.Write(Encoding.ASCII.GetBytes(MagicHeader));
        writer.Write(CurrentVersion);
        writer.Write(salt);
        writer.Write(nonce);
        writer.Write(tag);
        writer.Write(ciphertext.Length);
        writer.Write(ciphertext);
    }

    /// <summary>
    /// Importa secretos desde un archivo de exportación.
    /// </summary>
    /// <param name="filePath">Ruta del archivo a importar</param>
    /// <param name="exportPassword">Contraseña del archivo de exportación</param>
    /// <returns>Datos importados o null si falla</returns>
    public async Task<VaultExportData?> LoadExportFileAsync(string filePath, string exportPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPassword);

        if (!File.Exists(filePath))
            return null;

        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fileStream);

        // Verificar header
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(MagicHeader.Length));
        if (magic != MagicHeader)
            throw new InvalidDataException("Invalid export file format.");

        var version = reader.ReadInt32();
        if (version > CurrentVersion)
            throw new InvalidDataException($"Export file version {version} is not supported. Maximum supported: {CurrentVersion}");

        // Leer datos de cifrado
        var salt = reader.ReadBytes(SaltSize);
        var nonce = reader.ReadBytes(12);
        var tag = reader.ReadBytes(16);
        var ciphertextLength = reader.ReadInt32();
        var ciphertext = reader.ReadBytes(ciphertextLength);

        // Derivar clave y descifrar
        var key = DeriveKey(exportPassword, salt);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidOperationException("Invalid password or corrupted file.");
        }

        // Descomprimir
        using var compressedStream = new MemoryStream(plaintext);
        using var decompressedStream = new MemoryStream();
        await using (var gzip = new GZipStream(compressedStream, CompressionMode.Decompress))
        {
            await gzip.CopyToAsync(decompressedStream);
        }

        // Deserializar
        var jsonBytes = decompressedStream.ToArray();
        return JsonSerializer.Deserialize<VaultExportData>(jsonBytes);
    }

    /// <summary>
    /// Valida si un archivo es un export válido de Arca.
    /// </summary>
    public async Task<bool> IsValidExportFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fileStream);

            var magic = Encoding.ASCII.GetString(reader.ReadBytes(MagicHeader.Length));
            return magic == MagicHeader;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }
}
