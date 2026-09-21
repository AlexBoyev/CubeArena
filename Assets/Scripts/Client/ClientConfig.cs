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

        // Drives PlayerController.RunBotBehavior once connected instead of reading real
        // input — see its declaration. Only meaningful alongside AutoTest* (bot mode still
        // needs a way to log in and connect without the interactive UI).
        public bool BotModeEnabled { get; private set; }

        // Bot mode only: how many connected players the host bot waits for before
        // auto-starting the match (there's no one to click Start Match otherwise). 1 by
        // default so a lone bot "just works"; the load test sets this to 6 explicitly.
        public int BotAutoStartCount { get; private set; }

        public static ClientConfig FromEnvironment()
        {
            return new ClientConfig
            {
                BackendUrl = GetEnv("CUBEARENA_BACKEND_URL", "http://localhost:8080"),
                AutoTestEmail = GetEnv("CUBEARENA_AUTOTEST_EMAIL", ""),
                AutoTestPassword = GetEnv("CUBEARENA_AUTOTEST_PASSWORD", ""),
                BotModeEnabled = GetEnv("CUBEARENA_BOT_MODE", "") == "1",
                BotAutoStartCount = int.Parse(GetEnv("CUBEARENA_BOT_AUTOSTART_COUNT", "1"))
            };
        }

        private static string GetEnv(string name, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
