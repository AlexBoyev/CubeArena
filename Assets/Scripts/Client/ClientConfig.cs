using System;

namespace CubeArena.Client
{
    public class ClientConfig
    {
        public string BackendUrl { get; private set; }

        // Optional: bypasses the login/character-select UI for scripted multi-client
        // testing (e.g. launching four instances from a script). Leave unset for normal
        // interactive play.
        public string AutoTestEmail { get; private set; }
        public string AutoTestPassword { get; private set; }

        public bool AutoTestEnabled => !string.IsNullOrEmpty(AutoTestEmail);

        public static ClientConfig FromEnvironment()
        {
            return new ClientConfig
            {
                BackendUrl = GetEnv("CUBEARENA_BACKEND_URL", "http://localhost:8080"),
                AutoTestEmail = GetEnv("CUBEARENA_AUTOTEST_EMAIL", ""),
                AutoTestPassword = GetEnv("CUBEARENA_AUTOTEST_PASSWORD", "")
            };
        }

        private static string GetEnv(string name, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
