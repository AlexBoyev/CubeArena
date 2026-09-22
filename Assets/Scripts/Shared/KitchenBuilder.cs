using UnityEngine;

namespace CubeArena.Shared
{
    // Builds "The Midnight Snacker" kitchen (docs/GAME_DESIGN.md section 3) purely from
    // code, same convention as the Cube Arena ArenaBuilder it replaces (CLAUDE.md: no
    // hand-authored scene/prefab assets, though imported assets/prefabs referenced from
    // code are fine per the pivot's rule change) — every client and the server
    // independently produce byte-identical geometry with no network sync needed.
    //
    // Milestone 4: layers real KayKit/Kenney visuals (Assets/Resources/Kitchen/*, built by
    // Assets/Editor/PocketHeist/KitchenAssetPrefabBuilder.cs) on top of the *unchanged*
    // Milestone 2 collision/climb geometry rather than deriving new collision from the
    // real meshes' own bounds — re-deriving exact seat/climb heights from an unlabelled,
    // single-mesh chair model risked silently breaking the already-verified chair climb
    // route and Milestone 3's loot banking position for a purely cosmetic gain. See
    // docs/DECISIONS.md. All gameplay-critical dimensions (TableTopHeight, ChairSeatHeight,
    // MouseholePosition, TableCenter) are exactly what Milestones 2/3 already tested.
    public static class KitchenBuilder
    {
        // World-scale numbers, x25, from docs/GAME_DESIGN.md section 2. Unchanged from
        // Milestone 2/3 — still the authoritative gameplay anchors.
        public const float FloorWidth = 100f; // x (400cm)
        public const float FloorDepth = 87.5f; // z (350cm)
        public const float CeilingHeight = 65f;
        public const float TableTopHeight = 18.75f;
        public const float ChairSeatHeight = 11.25f;
        public const float CounterHeight = 22.5f;
        public const float GiantScale = 25f;

        // Real kitchentable_A_large is a single mesh, base grounded at its own pivot, top
        // 1.004 local units up (probed via a one-off Editor script, not re-checked at
        // runtime) — scaling it (10, TableTopHeight/1.004, 10) lands its visible top
        // exactly at TableTopHeight while keeping a reasonable footprint (its raw
        // footprint is 3x2, so x10 gives 30x20, matching the old greybox's own
        // footprint almost exactly — a happy coincidence, not tuned for it).
        private const float TableVisualScaleXZ = 10f;
        private static readonly float TableVisualScaleY = TableTopHeight / 1.004f;
        public const float TableWidth = 3f * TableVisualScaleXZ; // 30 — same as Milestone 2's greybox value
        public const float TableDepth = 2f * TableVisualScaleXZ; // 20 — same as Milestone 2's greybox value

        // Table centered toward the room's north-east so there's a clear run from the
        // south-west mousehole across the rug to reach it — matches "Rug: between
        // mousehole and table" (section 3's area table).
        // Public: PlayerController's bot-mode climb test targets a point on the table
        // top computed from this.
        public static readonly Vector3 TableCenter = new(10f, 0f,15f);
        // Public: SpawnPoints places all 6 slots near here, per section 3's "Mousehole:
        // spawn and delivery point" — not scattered around the room like Cube Arena's
        // corner-per-slot convention.
        public static readonly Vector3 MouseholePosition = new(-FloorWidth / 2f + 2f, 0f, -FloorDepth / 2f + 10f);

        // Milestone 3's "the coin test": the floor coin's spawn spot, per section 6's
        // loot table ("Floor under table"). Unchanged from Milestone 3.
        public static readonly Vector3 LootCoinSpawnPosition = TableCenter + new Vector3(5f, 0f, -5f);

