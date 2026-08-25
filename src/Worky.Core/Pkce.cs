using System.Security.Cryptography;
using System.Text;

namespace Worky.Core;

public static class Pkce
{
    const string VerifierChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
    const int VerifierLength = 64;

    public static string GenerateVerifier() =>
        new(RandomNumberGenerator.GetItems<char>(VerifierChars.AsSpan(), VerifierLength));

    public static string CreateChallenge(string verifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string CreateState() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
