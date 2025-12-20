using System.Security.Cryptography;
using Arca.Core.Interfaces;

namespace Arca.Infrastructure.Security;

/// <summary>
/// Implementación de cifrado AES-256-GCM (Autenticado).
/// El formato del ciphertext es: [Nonce (12 bytes)][Tag (16 bytes)][CiphertextData]
/// </summary>
public sealed class AesGcmService : IAesGcmService
{
    private const int NonceSize = 12; // 96 bits recomendado para GCM
    private const int TagSize = 16;   // 128 bits para máxima seguridad

    public byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != 32)
            throw new ArgumentException("La clave debe ser de 256 bits (32 bytes).", nameof(key));

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Formato: [Nonce][Tag][Ciphertext]
        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);

        return result;
    }

    public byte[] Decrypt(byte[] ciphertext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != 32)
            throw new ArgumentException("La clave debe ser de 256 bits (32 bytes).", nameof(key));

        if (ciphertext.Length < NonceSize + TagSize)
            throw new ArgumentException("El ciphertext es demasiado corto.", nameof(ciphertext));

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var encryptedData = new byte[ciphertext.Length - NonceSize - TagSize];

        Buffer.BlockCopy(ciphertext, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(ciphertext, NonceSize + TagSize, encryptedData, 0, encryptedData.Length);

        var plaintext = new byte[encryptedData.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, encryptedData, tag, plaintext);

        return plaintext;
    }
}
