using UnityEngine;

namespace CubeArena.Shared.Tuning
{
    // Milestone 2 "thief prefab with animations": blend/threshold tunables for
    // PlayerController.UpdateLocomotionAnimation's locomotion-state selection, so
    // timings can be tuned after playtesting without a code change - same rationale
    // as ClimbSettings.
    [CreateAssetMenu(fileName = "ThiefAnimationSettings", menuName = "PocketHeist/Thief Animation Settings")]
    public class ThiefAnimationSettings : ScriptableObject
    {
        [Tooltip("Crossfade duration (seconds) between locomotion animator states.")]
        public float CrossfadeDuration = 0.15f;

        [Tooltip("Vertical speed (m/s), inferred from frame-to-frame position delta, " +
                 "above which the player is considered airborne (jumping/falling).")]
        public float AirborneVerticalThreshold = 1.5f;

        [Tooltip("Seconds after leaving the ground to hold Jump_Start before switching to Jump_Loop.")]
        public float JumpStartDuration = 0.25f;

        [Tooltip("Seconds to hold Jump_Land after touching back down before returning to idle/walk.")]
        public float JumpLandDuration = 0.2f;
    }
}