        // Milestone 4: the rest of the table-top loot set (section 6's loot table), all at
        // TableTopHeight so a gripped item starts already resting on the table surface —
        // see LootItem.FixedUpdate's gripper-relative height tracking (docs/DECISIONS.md)
        // for why this no longer needs a special "lift it up from the floor first" step.
        public static Vector3 TableTopPosition(float dx, float dz) => TableCenter + new Vector3(dx, TableTopHeight, dz);
        public static readonly Vector3 WalletCoin1SpawnPosition = TableTopPosition(-10f, -6f);
        public static readonly Vector3 WalletCoin2SpawnPosition = TableTopPosition(-8f, -6f);
        public static readonly Vector3 WalletCoin3SpawnPosition = TableTopPosition(-6f, -6f);
        public static readonly Vector3 RingSpawnPosition = TableTopPosition(6f, TableDepth / 2f - 3f); // "beside the giant's hand" — giant sits at the table's south (+z) side
        public static readonly Vector3 WristwatchSpawnPosition = TableTopPosition(TableWidth / 2f - 6f, TableDepth / 2f - 6f); // "far corner"

        // Milestone 4's new "lower it down the tablecloth" descent method: a second
        // Climbable zone (reuses the exact same climbing mechanic from Milestone 2 — see
        // PlayerController.IsNearClimbable/ComputeClimbMove, which are gated purely by
        // proximity to any Climbable collider, not which one) at the table's east edge,
        // opposite the chair. Spans floor-to-table-top directly rather than literally
        // stopping "~5m above the floor" per the master prompt's flavour text — see
        // docs/DECISIONS.md for why the full-height version was chosen.
        private static readonly Vector3 TablecloudClimbPosition = TableCenter + new Vector3(TableWidth / 2f + 0.4f, TableTopHeight / 2f, TableDepth / 2f - 3f);

        public static GameObject Build()
        {
            var root = new GameObject("Kitchen");

            BuildFloor(root.transform);
            BuildWalls(root.transform);
            BuildRugRoute(root.transform);
            var table = BuildTable(root.transform);
            BuildChairClimbRoute(root.transform, table);
            BuildTablecloudClimbRoute(root.transform);
            BuildFridge(root.transform);
            BuildCounter(root.transform);
            BuildGiantPlaceholder(root.transform);

            return root;
        }

        private static void BuildFloor(Transform parent)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor_Tile";
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(FloorWidth, 1f, FloorDepth);
            MaterialUtil.ApplyLitColor(floor.GetComponent<Renderer>(), new Color(0.75f, 0.72f, 0.68f));
            floor.AddComponent<SurfaceType>().Surface = Surface.Tile;
        }

        // Raw wall panel footprint (probed): 4m wide x 4m tall x 0.5m thick. Every wall
        // segment below is a *visual* real-asset panel non-uniformly stretched to span
        // its segment, layered in front of an invisible primitive of the exact same
        // collision geometry Milestone 2 already validated (mousehole gap included) —
        // see the class comment for why collision stays on the old primitives.
        private static readonly Vector3 RawWallSize = new(4f, 4f, 0.5f);

