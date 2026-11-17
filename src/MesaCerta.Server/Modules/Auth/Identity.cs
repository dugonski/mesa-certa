using System.Security.Cryptography;
using System.Text;

namespace MesaCerta.Server.Modules.Auth;

public static class Identity
{
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public const int WorkFactor = 12;

    public static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public static bool VerifyPassword(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
