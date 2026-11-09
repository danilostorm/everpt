using System.Security.Cryptography;

namespace PristonTale.Persistence.Abstractions.Security;

public static class PasswordHasher
{
    private const string Scheme = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string encoded)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encoded))
            return false;

        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations))
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
