using System.Security.Cryptography;
using Aurigo.RowManagement.Application.Auth;

namespace Aurigo.RowManagement.Infrastructure.Auth;

/// <summary>
/// PBKDF2 (SHA-256) hasher using only the BCL. Stored format is "iterations.saltBase64.hashBase64";
/// the salt is embedded so no separate column is needed. Verification is constant-time.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    // LoginCommandHandler.DummyHash mirrors this iteration count.
    private const int Iterations = 100_000;
    private const int SaltSize = 16;   // bytes
    private const int KeySize = 32;    // bytes (256-bit)

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password is required.", nameof(password));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;
        var parts = storedHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations <= 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException) { return false; }

        if (expected.Length == 0) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
