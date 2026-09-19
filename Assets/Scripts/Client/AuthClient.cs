using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace CubeArena.Client
{
    public class AuthClient
    {
        private readonly string _baseUrl;

        public string AccessToken { get; private set; }
        public string RefreshToken { get; private set; }

        public AuthClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<(bool Success, string Error)> RegisterAsync(string email, string password)
        {
            var body = JsonConvert.SerializeObject(new { email, password });
            using var request = BuildPost("/auth/register", body);
            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
            {
                return (true, null);
            }

            return (false, ExtractError(request));
        }

        public async Task<(bool Success, string Error)> LoginAsync(string email, string password)
        {
            var body = JsonConvert.SerializeObject(new { email, password });
            using var request = BuildPost("/auth/login", body);
            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                return (false, ExtractError(request));
            }

            var json = JObject.Parse(request.downloadHandler.text);
            AccessToken = (string)json["accessToken"];
            RefreshToken = (string)json["refreshToken"];
            return (true, null);
        }

        private UnityWebRequest BuildPost(string path, string jsonBody)
        {
            var request = new UnityWebRequest($"{_baseUrl}{path}", "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        private static string ExtractError(UnityWebRequest request)
        {
            try
            {
                var json = JObject.Parse(request.downloadHandler.text);
                return (string)json["error"] ?? request.error;
            }
            catch (Exception)
            {
                return request.error;
            }
        }
    }
}
