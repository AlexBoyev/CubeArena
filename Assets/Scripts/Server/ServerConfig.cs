using System;

namespace CubeArena.Server
{
    // Configuration entirely via environment variables, per the brief's DevOps section.
    public class ServerConfig
    {
        public string BackendUrl { get; private set; }
        public string FleetApiKey { get; private set; }
        public string AdvertiseHost { get; private set; }
        public ushort ListenPort { get; private set; }
        public int Capacity { get; private set; }
        public string TicketIssuer { get; private set; }
        public string TicketAudience { get; private set; }
        public string TicketKeyId { get; private set; }

        // Small default for normal play; set to 30 for the physics-bandwidth load test
        // (see docs/NETCODE.md) without needing a rebuild.
        public int CrateCount { get; private set; }

        public static ServerConfig FromEnvironment()
        {
            return new ServerConfig
            {
                BackendUrl = GetEnv("CUBEARENA_BACKEND_URL", "http://localhost:8080"),
                FleetApiKey = GetEnv("CUBEARENA_FLEET_API_KEY", ""),
                AdvertiseHost = GetEnv("CUBEARENA_ADVERTISE_HOST", "127.0.0.1"),
                ListenPort = ushort.Parse(GetEnv("CUBEARENA_LISTEN_PORT", "7777")),
                Capacity = int.Parse(GetEnv("CUBEARENA_CAPACITY", "6")),
                TicketIssuer = GetEnv("CUBEARENA_TICKET_ISSUER", "cubearena-api"),
                TicketAudience = GetEnv("CUBEARENA_TICKET_AUDIENCE", "gameserver"),
                TicketKeyId = GetEnv("CUBEARENA_TICKET_KEY_ID", "cubearena-ticket-key-1"),
                CrateCount = int.Parse(GetEnv("CUBEARENA_CRATE_COUNT", "8"))
            };
        }

        private static string GetEnv(string name, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
