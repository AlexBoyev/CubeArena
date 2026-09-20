namespace CubeArena.Shared
{
    // Shared by client prediction and server simulation so they compute identical
    // motion from the same input — see docs/NETCODE.md.
    public static class MovementConstants
    {
        public const float MoveSpeed = 5f; // meters/second
        public const float JumpSpeed = 6f; // initial upward velocity, meters/second
        public const float Gravity = -20f; // meters/second^2
        public const float ServerTickRate = 30f; // Hz, per the brief's section 6
        public const float ArenaHalfExtent = 20f; // 40x40 arena
    }
}
