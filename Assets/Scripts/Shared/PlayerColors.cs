using UnityEngine;

namespace CubeArena.Shared
{
    // Six fixed slots, assigned by the server from the connect ticket's slot claim
    // (section 6: "The server owns the assignment; the client only renders it.").
    public static class PlayerColors
    {
        private static readonly Color[] Colors =
        {
            Color.red,
            new(0.15f, 0.4f, 1f), // blue
            new(0.15f, 0.85f, 0.2f), // green
            Color.yellow,
            new(0.6f, 0.25f, 0.85f), // purple
            new(1f, 0.55f, 0.1f), // orange
        };

        // Matches Colors index-for-index — used as the default display name (instead of
        // the much less legible "Slot 0") for anyone who leaves the name field blank.
        private static readonly string[] Names = { "Red", "Blue", "Green", "Yellow", "Purple", "Orange" };

        public static Color Get(int slotIndex) =>
            Colors[Mathf.Clamp(slotIndex, 0, Colors.Length - 1)];

        public static string GetName(int slotIndex) =>
            Names[Mathf.Clamp(slotIndex, 0, Names.Length - 1)];
    }
}
