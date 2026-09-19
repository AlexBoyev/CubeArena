using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CubeArena.Server
{
    // Registers this game server with the backend fleet on boot and heartbeats on an
    // interval (section 4: "registers -> reports players/capacity ... every 10 seconds").
    public class FleetClient
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;

        public Guid GameServerId { get; private set; }
        public Guid SessionId { get; private set; }

        public FleetClient(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
        }

        public async Task RegisterAsync(string host, int port, int capacity)
        {
            var body = JsonConvert.SerializeObject(new { host, port, capacity });
            var json = await PostAsync("/fleet/register", body);

            GameServerId = Guid.Parse((string)json["gameServerId"]);
            SessionId = Guid.Parse((string)json["sessionId"]);

            Debug.Log($"[FleetClient] Registered as game server {GameServerId}, session {SessionId}");
        }

        public async Task HeartbeatAsync(int playerCount)
        {
            var body = JsonConvert.SerializeObject(new { gameServerId = GameServerId, playerCount });
            await PostAsync("/fleet/heartbeat", body, expectJsonResponse: false);
        }

        // Section 6: "rejoin into the same session if a slot is still reserved" — the
        // backend's SessionSlot reservation otherwise expires with the 60s connect ticket,
        // so the server must explicitly extend it for as long as the player stays connected.
        public async Task ConfirmSlotAsync(Guid sessionId, Guid userId)
        {
            var body = JsonConvert.SerializeObject(new { sessionId, userId });
            await PostAsync("/fleet/sessions/confirm", body, expectJsonResponse: false);
        }

        public async Task ReleaseSlotAsync(Guid sessionId, Guid userId)
        {
            var body = JsonConvert.SerializeObject(new { sessionId, userId });
            await PostAsync("/fleet/sessions/release", body, expectJsonResponse: false);
        }

        public async Task RunHeartbeatLoopAsync(TimeSpan interval, Func<int> getPlayerCount, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await HeartbeatAsync(getPlayerCount());
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FleetClient] Heartbeat failed: {e.Message}");
                }

                try
                {
                    await Task.Delay(interval, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<JObject> PostAsync(string path, string jsonBody, bool expectJsonResponse = true)
        {
            using var request = new UnityWebRequest($"{_baseUrl}{path}", "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-Fleet-Api-Key", _apiKey);

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException(
                    $"POST {path} failed: {request.result} ({request.responseCode}) {request.downloadHandler?.text}");
            }

            return expectJsonResponse ? JObject.Parse(request.downloadHandler.text) : null;
        }
    }
}
