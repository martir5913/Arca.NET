namespace Arca.Core.Interfaces;

public interface IKeyDerivationService
{
    byte[] DeriveKey(string password, byte[] salt);
    byte[] GenerateSalt();
    bool VerifyKey(string password, byte[] salt, byte[] expectedKey);
}
