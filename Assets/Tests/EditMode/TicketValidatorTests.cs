using System;
using System.Collections.Generic;
using CubeArena.Server;
using CubeArena.Shared;
using NUnit.Framework;
using Org.BouncyCastle.Crypto.Parameters;

namespace CubeArena.Tests.EditMode
{
    // Fixtures below were signed by the real .NET backend's TicketService (ES256, same
    // code path as production) rather than generated in-Editor: Unity's Mono runtime here
    // has non-functional System.Security.Cryptography asymmetric factories (ECDsa.Create()
    // throws NotImplementedException, RSA.Create() hangs indefinitely — verified in an
    // actual built player), which is exactly why TicketValidator uses BouncyCastle instead.
    // These fixtures also prove .NET-signed / Unity-verified ES256 tickets interoperate.
    public class TicketValidatorTests
    {
        private const string Issuer = "cubearena-api-test";
        private const string Audience = "gameserver";
        private static readonly Guid SessionId = Guid.Parse("ba31dc86-4529-4931-9fd4-10be7a1af765");
        private static readonly Guid UserId = Guid.Parse("5178fb3a-608f-4ff6-95cd-b7582e0723f6");

        private const string PublicKeyX = "jtI-s8ojvMWcG85v1_M5nq68UqW0RKIhD_BUUHBqPVA";
        private const string PublicKeyY = "7kDrmzHEzR85pw93CDmKGYu2UB-nhC9DRTLv8DRDazg";

        // exp is ~2036 — long-lived on purpose, see class remarks.
        private const string ValidTicket =
            "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6InRlc3Qta2V5In0.eyJzdWIiOiI1MTc4ZmIzYS02MDhmLTRmZjYtOTVjZC1iNzU4MmUwNzIzZjYiLCJzaWQiOiJiYTMxZGM4Ni00NTI5LTQ5MzEtOWZkNC0xMGJlN2ExYWY3NjUiLCJzbG90IjoiMiIsImp0aSI6IjBlY2UzOWUyLTlkMGYtNDAxZC04MzAyLWEzZGFmYzZjNDZiYSIsIm5iZiI6MTc4OTg0NjA0MCwiZXhwIjoyMTA1MjA2MTAwLCJpc3MiOiJjdWJlYXJlbmEtYXBpLXRlc3QiLCJhdWQiOiJnYW1lc2VydmVyIn0.lH6xlJMfeqy1vmPOPIt-Du83mc0wTbhLx5tewKgl7uigmR_81BnT_pJ4Whdb6dhKTMNrkFvh1ayWkeu6uHrkOw";

        private const string ExpiredTicket =
            "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6InRlc3Qta2V5In0.eyJzdWIiOiI1MTc4ZmIzYS02MDhmLTRmZjYtOTVjZC1iNzU4MmUwNzIzZjYiLCJzaWQiOiJiYTMxZGM4Ni00NTI5LTQ5MzEtOWZkNC0xMGJlN2ExYWY3NjUiLCJzbG90IjoiMiIsImp0aSI6IjQ0ZjIyNWQ3LTZjMjAtNDk0Mi04NDJjLTFhMGRhMGM2MDQzMyIsIm5iZiI6MTc4OTg0NjA0MCwiZXhwIjoxNzg5ODQ2MDkwLCJpc3MiOiJjdWJlYXJlbmEtYXBpLXRlc3QiLCJhdWQiOiJnYW1lc2VydmVyIn0.hdYNv2Nt6jAmzINfAJUqimzoaNKdeZB1F1xbwKx4hdKyiIHHEA1TmaU1crW1wNX5vW18MaCdEXq1WJHVmgwXwQ";

        private ECPublicKeyParameters _publicKey;

        [SetUp]
        public void SetUp()
        {
            _publicKey = EcPublicKeyFactory.FromRawCoordinates(
                Base64UrlDecode(PublicKeyX), Base64UrlDecode(PublicKeyY));
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

        [Test]
        public void Validate_AcceptsARealBackendSignedTicket()
        {
            var validator = new TicketValidator(_publicKey, Issuer, Audience);

            var result = validator.Validate(ValidTicket, SessionId, new HashSet<string>(), DateTimeOffset.UtcNow);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(UserId, result.UserId);
            Assert.AreEqual(SessionId, result.SessionId);
            Assert.AreEqual(2, result.SlotIndex);
        }

        [Test]
        public void Validate_RejectsTamperedSignature()
        {
            var validator = new TicketValidator(_publicKey, Issuer, Audience);
            var tampered = ValidTicket[..^1] + (ValidTicket[^1] == 'A' ? 'B' : 'A');

            var result = validator.Validate(tampered, SessionId, new HashSet<string>(), DateTimeOffset.UtcNow);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ConnectRejectionReason.InvalidSignature, result.RejectionReason);
        }

        [Test]
        public void Validate_RejectsExpiredTicket()
        {
            var validator = new TicketValidator(_publicKey, Issuer, Audience);

            var result = validator.Validate(ExpiredTicket, SessionId, new HashSet<string>(), DateTimeOffset.UtcNow);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ConnectRejectionReason.Expired, result.RejectionReason);
        }

        [Test]
        public void Validate_RejectsWrongAudience()
        {
            var validator = new TicketValidator(_publicKey, Issuer, "not-a-gameserver");

            var result = validator.Validate(ValidTicket, SessionId, new HashSet<string>(), DateTimeOffset.UtcNow);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ConnectRejectionReason.WrongAudience, result.RejectionReason);
        }

        [Test]
        public void Validate_RejectsWrongIssuer()
        {
            var validator = new TicketValidator(_publicKey, "someone-else", Audience);

            var result = validator.Validate(ValidTicket, SessionId, new HashSet<string>(), DateTimeOffset.UtcNow);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ConnectRejectionReason.WrongIssuer, result.RejectionReason);
        }

        [Test]
        public void Validate_RejectsWrongSession()
        {
            var validator = new TicketValidator(_publicKey, Issuer, Audience);

            var result = validator.Validate(ValidTicket, Guid.NewGuid(), new HashSet<string>(), DateTimeOffset.UtcNow);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ConnectRejectionReason.WrongSession, result.RejectionReason);
        }

        [Test]
        public void Validate_RejectsReplayedJti()
        {
            var validator = new TicketValidator(_publicKey, Issuer, Audience);
            var usedJti = new HashSet<string>();

            var first = validator.Validate(ValidTicket, SessionId, usedJti, DateTimeOffset.UtcNow);
            Assert.IsTrue(first.IsValid);
            usedJti.Add(first.Jti); // exactly what ConnectionApprovalHandler does on approval

            var replay = validator.Validate(ValidTicket, SessionId, usedJti, DateTimeOffset.UtcNow);

            Assert.IsFalse(replay.IsValid);
            Assert.AreEqual(ConnectRejectionReason.ReplayedJti, replay.RejectionReason);
        }

        [Test]
        public void Validate_RejectsMalformedTicket()
        {
            var validator = new TicketValidator(_publicKey, Issuer, Audience);

            var result = validator.Validate("not-a-jwt", SessionId, new HashSet<string>(), DateTimeOffset.UtcNow);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ConnectRejectionReason.InvalidTicketFormat, result.RejectionReason);
        }
    }
}
