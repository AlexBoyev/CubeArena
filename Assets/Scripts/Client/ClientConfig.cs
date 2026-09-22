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

        // Optional: if set, ClientBootstrap.Update captures one ScreenCapture.
        // CaptureScreenshot to this path after ScreenshotDelaySeconds, then leaves it
        // there — a reusable, opt-in way to get real Play-mode/standalone-client visual
        // verification (per AUTONOMOUS_RUN.md's "a mechanic isn't done because it
        // compiles" rule) from a live networked client, not just the Editor-only
        // preview-scene screenshot path SleepPreviewScreenshotter uses. Empty (default)
        // means no screenshot is taken — this is a no-op for normal play.
        public string ScreenshotPath { get; private set; }
        public float ScreenshotDelaySeconds { get; private set; }

        // Bot mode only: which scripted routine PlayerController.RunBotBehavior runs.
        // "" (default) is the Milestone 3 coin-test routine (walk to nearest visible loot
        // by straight-line distance, grip, carry to the mousehole — in practice this can
        // still land on a table-top item for bots spawned close enough to the table, but
        // isn't a reliable way to target one). "lootdescent" and "banktest" are Milestone
        // 4's dedicated verification routines (climb to the table, grip a table-top item,
        // then either shove it off the edge or carry it back down and bank it) — see
        // PlayerController.RunLootDescentTestBotBehavior.
        public string BotTestMode { get; private set; }

        public static ClientConfig FromEnvironment()
        {
            return new ClientConfig
            {
                BackendUrl = GetEnv("CUBEARENA_BACKEND_URL", "http://localhost:8080"),
                AutoTestEmail = GetEnv("CUBEARENA_AUTOTEST_EMAIL", ""),
                AutoTestPassword = GetEnv("CUBEARENA_AUTOTEST_PASSWORD", ""),
                BotModeEnabled = GetEnv("CUBEARENA_BOT_MODE", "") == "1",
                BotAutoStartCount = int.Parse(GetEnv("CUBEARENA_BOT_AUTOSTART_COUNT", "1")),
                ScreenshotPath = GetEnv("CUBEARENA_SCREENSHOT_PATH", ""),
                ScreenshotDelaySeconds = float.Parse(GetEnv("CUBEARENA_SCREENSHOT_DELAY", "5"),
                    System.Globalization.CultureInfo.InvariantCulture),
                BotTestMode = GetEnv("CUBEARENA_BOT_TEST_MODE", "")
            };
        }

        private static string GetEnv(string name, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
