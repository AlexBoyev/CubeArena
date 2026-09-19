using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CubeArena.Api.Features.Sessions;

// Issues and describes (via JWKS) the asymmetrically-signed connect tickets that the
// Phase 4 game server will validate offline using only the public key. The private key
// never leaves this class — nothing in the API returns it, and no shared secret is ever
// sent to a client build (see docs/ARCHITECTURE.md section 4, connect-ticket threats).
public sealed class TicketService : IDisposable
{
    private readonly TicketOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ECDsa _key;

    public TicketService(IOptions<TicketOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _key = ECDsa.Create();
        // Env-var-sourced PEMs (docker-compose) carry literal "\n" two-char sequences since
        // shell env vars can't hold real newlines reliably; JSON-sourced ones (appsettings)
        // already contain real newlines, so this normalization is a no-op for those.
        _key.ImportFromPem(_options.SigningKeyPem.Replace("\\n", "\n"));
    }

    public string CreateTicket(Guid sessionId, Guid userId, int slotIndex)
    {
        var now = _timeProvider.GetUtcNow();
        var securityKey = new ECDsaSecurityKey(_key) { KeyId = _options.KeyId };
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("sid", sessionId.ToString()),
            new Claim("slot", slotIndex.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: (now + _options.TicketLifetime).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public JsonWebKeySet GetPublicJwks()
    {
        var publicKey = ECDsa.Create();
        publicKey.ImportParameters(_key.ExportParameters(includePrivateParameters: false));
        var securityKey = new ECDsaSecurityKey(publicKey) { KeyId = _options.KeyId };

        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityKey);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.EcdsaSha256;

        var jwks = new JsonWebKeySet();
        jwks.Keys.Add(jwk);
        return jwks;
    }

    public void Dispose() => _key.Dispose();
}
