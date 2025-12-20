namespace Arca.Core.Interfaces;

/// <summary>
/// Servicio de derivación de claves usando Argon2id.
/// </summary>
public interface IKeyDerivationService
{
    /// <summary>
    /// Deriva una clave simétrica a partir del password del usuario.
    /// </summary>
    byte[] DeriveKey(string password, byte[] salt);

    /// <summary>
    /// Genera un salt aleatorio seguro.
    /// </summary>
    byte[] GenerateSalt();

    /// <summary>
    /// Verifica si un password produce la misma clave derivada.
    /// </summary>
    bool VerifyKey(string password, byte[] salt, byte[] expectedKey);
}