        private static void BuildWalls(Transform parent)
        {
            const float thickness = 1f;
            const float mouseholeWidth = 3f; // a real gap, not just visual - the spawn/delivery point
            var color = new Color(0.85f, 0.83f, 0.78f);
            var halfW = FloorWidth / 2f;
            var halfD = FloorDepth / 2f;

            BuildWallSegment(parent, "Wall_North", new Vector3(0f, CeilingHeight / 2f, halfD),
                new Vector3(FloorWidth + thickness, CeilingHeight, thickness), color,
                "Kitchen_WallWindow", Quaternion.identity); // window + counter/sink area, per section 6
            BuildWallSegment(parent, "Wall_East", new Vector3(halfW, CeilingHeight / 2f, 0f),
                new Vector3(thickness, CeilingHeight, FloorDepth + thickness), color,
                "Kitchen_Wall", Quaternion.Euler(0f, 90f, 0f));

            // West wall carries the mousehole - built as two segments with a gap, same
            // technique ArenaBuilder.BuildWallWithGap used for house doors.
            var mouseholeZ = MouseholePosition.z;
            var segmentLength = (FloorDepth - mouseholeWidth) / 2f;
            BuildWallSegment(parent, "Wall_West_South",
                new Vector3(-halfW, CeilingHeight / 2f, mouseholeZ - mouseholeWidth / 2f - segmentLength / 2f),
                new Vector3(thickness, CeilingHeight, segmentLength), color,
                "Kitchen_Wall", Quaternion.Euler(0f, 90f, 0f));
            BuildWallSegment(parent, "Wall_West_North",
                new Vector3(-halfW, CeilingHeight / 2f, mouseholeZ + mouseholeWidth / 2f + segmentLength / 2f),
                new Vector3(thickness, CeilingHeight, FloorDepth - mouseholeWidth - segmentLength), color,
                "Kitchen_Wall", Quaternion.Euler(0f, 90f, 0f));

            BuildWallSegment(parent, "Wall_South", new Vector3(0f, CeilingHeight / 2f, -halfD),
                new Vector3(FloorWidth + thickness, CeilingHeight, thickness), color,
                "Kitchen_Wall", Quaternion.identity);

            // Mousehole itself - a small dark arch marking the spawn/delivery trigger
            // point, per section 3's "Mousehole: baseboard, south-west wall".
            var mousehole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mousehole.name = "Mousehole";
            mousehole.transform.SetParent(parent, false);
            mousehole.transform.localPosition = MouseholePosition + new Vector3(0f, 0.75f, 0f);
            mousehole.transform.localScale = new Vector3(0.5f, 1.5f, mouseholeWidth);
            MaterialUtil.ApplyLitColor(mousehole.GetComponent<Renderer>(), new Color(0.05f, 0.05f, 0.05f));
        }

        // One invisible collision block (unchanged room-boundary geometry) plus one real
        // visual panel stretched to match, sharing position/scale/rotation.
        private static void BuildWallSegment(Transform parent, string name, Vector3 position, Vector3 scale, Color color, string visualPrefabName, Quaternion visualRotation)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;
            MaterialUtil.ApplyLitColor(block.GetComponent<Renderer>(), color);
            block.GetComponent<Renderer>().enabled = false; // collision-only now that a real visual sits on top

            var visualPrefab = Resources.Load<GameObject>($"Kitchen/{visualPrefabName}");
            if (visualPrefab == null)
            {
                block.GetComponent<Renderer>().enabled = true; // fall back to the plain block if the real asset is missing
                return;
            }

