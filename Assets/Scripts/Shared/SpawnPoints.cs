using UnityEngine;

namespace CubeArena.Shared
{
    // One fixed spawn point per slot, all near KitchenBuilder.MouseholePosition — per
    // docs/GAME_DESIGN.md section 3, the mousehole is the spawn *and* delivery point,
    // not a scattered corner-per-slot layout like Cube Arena's old ArenaBuilder used.
    // Offset along Z (into the room, away from the wall) and spread slightly along X so
    // six players spawning at once don't stack exactly on top of each other.
    public static class SpawnPoints
    {
        private static readonly Vector3[] Points = BuildPoints();

        private static Vector3[] BuildPoints()
        {
            var mousehole = KitchenBuilder.MouseholePosition;
            var points = new Vector3[6];
            for (var i = 0; i < points.Length; i++)
            {
                var row = i / 3;
                var col = i % 3;
                points[i] = mousehole + new Vector3(1.5f + row * 1.5f, 0f, -3f + col * 3f);
            }

            return points;
        }

        public static Vector3 Get(int slotIndex) => Points[Mathf.Clamp(slotIndex, 0, Points.Length - 1)];
    }
}
