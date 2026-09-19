using UnityEngine;

namespace CubeArena.Shared
{
    // Four fixed slots, assigned by the server from the connect ticket's slot claim
    // (section 6: "The server owns the assignment; the client only renders it.").
    public static class PlayerColors
    {
        private static readonly Color[] Colors =
        {
            Color.red,
            new(0.15f, 0.4f, 1f), // blue
            new(0.15f, 0.85f, 0.2f), // green
            Color.yellow
        };

        public static Color Get(int slotIndex) =>
            Colors[Mathf.Clamp(slotIndex, 0, Colors.Length - 1)];
    }
}