            var visual = Object.Instantiate(visualPrefab, parent);
            visual.name = $"{name}_Visual";
            visual.transform.SetLocalPositionAndRotation(position, visualRotation);
            // scale is defined in the panel's own local space, before the rotation above is
            // applied - passing "span along local X/Y/Z" works regardless of which world
            // axis that ends up facing once rotated (see class comment / docs/DECISIONS.md).
            var spanX = visualRotation.eulerAngles.y > 45f ? scale.z : scale.x;
            var spanZ = visualRotation.eulerAngles.y > 45f ? scale.x : scale.z;
            visual.transform.localScale = new Vector3(spanX / RawWallSize.x, scale.y / RawWallSize.y, spanZ / RawWallSize.z);
        }

        // Quiet route from the mousehole toward the table - a rug patch (per section 3's
        // "Rug: between mousehole and table | Quiet route (soft surface)"). Invisible
        // primitive keeps its SurfaceType tag and collision footprint (Milestone 5's noise
        // model reads this); the real Kenney rug mesh (vertex-coloured, see
        // Assets/Shaders/VertexColorURP.shader) sits on top purely for looks.
        private static readonly Vector3 RawRugSize = new(15.7f, 0.1f, 9.2f);
        private static readonly Vector3 RawRugCenterOffset = new(7.85f, 0.05f, 4.6f); // off-centre pivot, probed

        private static void BuildRugRoute(Transform parent)
        {
            var rugStart = MouseholePosition;
            var rugEnd = new Vector3(TableCenter.x - TableWidth / 2f - 5f, 0f, TableCenter.z);
            var center = (rugStart + rugEnd) / 2f;
            var length = Vector3.Distance(rugStart, rugEnd);
            var direction = (rugEnd - rugStart).normalized;
            var rotation = Quaternion.LookRotation(direction, Vector3.up);

            var rug = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rug.name = "Rug";
            rug.transform.SetParent(parent, false);
            rug.transform.SetLocalPositionAndRotation(center + Vector3.up * 0.05f, rotation);
            rug.transform.localScale = new Vector3(6f, 0.1f, length);
            MaterialUtil.ApplyLitColor(rug.GetComponent<Renderer>(), new Color(0.6f, 0.15f, 0.15f));
            rug.GetComponent<Renderer>().enabled = false;
            rug.AddComponent<SurfaceType>().Surface = Surface.Rug;

            var rugVisualPrefab = Resources.Load<GameObject>("Kitchen/Kitchen_Rug");
            if (rugVisualPrefab == null)
            {
                rug.GetComponent<Renderer>().enabled = true;
                return;
            }

            var scale = new Vector3(6f / RawRugSize.x, 1f, length / RawRugSize.z);
            var rugVisual = Object.Instantiate(rugVisualPrefab, parent);
            rugVisual.name = "Rug_Visual";
            // Corrects for the raw mesh's own off-centre pivot so the *visible* rug lands
            // on `center` regardless of the scale/rotation applied to reach it there.
            var offset = rotation * Vector3.Scale(RawRugCenterOffset, scale);
            rugVisual.transform.SetPositionAndRotation(center + Vector3.up * 0.05f - offset, rotation);
            rugVisual.transform.localScale = scale;
        }

        private static GameObject BuildTable(Transform parent)
        {
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Table";
            table.transform.SetParent(parent, false);
            table.transform.localPosition = TableCenter + Vector3.up * (TableTopHeight - 0.75f);
            table.transform.localScale = new Vector3(TableWidth, 1.5f, TableDepth);
            MaterialUtil.ApplyLitColor(table.GetComponent<Renderer>(), new Color(0.5f, 0.35f, 0.2f));
            table.GetComponent<Renderer>().enabled = false;

            // A leg for visual grounding - not climbable itself, the chair (north side,
            // per section 3) is the actual route up.
            var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leg.name = "Table_Leg";
            leg.transform.SetParent(parent, false);
            leg.transform.localPosition = TableCenter + Vector3.up * (TableTopHeight / 2f - 0.75f);
            leg.transform.localScale = new Vector3(2f, TableTopHeight - 1.5f, 2f);
            MaterialUtil.ApplyLitColor(leg.GetComponent<Renderer>(), new Color(0.4f, 0.28f, 0.16f));
            leg.GetComponent<Renderer>().enabled = false;

            var visualPrefab = Resources.Load<GameObject>("Kitchen/Kitchen_Table");
            if (visualPrefab == null)
            {
                table.GetComponent<Renderer>().enabled = true;
                leg.GetComponent<Renderer>().enabled = true;
                return table;
            }

            var visual = Object.Instantiate(visualPrefab, parent);
            visual.name = "Table_Visual";
            visual.transform.localPosition = TableCenter; // raw mesh is grounded at its own pivot
            visual.transform.localScale = new Vector3(TableVisualScaleXZ, TableVisualScaleY, TableVisualScaleXZ);

            return table;
        }

        // The climb route: leg (floor -> seat, one continuous Climbable zone rather than
        // separate leg/rung sub-stages - a deliberate greybox simplification, see
        // docs/DECISIONS.md) -> seat (a real solid platform to stand/walk on) -> a second
        // short Climbable zone up the table's near edge (seat -> table top). Collision
        // geometry unchanged from Milestone 2 — see class comment.
        private static void BuildChairClimbRoute(Transform parent, GameObject table)
        {
            var chairX = TableCenter.x - TableWidth / 2f - 1.5f;
            var chairZ = TableCenter.z;
            var chairColor = new Color(0.45f, 0.32f, 0.2f);

            // Leg: a wide climbable pillar from floor to just under the seat.
            var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leg.name = "Chair_Leg_Climbable";
            leg.transform.SetParent(parent, false);
            leg.transform.localPosition = new Vector3(chairX, ChairSeatHeight / 2f, chairZ);
            leg.transform.localScale = new Vector3(2.5f, ChairSeatHeight, 2.5f);
            MaterialUtil.ApplyLitColor(leg.GetComponent<Renderer>(), chairColor);
            leg.GetComponent<Renderer>().enabled = false;
            leg.AddComponent<Climbable>();

            // Seat: solid platform, flush with the table's near edge so reaching the
            // table from here is a short climb, not a jump across a gap.
            var seat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seat.name = "Chair_Seat";
            seat.transform.SetParent(parent, false);
            seat.transform.localPosition = new Vector3(chairX, ChairSeatHeight - 0.25f, chairZ);
            seat.transform.localScale = new Vector3(4f, 0.5f, 4f);
            MaterialUtil.ApplyLitColor(seat.GetComponent<Renderer>(), chairColor);
            seat.GetComponent<Renderer>().enabled = false;

            // Seat-to-table climb: a thin climbable strip flush against the table's near
            // face, spanning from the seat up to the table top.
            var climbHeight = TableTopHeight - ChairSeatHeight;
            var seatToTable = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seatToTable.name = "SeatToTable_Climbable";
            seatToTable.transform.SetParent(parent, false);
            seatToTable.transform.localPosition = new Vector3(
                TableCenter.x - TableWidth / 2f - 0.4f, ChairSeatHeight + climbHeight / 2f, chairZ);
            seatToTable.transform.localScale = new Vector3(0.8f, climbHeight, 3f);
            MaterialUtil.ApplyLitColor(seatToTable.GetComponent<Renderer>(), new Color(0.5f, 0.4f, 0.3f, 0.4f));
            seatToTable.GetComponent<Renderer>().enabled = false;
            seatToTable.AddComponent<Climbable>();

            var chairVisualPrefab = Resources.Load<GameObject>("Kitchen/Kitchen_Chair");
            if (chairVisualPrefab == null)
            {
                leg.GetComponent<Renderer>().enabled = true;
                seat.GetComponent<Renderer>().enabled = true;
            }
            else
            {
                // Real chair_A is a single unlabelled mesh with no discoverable "seat"
                // sub-part (checked directly) — scaled from an estimated seat-height
                // fraction of its total bounds (~47%, a plausible dining-chair
                // proportion) so it *looks* roughly right next to the climb route,
                // without the climb route's own collision depending on that estimate
                // being exact. See docs/DECISIONS.md.
                const float estimatedSeatFraction = 0.47f;
                const float rawChairHeight = 1.208f;
                var chairVisualScale = ChairSeatHeight / (estimatedSeatFraction * rawChairHeight);
                var chairVisual = Object.Instantiate(chairVisualPrefab, parent);
                chairVisual.name = "Chair_Visual";
                chairVisual.transform.localPosition = new Vector3(chairX, 0f, chairZ);
                chairVisual.transform.localScale = Vector3.one * chairVisualScale;
            }

            table.name = "Table"; // no-op, keeps the parameter used/readable at the call site
        }

        // Milestone 4's "lower it down the tablecloth" descent method — a second Climbable
        // zone at the table's east edge, opposite the chair, spanning floor-to-table-top.
        // Reuses the exact same player climbing mechanic from Milestone 2 (proximity-gated,
        // not route-specific) — a player gripping a loot item can walk it down this zone
        // exactly like carrying it down the chair, since LootItem's carry height now
        // tracks the gripper's own Y (see docs/DECISIONS.md). No greybox visual — it's a
        // thin invisible climb trigger with a real hanging-cloth look left for a future
        // art pass (this milestone's done-criterion is "all descent methods work," not
        // full art parity for the newest one).
        private static void BuildTablecloudClimbRoute(Transform parent)
        {
            var zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zone.name = "Tablecloth_Climbable";
            zone.transform.SetParent(parent, false);
            zone.transform.localPosition = TablecloudClimbPosition;
            zone.transform.localScale = new Vector3(0.8f, TableTopHeight, 3f);
            MaterialUtil.ApplyLitColor(zone.GetComponent<Renderer>(), new Color(0.7f, 0.15f, 0.15f, 0.5f));
            zone.AddComponent<Climbable>();
        }

        private static void BuildFridge(Transform parent)
        {
            var visualPrefab = Resources.Load<GameObject>("Kitchen/Kitchen_Fridge");
            var position = new Vector3(FloorWidth / 2f - 12f, 0f, FloorDepth / 2f - 13f);

            if (visualPrefab == null)
            {
                BuildBlock(parent, "Fridge_Greybox", position + Vector3.up * 12.5f, new Vector3(9f, 25f, 7f), new Color(0.8f, 0.8f, 0.85f));
                return;
            }

            const float scale = 10f; // 25 (target height) / 2.5 (raw height)
            var fridge = Object.Instantiate(visualPrefab, parent);
            fridge.name = "Fridge";
            fridge.transform.localPosition = position;
            fridge.transform.localScale = Vector3.one * scale;

            var collider = fridge.AddComponent<BoxCollider>();
            collider.size = new Vector3(2f, 2.5f, 2.24f); // raw local bounds, probed
            collider.center = new Vector3(0f, 1.25f, 0.12f);
        }

        private static void BuildCounter(Transform parent)
        {
            var visualPrefab = Resources.Load<GameObject>("Kitchen/Kitchen_CounterStraight");
            var position = new Vector3(0f, 0f, FloorDepth / 2f - 3f);

            if (visualPrefab == null)
            {
                BuildBlock(parent, "Counter_Greybox", position + Vector3.up * (CounterHeight / 2f), new Vector3(40f, CounterHeight, 6f), new Color(0.7f, 0.68f, 0.62f));
                return;
            }

            // One module, stretched to span the counter run — see docs/DECISIONS.md for
            // why tiling several real modules (more accurate, much more code) was
            // descoped this milestone. Raw local bounds probed: (2, 1, 2.042).
            var counter = Object.Instantiate(visualPrefab, parent);
            counter.name = "Counter";
            counter.transform.localPosition = position;
            counter.transform.localScale = new Vector3(40f / 2f, CounterHeight / 1f, 6f / 2.042f);

            var collider = counter.AddComponent<BoxCollider>();
            collider.size = new Vector3(2f, 1f, 2.042f);
            collider.center = new Vector3(0f, 0.5f, 0.021f);
        }

        // Static only - no AI/state machine yet (Milestone 6). Purely spatial: the whole
        // point of the level's layout is loot being within arm's reach of him.
        private static void BuildGiantPlaceholder(Transform parent)
        {
            var prefab = Resources.Load<GameObject>("GiantModel_Placeholder");
            if (prefab == null)
            {
                return; // greybox still works without him; see docs/PROGRESS.md if this fires
            }

            var giant = Object.Instantiate(prefab, parent);
            giant.name = "Giant_Placeholder";
            giant.transform.localPosition = TableCenter + new Vector3(0f, 0f, TableDepth / 2f + 1.5f);
            giant.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        }

        private static void BuildBlock(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;
            MaterialUtil.ApplyLitColor(block.GetComponent<Renderer>(), color);
        }
    }
}
