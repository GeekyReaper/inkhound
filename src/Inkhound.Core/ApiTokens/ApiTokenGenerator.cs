using System.Security.Cryptography;
using System.Text;

namespace Inkhound.Core.ApiTokens;

public static class ApiTokenGenerator
{
    private const string Prefix = "ihk_";

    public static (string RawToken, string DisplayPrefix, string Hash) Generate()
    {
        var secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var raw = Prefix + secret;
        return (raw, raw[..Math.Min(12, raw.Length)], Hash(raw));
    }

    public static string Hash(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public static bool HasPrefix(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
