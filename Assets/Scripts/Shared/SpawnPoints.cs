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
            // Not corners like the original 4 — those are already claimed, and the
            // obvious remaining mid-edge spots (e.g. (0,0,±15)) land inside House1's
            // footprint or too close to the jump gap/balance beam/tower. These two are
            // verified clear of every ArenaBuilder obstacle/course piece.
            new(17, 0, 10),
            new(-17, 0, -10),
        };

        public static Vector3 Get(int slotIndex) => Points[Mathf.Clamp(slotIndex, 0, Points.Length - 1)];
    }
}
