using System.Security.Cryptography;
using System.Text;
using Worky.Core;

namespace Worky.Core.Tests;

public class PkceTests
{
    const string Rfc7636Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    const string Rfc7636Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Fact]
    public void ChallengeMatchesRfc7636AppendixBVector()
    {
        Assert.Equal(Rfc7636Challenge, Pkce.CreateChallenge(Rfc7636Verifier));
    }

    [Fact]
    public void GeneratedVerifierStaysInAllowedCharsetAndLength()
    {
        for (var i = 0; i < 50; i++)
        {
            var verifier = Pkce.GenerateVerifier();
            Assert.InRange(verifier.Length, 43, 128);
            Assert.Matches("^[A-Za-z0-9\\-._~]+$", verifier);
        }
    }

    [Fact]
    public void ChallengeIsUnpaddedBase64UrlSha256OfVerifier()
    {
        var verifier = Pkce.GenerateVerifier();
        var challenge = Pkce.CreateChallenge(verifier);

        Assert.Equal(43, challenge.Length);
        Assert.DoesNotContain('=', challenge);
        Assert.Equal(
            Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('='),
            challenge);
    }

    [Fact]
    public void StateIsUrlSafeAndUnique()
    {
        var states = Enumerable.Range(0, 20).Select(_ => Pkce.CreateState()).ToHashSet();

        Assert.Equal(20, states.Count);
        Assert.All(states, s => Assert.Matches("^[A-Za-z0-9_-]+$", s));
    }
}
