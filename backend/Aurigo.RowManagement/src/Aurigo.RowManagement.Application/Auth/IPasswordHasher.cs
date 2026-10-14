namespace Aurigo.RowManagement.Application.Auth;

/// <summary>Hashes and verifies user passwords. Implementations must never store or log plaintext.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
}
