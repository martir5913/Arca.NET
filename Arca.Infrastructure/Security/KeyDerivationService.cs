using System.Security.Cryptography;
using Arca.Core.Interfaces;
using Konscious.Security.Cryptography;

namespace Arca.Infrastructure.Security;

/// <summary>
/// Implementación de derivación de claves usando Argon2id.
/// Parámetros recomendados por OWASP para alta seguridad.
/// </summary>
public sealed class KeyDerivationService : IKeyDerivationService
{
    private const int SaltSize = 16;        // 128 bits
    private const int KeySize = 32;         // 256 bits para AES-256
    private const int DegreeOfParallelism = 4;
    private const int MemorySize = 65536;   // 64 MB
    private const int Iterations = 3;

    public byte[] DeriveKey(string password, byte[] salt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentNullException.ThrowIfNull(salt);

        using var argon2 = new Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySize,
            Iterations = Iterations
        };

        return argon2.GetBytes(KeySize);
    }

    public byte[] GenerateSalt()
    {
        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    public bool VerifyKey(string password, byte[] salt, byte[] expectedKey)
    {
        var derivedKey = DeriveKey(password, salt);
        return CryptographicOperations.FixedTimeEquals(derivedKey, expectedKey);
    }
}
