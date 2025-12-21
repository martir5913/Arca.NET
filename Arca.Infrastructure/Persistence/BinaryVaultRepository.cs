using System.Text;
using System.Text.Json;
using Arca.Core.Entities;
using Arca.Core.Interfaces;

namespace Arca.Infrastructure.Persistence;

/// <summary>
/// Repositorio binario para el archivo vault (.vlt).
/// Formato del archivo:
/// [Magic "ARCA" (4 bytes)][Version (4 bytes)][Salt (16 bytes)][CreatedAt (8 bytes)][EncryptedPayload]
/// </summary>
public sealed class BinaryVaultRepository : IVaultRepository
{
    private static readonly byte[] MagicNumber = "ARCA"u8.ToArray();
    private const int HeaderSize = 4 + 4 + 16 + 8; // Magic + Version + Salt + CreatedAt

    private readonly IAesGcmService _aesGcmService;
    private readonly string _vaultPath;
    private readonly string _apiKeysPath;

    public BinaryVaultRepository(IAesGcmService aesGcmService, string? vaultPath = null)
    {
        _aesGcmService = aesGcmService;
        _vaultPath = vaultPath ?? GetDefaultVaultPath();
        _apiKeysPath = Path.ChangeExtension(_vaultPath, ".keys");
    }

    public bool VaultExists() => File.Exists(_vaultPath);

    public string GetVaultPath() => _vaultPath;

    public async Task CreateVaultAsync(VaultMetadata metadata, byte[] derivedKey)
    {
        var directory = Path.GetDirectoryName(_vaultPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Crear vault vacío
        var emptySecrets = Array.Empty<SecretEntry>();
        var payload = JsonSerializer.SerializeToUtf8Bytes(emptySecrets);
        var encryptedPayload = _aesGcmService.Encrypt(payload, derivedKey);

        await using var stream = new FileStream(_vaultPath, FileMode.Create, FileAccess.Write);
        await using var writer = new BinaryWriter(stream);

        // Header
        writer.Write(MagicNumber);
        writer.Write(metadata.Version);
        writer.Write(metadata.Salt);
        writer.Write(metadata.CreatedAt.ToBinary());

        // Encrypted payload
        writer.Write(encryptedPayload.Length);
        writer.Write(encryptedPayload);
    }

    public async Task<VaultMetadata?> LoadMetadataAsync()
    {
        if (!VaultExists())
            return null;

        await using var stream = new FileStream(_vaultPath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);

        // Verificar magic number
        var magic = reader.ReadBytes(4);
        if (!magic.SequenceEqual(MagicNumber))
            throw new InvalidDataException("El archivo no es un vault válido de Arca.");

        var version = reader.ReadInt32();
        var salt = reader.ReadBytes(16);
        var createdAt = DateTime.FromBinary(reader.ReadInt64());

        return new VaultMetadata(salt, version, createdAt);
    }

    public async Task<IReadOnlyList<SecretEntry>> LoadSecretsAsync(byte[] derivedKey)
    {
        if (!VaultExists())
            return [];

        await using var stream = new FileStream(_vaultPath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);

        // Saltar header
        reader.ReadBytes(HeaderSize);

        // Leer payload cifrado
        var payloadLength = reader.ReadInt32();
        var encryptedPayload = reader.ReadBytes(payloadLength);

        // Descifrar
        var payload = _aesGcmService.Decrypt(encryptedPayload, derivedKey);
        var secrets = JsonSerializer.Deserialize<List<SecretEntry>>(payload);

        return secrets ?? [];
    }

    public async Task SaveSecretsAsync(IEnumerable<SecretEntry> secrets, byte[] derivedKey)
    {
        var metadata = await LoadMetadataAsync()
            ?? throw new InvalidOperationException("El vault no existe.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(secrets.ToList());
        var encryptedPayload = _aesGcmService.Encrypt(payload, derivedKey);

        await using var stream = new FileStream(_vaultPath, FileMode.Create, FileAccess.Write);
        await using var writer = new BinaryWriter(stream);

        // Header (mantener metadata original)
        writer.Write(MagicNumber);
        writer.Write(metadata.Version);
        writer.Write(metadata.Salt);
        writer.Write(metadata.CreatedAt.ToBinary());

        // Encrypted payload
        writer.Write(encryptedPayload.Length);
        writer.Write(encryptedPayload);
    }

    public async Task<IReadOnlyList<ApiKeyEntry>> LoadApiKeysAsync(byte[] derivedKey)
    {
        if (!File.Exists(_apiKeysPath))
            return [];

        try
        {
            await using var stream = new FileStream(_apiKeysPath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);

            // Leer payload cifrado
            var payloadLength = reader.ReadInt32();
            var encryptedPayload = reader.ReadBytes(payloadLength);

            // Descifrar
            var payload = _aesGcmService.Decrypt(encryptedPayload, derivedKey);
            var apiKeys = JsonSerializer.Deserialize<List<ApiKeyEntry>>(payload);

            return apiKeys ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveApiKeysAsync(IEnumerable<ApiKeyEntry> apiKeys, byte[] derivedKey)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(apiKeys.ToList());
        var encryptedPayload = _aesGcmService.Encrypt(payload, derivedKey);

        await using var stream = new FileStream(_apiKeysPath, FileMode.Create, FileAccess.Write);
        await using var writer = new BinaryWriter(stream);

        // Solo guardar payload cifrado (las API Keys usan la misma clave derivada)
        writer.Write(encryptedPayload.Length);
        writer.Write(encryptedPayload);
    }

    private static string GetDefaultVaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Arca", "vault.vlt");
    }
}
