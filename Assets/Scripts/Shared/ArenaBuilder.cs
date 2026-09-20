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
            new(4, 1, 12), // was (0,1,12) — sat directly in front of the north house's door at (0,0,13.5), blocking it
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

            // Two houses (walls, roof, an interior jump-staircase to a loft — same
            // step-height convention as BuildClimbableTower), each with an attached crawl
            // tunnel: narrower and lower than the main crouch tunnel, and dark-themed for
            // an "underground" feel, even though it's not literally below y=0 (there's no
            // way to punch a hole in a single flat ground Plane without a custom mesh).
            BuildHouse(root.transform, new Vector3(0f, 0f, 16f), doorFacesSouth: true);
            BuildCrawlTunnel(root.transform, new Vector3(-3f, 0f, 16f), new Vector3(-1f, 0f, 0f), 5f);

            BuildHouse(root.transform, new Vector3(-5f, 0f, -16f), doorFacesSouth: false);
            BuildCrawlTunnel(root.transform, new Vector3(-2f, 0f, -16f), new Vector3(1f, 0f, 0f), 4f);

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

        // A wall split into two segments with a gap between them (a doorway) — gapAlongX
        // true means the gap runs along the wall's local X (an east/west-facing wall with
        // a north/south-walkable opening), false means it runs along Z.
        private static void BuildWallWithGap(Transform parent, string name, Vector3 center,
            float length, float height, float thickness, float gapWidth, bool gapAlongX, Color color)
        {
            var segmentLength = (length - gapWidth) / 2f;
            var offset = gapWidth / 2f + segmentLength / 2f;
            var segmentScale = gapAlongX
                ? new Vector3(segmentLength, height, thickness)
                : new Vector3(thickness, height, segmentLength);
            var offsetVector = gapAlongX ? new Vector3(offset, 0f, 0f) : new Vector3(0f, 0f, offset);

            BuildBlock(parent, name + "_A", center + new Vector3(0, height / 2f, 0) - offsetVector, segmentScale, color);
            BuildBlock(parent, name + "_B", center + new Vector3(0, height / 2f, 0) + offsetVector, segmentScale, color);
        }

        // Two floors: ground level (walk in through the door) and a loft reached by an
        // interior jump-staircase (same 0.75m-per-step convention as BuildClimbableTower —
        // walking up fine stairs would need a much shallower per-step rise than a single
        // Cube-per-step approach can give here, so climbing them plays the same as the
        // tower rather than like real stairs).
        private static void BuildHouse(Transform parent, Vector3 basePosition, bool doorFacesSouth)
        {
            const float half = 2.5f; // 5x5 footprint
            const float groundClearance = 2f;
            const int stairSteps = 3;
            const float stepHeight = 0.75f; // groundClearance / stairSteps, jump-climbable
            const float loftThickness = 0.2f;
            const float upperClearance = 2f;
            const float wallHeight = groundClearance + loftThickness + upperClearance;
            const float wallThickness = 0.3f;
            const float doorWidth = 1.4f;
            const float roofThickness = 0.3f;
            var wallColor = new Color(0.55f, 0.5f, 0.4f);
            var roofColor = new Color(0.35f, 0.25f, 0.2f);

            var doorZ = doorFacesSouth ? -half : half;
            var backZ = doorFacesSouth ? half : -half;

            BuildWallWithGap(parent, doorFacesSouth ? "House_WallSouth" : "House_WallNorth",
                basePosition + new Vector3(0, 0, doorZ), half * 2f, wallHeight, wallThickness, doorWidth, gapAlongX: true, wallColor);
            BuildBlock(parent, doorFacesSouth ? "House_WallNorth" : "House_WallSouth",
                basePosition + new Vector3(0, wallHeight / 2f, backZ), new Vector3(half * 2f, wallHeight, wallThickness), wallColor);
            BuildBlock(parent, "House_WallEast",
                basePosition + new Vector3(half, wallHeight / 2f, 0), new Vector3(wallThickness, wallHeight, half * 2f), wallColor);
            BuildBlock(parent, "House_WallWest",
                basePosition + new Vector3(-half, wallHeight / 2f, 0), new Vector3(wallThickness, wallHeight, half * 2f), wallColor);
            BuildBlock(parent, "House_Roof",
                basePosition + new Vector3(0, wallHeight + roofThickness / 2f, 0), new Vector3(half * 2f + 0.4f, roofThickness, half * 2f + 0.4f), roofColor);

            // Stairs climb from just inside the door toward the back wall.
            var stairZSign = doorFacesSouth ? 1f : -1f;
            for (var i = 0; i < stairSteps; i++)
            {
                var h = stepHeight * (i + 1);
                var pos = basePosition + new Vector3(-half + 1f, h / 2f, stairZSign * (0.6f + i * 0.9f));
                BuildBlock(parent, $"House_Stair{i}", pos, new Vector3(1.4f, h, 0.9f), wallColor);
            }

            var loftY = stepHeight * stairSteps + loftThickness / 2f;
            var loftPos = basePosition + new Vector3(0.5f, loftY, stairZSign * (half - 1.2f));
            BuildBlock(parent, "House_Loft", loftPos, new Vector3(half * 2f - 2f, loftThickness, 2.2f), wallColor);
        }

        // A narrower, lower, dark-themed cousin of BuildCrouchTunnel — tighter (1m wide
        // instead of 3m) and, crucially, low enough that PlayerPose.Crouching (~1.1m,
        // PlayerController.CrouchControllerHeight) does NOT fit under it — only
        // PlayerPose.Crawling (~0.6m, hold C) does. Bound C separately from crouch (Ctrl)
        // for exactly this: a crouch tunnel to walk through, and a crawl tunnel too low
        // for anything but crawling. direction is the tunnel's long axis (need not be
        // axis-aligned); start is the near end, flush against whatever it's attached to
        // (a house wall).
        private static void BuildCrawlTunnel(Transform parent, Vector3 start, Vector3 direction, float length)
        {
            const float halfWidth = 0.5f;
            const float wallHeight = 2.5f;
            const float roofClearance = 0.85f; // < CrouchControllerHeight (1.1) — only crawling clears this
            const float roofThickness = 0.3f;
            var color = new Color(0.15f, 0.15f, 0.18f);

            var forward = direction.normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var center = start + forward * (length / 2f);
            var rotation = Quaternion.LookRotation(forward, Vector3.up);

            BuildRotatedBlock(parent, "Crawl_WallLeft",
                center - right * (halfWidth + 0.15f), rotation, new Vector3(0.3f, wallHeight, length), color);
            BuildRotatedBlock(parent, "Crawl_WallRight",
                center + right * (halfWidth + 0.15f), rotation, new Vector3(0.3f, wallHeight, length), color);
            BuildRotatedBlock(parent, "Crawl_Roof",
                center + Vector3.up * (roofClearance + roofThickness / 2f), rotation, new Vector3(halfWidth * 2f + 0.6f, roofThickness, length), color);
        }

        private static void BuildRotatedBlock(Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 scale, Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent);
            block.transform.SetPositionAndRotation(position, rotation);
            block.transform.localScale = scale;
            MaterialUtil.ApplyLitColor(block.GetComponent<Renderer>(), color);
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
