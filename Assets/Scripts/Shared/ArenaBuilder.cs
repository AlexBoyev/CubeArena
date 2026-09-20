using UnityEngine;

namespace CubeArena.Shared
{
    // Builds the arena purely from code (no scene/prefab assets — see CLAUDE.md's hard
    // rule) with a fixed, hardcoded layout so every client and the server independently
    // produce byte-identical geometry without any network sync.
    public static class ArenaBuilder
    {
        private static readonly Vector3[] ObstaclePositions =
        {
            new(8, 1, 8),
            new(-8, 1, 8),
            new(8, 1, -8),
            new(-8, 1, -8),
            new(0, 1, 12),
            new(0, 1, -12),
            new(12, 1, 0),
            new(-12, 1, 0),
        };

        public static GameObject Build()
        {
            var root = new GameObject("Arena");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(
                MovementConstants.ArenaHalfExtent / 5f, 1f, MovementConstants.ArenaHalfExtent / 5f);
            MaterialUtil.ApplyLitColor(ground.GetComponent<Renderer>(), new Color(0.3f, 0.32f, 0.36f));

            const float wallHeight = 3f;
            const float wallThickness = 1f;
            var extent = MovementConstants.ArenaHalfExtent;

            BuildWall(root.transform, "Wall_North", new Vector3(0, wallHeight / 2f, extent), new Vector3(extent * 2 + wallThickness, wallHeight, wallThickness));
            BuildWall(root.transform, "Wall_South", new Vector3(0, wallHeight / 2f, -extent), new Vector3(extent * 2 + wallThickness, wallHeight, wallThickness));
            BuildWall(root.transform, "Wall_East", new Vector3(extent, wallHeight / 2f, 0), new Vector3(wallThickness, wallHeight, extent * 2 + wallThickness));
            BuildWall(root.transform, "Wall_West", new Vector3(-extent, wallHeight / 2f, 0), new Vector3(wallThickness, wallHeight, extent * 2 + wallThickness));

            for (var i = 0; i < ObstaclePositions.Length; i++)
            {
                var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.name = $"Obstacle_{i}";
                obstacle.transform.SetParent(root.transform);
                obstacle.transform.position = ObstaclePositions[i];
                obstacle.transform.localScale = new Vector3(2f, 2f, 2f);
                MaterialUtil.ApplyLitColor(obstacle.GetComponent<Renderer>(), new Color(0.55f, 0.4f, 0.25f));
            }

            return root;
        }

        private static void BuildWall(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent);
            wall.transform.position = position;
            wall.transform.localScale = scale;
            MaterialUtil.ApplyLitColor(wall.GetComponent<Renderer>(), new Color(0.2f, 0.2f, 0.24f));
        }
    }
}
