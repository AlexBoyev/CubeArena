using UnityEngine;

namespace CubeArena.Shared
{
    // One fixed spawn point per slot, clear of ArenaBuilder's obstacles.
    public static class SpawnPoints
    {
        private static readonly Vector3[] Points =
        {
            new(15, 0, 15),
            new(-15, 0, 15),
            new(15, 0, -15),
            new(-15, 0, -15),
        };

        public static Vector3 Get(int slotIndex) => Points[Mathf.Clamp(slotIndex, 0, Points.Length - 1)];
    }
}
