using System.IdentityModel.Tokens.Jwt;
using CubeArena.Api.Features.Sessions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;

namespace CubeArena.Tests.Sessions;

public class TicketServiceTests
{
    // Test-only key, never used outside this file.
    private const string TestSigningKeyPem = """
        -----BEGIN EC PRIVATE KEY-----
        MHcCAQEEIPL41AQaBDyEWp74wh4x7HHJTLk/H+wGMuX8vhClj5BboAoGCCqGSM49
        AwEHoUQDQgAEu1onIul7buAJHIFBughJQsNefqRgfZtEkRifDqF9/ogN8HMejaZK
        XtRU0QIkBnTqI04BXCQKH4y+H2jUTvVfLw==
        -----END EC PRIVATE KEY-----
        """;

    private static readonly TicketOptions TestOptions = new()
    {
        SigningKeyPem = TestSigningKeyPem,
        Issuer = "cubearena-api-test",
        Audience = "gameserver",
        TicketLifetime = TimeSpan.FromSeconds(60),
        KeyId = "test-key-1"
    };

    // Mirrors what the Phase 4 Unity game server will do: fetch the JWKS, build
    // TokenValidationParameters purely from the public key material, and validate
    // signature/expiry/audience/session-id — with no access to the private key at all.
    private static TokenValidationParameters BuildStubValidatorParameters(JsonWebKeySet jwks, string expectedSessionId)
    {
        var jwk = Assert.Single(jwks.Keys);
        var publicKey = new ECDsaSecurityKey(
            System.Security.Cryptography.ECDsa.Create(new System.Security.Cryptography.ECParameters
            {
                Curve = System.Security.Cryptography.ECCurve.NamedCurves.nistP256,
                Q = new System.Security.Cryptography.ECPoint
                {
                    X = Base64UrlEncoder.DecodeBytes(jwk.X),
                    Y = Base64UrlEncoder.DecodeBytes(jwk.Y)
                }
            }))
        { KeyId = jwk.Kid };

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TestOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = TestOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = publicKey,
            ClockSkew = TimeSpan.Zero
        };
    }

    [Fact]
    public void GetPublicJwks_ExposesOnlyThePublicKey()
    {
        var service = new TicketService(Options.Create(TestOptions), TimeProvider.System);

        var jwks = service.GetPublicJwks();

        var jwk = Assert.Single(jwks.Keys);
        Assert.Equal("EC", jwk.Kty);
        Assert.Equal("P-256", jwk.Crv);
        Assert.Equal("test-key-1", jwk.Kid);
        Assert.False(jwk.HasPrivateKey);
    }

    [Fact]
    public void StubValidator_AcceptsAGoodTicket_UsingOnlyThePublicJwks()
    {
        // The stub validator, like the real Phase 4 game server, checks expiry against
        // the real wall clock — so the ticket must be issued relative to real "now".
        var service = new TicketService(Options.Create(TestOptions), TimeProvider.System);
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var ticket = service.CreateTicket(sessionId, userId, slotIndex: 2);
        var jwks = service.GetPublicJwks();
        var validationParameters = BuildStubValidatorParameters(jwks, sessionId.ToString());

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(ticket, validationParameters, out var validatedToken);

        Assert.Equal(userId.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        Assert.Equal(sessionId.ToString(), principal.FindFirst("sid")!.Value);
        Assert.Equal("2", principal.FindFirst("slot")!.Value);
        Assert.False(string.IsNullOrEmpty(principal.FindFirst(JwtRegisteredClaimNames.Jti)!.Value));
        Assert.Equal("test-key-1", ((JwtSecurityToken)validatedToken).Header.Kid);
    }

    [Fact]
    public void StubValidator_RejectsAnExpiredTicket()
    {
        // Issue the ticket as if it were minted 2 minutes ago (real wall clock, since the
        // stub validator checks expiry against real time) so it's already past its 60s life.
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2));
        var service = new TicketService(Options.Create(TestOptions), time);
        var ticket = service.CreateTicket(Guid.NewGuid(), Guid.NewGuid(), slotIndex: 0);

        var jwks = service.GetPublicJwks();
        var validationParameters = BuildStubValidatorParameters(jwks, "irrelevant");
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        // The stub validator's own real-clock check catches the expiry (ticket's exp
        // was minted relative to the fake clock, which is now in the past for real).
        Assert.Throws<SecurityTokenExpiredException>(() =>
            handler.ValidateToken(ticket, validationParameters, out _));
    }

    [Fact]
    public void StubValidator_RejectsWrongAudience()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var wrongAudienceOptions = new TicketOptions
        {
            SigningKeyPem = TestSigningKeyPem,
            Audience = "not-a-gameserver"
        };
        var service = new TicketService(Options.Create(wrongAudienceOptions), time);
        var ticket = service.CreateTicket(Guid.NewGuid(), Guid.NewGuid(), slotIndex: 0);

        var jwks = service.GetPublicJwks();
        var validationParameters = BuildStubValidatorParameters(jwks, "irrelevant");
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        Assert.Throws<SecurityTokenInvalidAudienceException>(() =>
            handler.ValidateToken(ticket, validationParameters, out _));
    }

    [Fact]
    public void StubValidator_RejectsATamperedSignature()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new TicketService(Options.Create(TestOptions), time);
        var ticket = service.CreateTicket(Guid.NewGuid(), Guid.NewGuid(), slotIndex: 0);
        var tampered = ticket[..^4] + (ticket[^4] == 'A' ? "B" : "A") + ticket[^3..];

        var jwks = service.GetPublicJwks();
        var validationParameters = BuildStubValidatorParameters(jwks, "irrelevant");
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        Assert.Throws<SecurityTokenInvalidSignatureException>(() =>
            handler.ValidateToken(tampered, validationParameters, out _));
    }
}
