using UnityEngine;

namespace CubeArena.Shared.Tuning
{
    // Per AUTONOMOUS_RUN.md section 2: new tunables go in ScriptableObjects (not plain
    // consts) so values can be retuned after playtesting without a code change. Loaded
    // via Resources.Load at runtime — see the asset at Assets/Resources/LootSettings.asset
    // (created via Assets/Editor/PocketHeist/LootSettingsAssetCreator.cs).
    [CreateAssetMenu(fileName = "LootSettings", menuName = "CubeArena/Loot Settings")]
    public class LootSettings : ScriptableObject
    {
        [Tooltip("How far in front of a player (roughly, within a forward-facing dot-product " +
                 "gate) a loot item can be gripped from, meters.")]
        public float GripRange = 2.5f;

        [Tooltip("Speed a loot item moves at while gripped by fewer than its requiredCarriers " +
                 "(dragged along whatever surface the grippers are on, not lifted), meters/second.")]
        public float DragSpeed = 1.2f;

        [Tooltip("Speed a loot item moves at while gripped by at least its requiredCarriers " +
                 "(lifted and carried), meters/second.")]
        public float CarrySpeed = 3.5f;

        [Tooltip("Height above the *grippers' own current Y* a loot item is held at while " +
                 "carried (at/above requiredCarriers), meters. Tracking the grippers' actual " +
                 "height (not a fixed absolute height) is what makes carrying loot down the " +
                 "chair or the tablecloth climb route work automatically as the carriers " +
                 "physically climb — see LootItem.FixedUpdate and docs/DECISIONS.md.")]
        public float CarryHeightOffset = 1.2f;

        [Tooltip("Height above the grippers' own current Y a loot item is held at while " +
                 "under-staffed (dragged, below requiredCarriers) — near ground level " +
                 "relative to them, not lifted.")]
        public float DragHeightOffset = 0.1f;

        [Tooltip("Horizontal distance from the mousehole within which a loot item banks " +
                 "(despawns, credits its value to the team total), meters.")]
        public float BankRadius = 2.5f;

        [Tooltip("Horizontal speed applied to a loot item when shoved off the table edge " +
                 "(Milestone 4's instant descent method), meters/second. A small upward " +
                 "component is added on top so it visibly leaves the edge before gravity " +
                 "takes over — see LootItem.ServerShove.")]
        public float ShoveSpeed = 6f;
    }
}
