namespace CubeArena.Shared
{
    // Single source of truth for which scene actually runs the game. Real builds
    // only ever include this one scene (see Editor/BuildScript.cs's
    // BuildPlayerOptions.scenes), so ServerBootstrap/ClientBootstrap's
    // auto-bootstrap checking against this name is a no-op there - it only
    // matters in the Editor, where opening any *other* scene (a preview/test
    // scene, a future level greybox) must play normally instead of trying to
    // boot the whole networked game on top of it.
    public static class BootConfig
    {
        public const string BootSceneName = "SampleScene";
    }
}
