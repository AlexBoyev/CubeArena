using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CubeArena.Shared;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;

namespace CubeArena.Server
{
    // Offline validation of the connect ticket described in the brief's section 3.2:
    // checks signature, expiry, issuer, audience, session id, and (via the caller-owned
    // used-jti set) replay. No network calls — the public key is fetched once at boot
    // from the backend's JWKS endpoint and cached by the caller.
    //
    // Uses BouncyCastle rather than System.Security.Cryptography: on this Unity/Mono
    // runtime, ECDsa.Create() throws NotImplementedException and RSA.Create() hangs
    // indefinitely (verified in an actual built player, not just the Editor) — Unity's
    // built-in asymmetric crypto factories are non-functional here. BouncyCastle is pure
    // managed code with no dependency on those broken native provider bindings.
    public readonly struct TicketValidationResult
    {
        public bool IsValid { get; }
        public ConnectRejectionReason RejectionReason { get; }
        public Guid UserId { get; }
        public Guid SessionId { get; }
        public int SlotIndex { get; }
        public string Jti { get; }

        private TicketValidationResult(bool isValid, ConnectRejectionReason rejectionReason,
            Guid userId, Guid sessionId, int slotIndex, string jti)
        {
            IsValid = isValid;
            RejectionReason = rejectionReason;
            UserId = userId;
            SessionId = sessionId;
            SlotIndex = slotIndex;
            Jti = jti;
        }

        public static TicketValidationResult Valid(Guid userId, Guid sessionId, int slotIndex, string jti) =>
            new(true, default, userId, sessionId, slotIndex, jti);

        public static TicketValidationResult Invalid(ConnectRejectionReason reason) =>
            new(false, reason, default, default, default, null);
    }

    public class TicketValidator
    {
        private readonly ECPublicKeyParameters _publicKey;
        private readonly string _expectedIssuer;
        private readonly string _expectedAudience;

        public TicketValidator(ECPublicKeyParameters publicKey, string expectedIssuer, string expectedAudience)
        {
            _publicKey = publicKey;
            _expectedIssuer = expectedIssuer;
            _expectedAudience = expectedAudience;
        }

        public TicketValidationResult Validate(
            string ticket, Guid expectedSessionId, IReadOnlyCollection<string> usedJti, DateTimeOffset now)
        {
            var parts = ticket?.Split('.');
            if (parts == null || parts.Length != 3)
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.InvalidTicketFormat);
            }

            byte[] signature;
            byte[] headerBytes;
            byte[] payloadBytes;
            try
            {
                signature = Base64UrlDecode(parts[2]);
                headerBytes = Base64UrlDecode(parts[0]);
                payloadBytes = Base64UrlDecode(parts[1]);
            }
            catch (FormatException)
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.InvalidTicketFormat);
            }

            if (signature.Length != 64)
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.InvalidTicketFormat);
            }

            var signedInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            var hash = Sha256(signedInput);

            // JWS ES256 signatures are the IEEE P1363 R||S concatenation: 32 bytes each.
            var r = new BigInteger(1, signature, 0, 32);
            var s = new BigInteger(1, signature, 32, 32);

            var signer = new ECDsaSigner();
            signer.Init(false, _publicKey);
            if (!signer.VerifySignature(hash, r, s))
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.InvalidSignature);
            }

            JObject header;
            JObject payload;
            try
            {
                header = JObject.Parse(Encoding.UTF8.GetString(headerBytes));
                payload = JObject.Parse(Encoding.UTF8.GetString(payloadBytes));
            }
            catch (Exception)
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.InvalidTicketFormat);
            }

            if ((string)header["alg"] != "ES256")
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.InvalidTicketFormat);
            }

            var exp = payload.Value<long?>("exp");
            if (exp is null)
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.InvalidTicketFormat);
            }

            if (now > DateTimeOffset.FromUnixTimeSeconds(exp.Value))
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.Expired);
            }

            var nbf = payload.Value<long?>("nbf");
            if (nbf is not null && now < DateTimeOffset.FromUnixTimeSeconds(nbf.Value))
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.Expired);
            }

            if ((string)payload["iss"] != _expectedIssuer)
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.WrongIssuer);
            }

            if ((string)payload["aud"] != _expectedAudience)
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.WrongAudience);
            }

            if (!Guid.TryParse((string)payload["sid"], out var sessionId) || sessionId != expectedSessionId)
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.WrongSession);
            }

            var jti = (string)payload["jti"];
            if (string.IsNullOrEmpty(jti))
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.InvalidTicketFormat);
            }

            if (usedJti.Contains(jti))
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.ReplayedJti);
            }

            if (!Guid.TryParse((string)payload["sub"], out var userId))
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.InvalidTicketFormat);
            }

            if (!int.TryParse((string)payload["slot"], out var slotIndex))
            {
                return TicketValidationResult.Invalid(ConnectRejectionReason.InvalidTicketFormat);
            }

            return TicketValidationResult.Valid(userId, sessionId, slotIndex, jti);
        }

        private static byte[] Sha256(byte[] data)
        {
            var digest = new Sha256Digest();
            digest.BlockUpdate(data, 0, data.Length);
            var hash = new byte[digest.GetDigestSize()];
            digest.DoFinal(hash, 0);
            return hash;
        }

        private static byte[] Base64UrlDecode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
