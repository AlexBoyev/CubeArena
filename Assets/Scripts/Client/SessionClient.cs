using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace CubeArena.Client
{
    public struct QuickplayResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Ticket { get; set; }
        public int SlotIndex { get; set; }
    }

    public class SessionClient
    {
        private readonly string _baseUrl;

        public SessionClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<QuickplayResult> QuickplayAsync(string accessToken)
        {
            using var request = new UnityWebRequest($"{_baseUrl}/sessions/quickplay", "POST");
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error;
                try
                {
                    error = (string)JObject.Parse(request.downloadHandler.text)["error"];
                }
                catch (Exception)
                {
                    error = request.error;
                }

                return new QuickplayResult { Success = false, Error = error ?? request.error };
            }

            var json = JObject.Parse(request.downloadHandler.text);
            return new QuickplayResult
            {
                Success = true,
                Host = (string)json["host"],
                Port = (int)json["port"],
                Ticket = (string)json["ticket"],
                SlotIndex = (int)json["slotIndex"]
            };
        }
    }
}
