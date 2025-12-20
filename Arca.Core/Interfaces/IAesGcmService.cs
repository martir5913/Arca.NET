namespace Arca.Core.Interfaces;

/// <summary>
/// Servicio de cifrado AES-256-GCM (Autenticado).
/// </summary>
public interface IAesGcmService
{
    /// <summary>
    /// Cifra los datos usando AES-256-GCM.
    /// </summary>
    byte[] Encrypt(byte[] plaintext, byte[] key);

    /// <summary>
    /// Descifra los datos usando AES-256-GCM.
    /// </summary>
    byte[] Decrypt(byte[] ciphertext, byte[] key);
}
