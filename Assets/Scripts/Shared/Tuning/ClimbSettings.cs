using UnityEngine;

namespace CubeArena.Shared.Tuning
{
    // Per AUTONOMOUS_RUN.md section 2: new tunables go in ScriptableObjects (not plain
    // consts like MovementConstants) so values can be retuned after playtesting without
    // a code change. Loaded via Resources.Load at runtime — see the asset at
    // Assets/Resources/ClimbSettings.asset (created via
    // Assets/Editor/PocketHeist/ClimbSettingsAssetCreator.cs, since CLAUDE.md forbids
    // hand-authoring asset files directly).
    [CreateAssetMenu(fileName = "ClimbSettings", menuName = "CubeArena/Climb Settings")]
    public class ClimbSettings : ScriptableObject
    {
        [Tooltip("Vertical speed while climbing a Climbable surface, meters/second.")]
        public float ClimbSpeed = 3f;

        [Tooltip("How far in front of the player (along their facing) a Climbable surface " +
                 "is detected from, meters.")]
        public float DetectionRange = 1.2f;

        [Tooltip("Horizontal speed allowed while climbing, for shifting between adjacent " +
                 "footholds (e.g. chair rung to chair rung) — much slower than normal " +
                 "ground movement since it's a small adjustment, not travel.")]
        public float HorizontalShiftSpeed = 1.5f;
    }
}
