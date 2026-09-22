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
                 "(dragged along the ground, not lifted), meters/second.")]
        public float DragSpeed = 1.2f;

        [Tooltip("Speed a loot item moves at while gripped by at least its requiredCarriers " +
                 "(lifted and carried), meters/second.")]
        public float CarrySpeed = 3.5f;

        [Tooltip("Height above the floor a loot item is lifted to while being carried " +
                 "(at/above requiredCarriers), meters.")]
        public float CarryHeight = 1.2f;

        [Tooltip("Horizontal distance from the mousehole within which a loot item banks " +
                 "(despawns, credits its value to the team total), meters.")]
        public float BankRadius = 2.5f;
    }
}
