namespace CubeArena.Shared
{
    // Shared by client prediction and server simulation so they compute identical
    // motion from the same input — see docs/NETCODE.md.
    public static class MovementConstants
    {
        public const float MoveSpeed = 5f; // meters/second
        public const float CrouchSpeedMultiplier = 0.5f;
        public const float CrawlSpeedMultiplier = 0.3f;
        public const float SprintSpeedMultiplier = 1.6f;
        // Stamina is the resource sprint actually spends — kept named for the resource,
        // not the action, since "Mana" is a separate (currently inert, for future
        // ability/spell work) resource on PlayerController.
        public const float StaminaMax = 100f;
        public const float StaminaDrainPerSecond = 35f;
        public const float StaminaRegenPerSecond = 18f;
        // Once stamina hits 0, sprint is locked out until it recovers back up to this
        // fraction — without this, stamina hovering near 0 (drain and regen fighting each
        // other tick to tick right at the boundary) let you keep sprinting in a stuttery
        // near-infinite loop instead of actually running out.
        public const float StaminaResumeFraction = 0.3f;
        public const float JumpSpeed = 8f; // initial upward velocity, meters/second
        public const float Gravity = -28f; // meters/second^2 — steep on purpose, for a snappy jump arc rather than a floaty one
        public const float ServerTickRate = 30f; // Hz, per the brief's section 6
        public const float ArenaHalfExtent = 20f; // 40x40 arena
    }
}
