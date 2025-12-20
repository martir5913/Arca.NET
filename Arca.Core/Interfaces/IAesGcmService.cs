namespace Arca.Core.Interfaces;

public interface IAesGcmService
{
    // cifrado y descifrado usando AES-256-GCM
    byte[] Encrypt(byte[] plaintext, byte[] key);
    byte[] Decrypt(byte[] ciphertext, byte[] key);
}
