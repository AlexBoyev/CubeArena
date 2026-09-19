using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Parameters;
using UnityEngine;
using UnityEngine.Networking;

namespace CubeArena.Server
{
    // Fetches the backend's public ticket-signing key once at boot (section 3.2: "the
    // game server holds only the public key, fetched from a JWKS endpoint at boot and
    // cached"). No secret ever reaches this class.
    public static class JwksClient
    {
        public static async Task<ECPublicKeyParameters> FetchPublicKeyAsync(string jwksUrl, string expectedKeyId)
        {
            using var request = UnityWebRequest.Get(jwksUrl);
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException($"Failed to fetch JWKS from {jwksUrl}: {request.error}");
            }

            var jwks = JObject.Parse(request.downloadHandler.text);
            var keys = (JArray)jwks["keys"];
            if (keys == null || keys.Count == 0)
            {
                throw new InvalidOperationException("JWKS response contained no keys.");
            }

            JObject key = null;
            foreach (var candidate in keys)
            {
                if ((string)candidate["kid"] == expectedKeyId)
                {
                    key = (JObject)candidate;
                    break;
                }
            }
            key ??= (JObject)keys[0];

            if ((string)key["kty"] != "EC" || (string)key["crv"] != "P-256")
            {
                throw new InvalidOperationException("Unsupported JWKS key type; expected EC P-256.");
            }

            var x = Base64UrlDecode((string)key["x"]);
            var y = Base64UrlDecode((string)key["y"]);
            var publicKey = EcPublicKeyFactory.FromRawCoordinates(x, y);

            Debug.Log($"[JwksClient] Loaded public ticket-signing key '{(string)key["kid"]}' from {jwksUrl}");
            return publicKey;
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
