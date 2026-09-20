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

            // Course pieces that actually require the crouch/jump mechanics rather than
            // just blocking a path — placed in the open ring between the fixed obstacles
            // above and the walls, clear of both spawn points and each other.
            BuildCrouchTunnel(root.transform, new Vector3(0f, 0f, 0f));
            BuildClimbableTower(root.transform, new Vector3(6f, 0f, -16f));
            BuildJumpGap(root.transform, new Vector3(-16f, 0f, 6f));
            BuildBalanceBeam(root.transform, new Vector3(17f, 0f, -3f));

            return root;
        }

        private static void BuildWall(Transform parent, string name, Vector3 position, Vector3 scale) =>
            BuildBlock(parent, name, position, scale, new Color(0.2f, 0.2f, 0.24f));

        private static void BuildBlock(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent);
            block.transform.position = position;
            block.transform.localScale = scale;
            MaterialUtil.ApplyLitColor(block.GetComponent<Renderer>(), color);
        }

        // A low roof over a short corridor: clears a crouching player
        // (PlayerController.CrouchControllerHeight, ~1.1m) but blocks a standing one
        // (StandingControllerHeight, ~1.9m) — walking through means crouching through.
        private static void BuildCrouchTunnel(Transform parent, Vector3 center)
        {
            const float halfWidth = 1.5f;
            const float halfLength = 3f;
            const float wallThickness = 0.5f;
            const float wallHeight = 3f;
            const float roofClearance = 1.35f;
            const float roofThickness = 0.4f;
            var color = new Color(0.35f, 0.3f, 0.3f);

            BuildBlock(parent, "Tunnel_WallLeft",
                center + new Vector3(-halfWidth - wallThickness / 2f, wallHeight / 2f, 0f),
                new Vector3(wallThickness, wallHeight, halfLength * 2f), color);
            BuildBlock(parent, "Tunnel_WallRight",
                center + new Vector3(halfWidth + wallThickness / 2f, wallHeight / 2f, 0f),
                new Vector3(wallThickness, wallHeight, halfLength * 2f), color);
            BuildBlock(parent, "Tunnel_Roof",
                center + new Vector3(0f, roofClearance + roofThickness / 2f, 0f),
                new Vector3(halfWidth * 2f + wallThickness * 2f, roofThickness, halfLength * 2f), color);
        }

        // Four ascending steps (~0.75m each — comfortably under a jump's ~1.1m apex),
        // forming a climbable "building": jump from the ground onto step 0, then step to
        // step up to a flat rooftop at the top, about 3m up.
        private static void BuildClimbableTower(Transform parent, Vector3 basePosition)
        {
            const int stepCount = 4;
            const float stepHeight = 0.75f;
            const float stepDepth = 2.2f;
            const float stepWidth = 4f;
            const float roofDepth = 3f;
            var color = new Color(0.45f, 0.45f, 0.5f);

            for (var i = 0; i < stepCount; i++)
            {
                var height = stepHeight * (i + 1);
                var position = basePosition + new Vector3(0f, height / 2f, i * stepDepth);
                BuildBlock(parent, $"Tower_Step{i}", position, new Vector3(stepWidth, height, stepDepth), color);
            }

            var roofHeight = stepHeight * stepCount;
            var lastStepFarEdgeOffset = (stepCount - 1) * stepDepth + stepDepth / 2f;
            var roofZOffset = lastStepFarEdgeOffset + roofDepth / 2f + 0.3f;
            var roofPosition = basePosition + new Vector3(0f, roofHeight / 2f, roofZOffset);
            BuildBlock(parent, "Tower_Roof", roofPosition, new Vector3(stepWidth, roofHeight, roofDepth), color);
        }

        // A narrow (1.2m — about the width of a player) elevated walkway between two
        // short support blocks. Nothing about it needs crouch or jump, just careful
        // walking: the CharacterController's own radius (0.35m) leaves little margin to
        // drift off the edge, unlike every other piece here which is wide enough to not
        // need any care.
        private static void BuildBalanceBeam(Transform parent, Vector3 basePosition)
        {
            const float supportHeight = 0.9f;
            const float beamWidth = 1.2f;
            const float beamLength = 12f;
            const float beamThickness = 0.3f;
            var color = new Color(0.5f, 0.35f, 0.55f);

            BuildBlock(parent, "Beam_SupportA",
                basePosition + new Vector3(0f, supportHeight / 2f, -beamLength / 2f + 0.5f),
                new Vector3(beamWidth, supportHeight, 1f), color);
            BuildBlock(parent, "Beam_SupportB",
                basePosition + new Vector3(0f, supportHeight / 2f, beamLength / 2f - 0.5f),
                new Vector3(beamWidth, supportHeight, 1f), color);
            BuildBlock(parent, "Beam_Walkway",
                basePosition + new Vector3(0f, supportHeight + beamThickness / 2f, 0f),
                new Vector3(beamWidth, beamThickness, beamLength), color);
        }

        // Two low platforms with a gap wide enough that only a jump clears it, not just
        // walking.
        private static void BuildJumpGap(Transform parent, Vector3 basePosition)
        {
            const float platformHeight = 0.6f;
            const float platformSize = 3f;
            const float gap = 2.5f;
            var color = new Color(0.3f, 0.45f, 0.4f);

            BuildBlock(parent, "JumpGap_PlatformA",
                basePosition + new Vector3(0f, platformHeight / 2f, 0f),
                new Vector3(platformSize, platformHeight, platformSize), color);
            BuildBlock(parent, "JumpGap_PlatformB",
                basePosition + new Vector3(platformSize + gap, platformHeight / 2f, 0f),
                new Vector3(platformSize, platformHeight, platformSize), color);
        }
    }
}
