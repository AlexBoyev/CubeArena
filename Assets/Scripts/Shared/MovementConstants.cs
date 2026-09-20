namespace CubeArena.Shared
{
    // Shared by client prediction and server simulation so they compute identical
    // motion from the same input — see docs/NETCODE.md.
    public static class MovementConstants
    {
        public const float MoveSpeed = 5f; // meters/second
        public const float CrouchSpeedMultiplier = 0.5f;
        public const float CrawlSpeedMultiplier = 0.3f;
        public const float JumpSpeed = 8f; // initial upward velocity, meters/second
        public const float Gravity = -28f; // meters/second^2 — steep on purpose, for a snappy jump arc rather than a floaty one
        public const float ServerTickRate = 30f; // Hz, per the brief's section 6
        public const float ArenaHalfExtent = 20f; // 40x40 arena
    }
}
