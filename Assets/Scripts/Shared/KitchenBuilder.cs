using UnityEngine;

namespace CubeArena.Shared
{
    // Builds "The Midnight Snacker" kitchen (docs/GAME_DESIGN.md section 3) purely from
    // code, same convention as the Cube Arena ArenaBuilder it replaces (CLAUDE.md: no
    // hand-authored scene/prefab assets) — every client and the server independently
    // produce byte-identical geometry with no network sync needed. All dimensions are
    // the real-world cm figures from docs/GAME_DESIGN.md section 2 x25.
    //
    // Milestone 2 scope: greybox only (primitives, no real KayKit/Kenney assets yet —
    // that's Milestone 4's "replace greybox with real assets" pass), enough geometry to
    // prove a thief can walk from the mousehole to the chair and climb it to the table
    // top. Loot items, the giant's AI, and lighting are later milestones — the giant
    // and a coin are placed here as static, non-interactive placeholders purely for
    // spatial layout, not gameplay yet.
    public static class KitchenBuilder
    {
        // World-scale numbers, x25, from docs/GAME_DESIGN.md section 2.
        public const float FloorWidth = 100f; // x (400cm)
        public const float FloorDepth = 87.5f; // z (350cm)
        public const float CeilingHeight = 65f;
        public const float TableTopHeight = 18.75f;
        public const float TableWidth = 30f;
        public const float TableDepth = 20f;
        public const float ChairSeatHeight = 11.25f;
        public const float CounterHeight = 22.5f;
        public const float GiantScale = 25f;

        // Table centered toward the room's north-east so there's a clear run from the
        // south-west mousehole across the rug to reach it — matches "Rug: between
        // mousehole and table" (section 3's area table).
        // Public: PlayerController's bot-mode climb test targets a point on the table
        // top computed from this.
        public static readonly Vector3 TableCenter = new(10f, 0f, 15f);
        // Public: SpawnPoints places all 6 slots near here, per section 3's "Mousehole:
        // spawn and delivery point" — not scattered around the room like Cube Arena's
        // corner-per-slot convention.
        public static readonly Vector3 MouseholePosition = new(-FloorWidth / 2f + 2f, 0f, -FloorDepth / 2f + 10f);

        // Milestone 3's "the coin test": the floor coin's spawn spot, per section 6's
        // loot table ("Floor under table"). Offset from TableCenter rather than placed
        // exactly on it — the table leg (BuildTable) is a 2x2 footprint centered on
        // TableCenter's own x/z, so a coin spawned there would sit inside it. Still
        // within the table's 30x20 footprint (clear of the giant's placeholder seat,
        // which faces south from the table's far side), just off to one corner.
        public static readonly Vector3 LootCoinSpawnPosition = TableCenter + new Vector3(5f, 0f, -5f);

        public static GameObject Build()
        {
            var root = new GameObject("Kitchen");

            BuildFloor(root.transform);
            BuildWalls(root.transform);
            BuildRugRoute(root.transform);
            var table = BuildTable(root.transform);
            BuildChairClimbRoute(root.transform, table);
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

        private static void BuildWalls(Transform parent)
        {
            const float thickness = 1f;
            const float mouseholeWidth = 3f; // a real gap, not just visual - the spawn/delivery point
            var color = new Color(0.85f, 0.83f, 0.78f);
            var halfW = FloorWidth / 2f;
            var halfD = FloorDepth / 2f;

            BuildBlock(parent, "Wall_North", new Vector3(0f, CeilingHeight / 2f, halfD), new Vector3(FloorWidth + thickness, CeilingHeight, thickness), color);
            BuildBlock(parent, "Wall_East", new Vector3(halfW, CeilingHeight / 2f, 0f), new Vector3(thickness, CeilingHeight, FloorDepth + thickness), color);

            // West wall carries the mousehole - built as two segments with a gap, same
            // technique ArenaBuilder.BuildWallWithGap used for house doors.
            var mouseholeZ = MouseholePosition.z;
            var segmentLength = (FloorDepth - mouseholeWidth) / 2f;
            BuildBlock(parent, "Wall_West_South",
                new Vector3(-halfW, CeilingHeight / 2f, mouseholeZ - mouseholeWidth / 2f - segmentLength / 2f),
                new Vector3(thickness, CeilingHeight, segmentLength), color);
            BuildBlock(parent, "Wall_West_North",
                new Vector3(-halfW, CeilingHeight / 2f, mouseholeZ + mouseholeWidth / 2f + segmentLength / 2f),
                new Vector3(thickness, CeilingHeight, FloorDepth - mouseholeWidth - segmentLength), color);

            BuildBlock(parent, "Wall_South", new Vector3(0f, CeilingHeight / 2f, -halfD), new Vector3(FloorWidth + thickness, CeilingHeight, thickness), color);

            // Mousehole itself - a small dark arch marking the spawn/delivery trigger
            // point, per section 3's "Mousehole: baseboard, south-west wall".
            var mousehole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mousehole.name = "Mousehole";
            mousehole.transform.SetParent(parent, false);
            mousehole.transform.localPosition = MouseholePosition + new Vector3(0f, 0.75f, 0f);
            mousehole.transform.localScale = new Vector3(0.5f, 1.5f, mouseholeWidth);
            MaterialUtil.ApplyLitColor(mousehole.GetComponent<Renderer>(), new Color(0.05f, 0.05f, 0.05f));
        }

        // Quiet route from the mousehole toward the table - a rug patch (per section 3's
        // "Rug: between mousehole and table | Quiet route (soft surface)"). Sits flush on
        // top of the tile floor with its own SurfaceType so Milestone 5's noise model can
        // tell them apart without re-tagging geometry.
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
            rug.AddComponent<SurfaceType>().Surface = Surface.Rug;
        }

        private static GameObject BuildTable(Transform parent)
        {
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Table";
            table.transform.SetParent(parent, false);
            table.transform.localPosition = TableCenter + Vector3.up * (TableTopHeight - 0.75f);
            table.transform.localScale = new Vector3(TableWidth, 1.5f, TableDepth);
            MaterialUtil.ApplyLitColor(table.GetComponent<Renderer>(), new Color(0.5f, 0.35f, 0.2f));

            // A leg for visual grounding - not climbable itself, the chair (north side,
            // per section 3) is the actual route up.
            var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leg.name = "Table_Leg";
            leg.transform.SetParent(parent, false);
            leg.transform.localPosition = TableCenter + Vector3.up * (TableTopHeight / 2f - 0.75f);
            leg.transform.localScale = new Vector3(2f, TableTopHeight - 1.5f, 2f);
            MaterialUtil.ApplyLitColor(leg.GetComponent<Renderer>(), new Color(0.4f, 0.28f, 0.16f));

            return table;
        }

        // The climb route: leg (floor -> seat, one continuous Climbable zone rather than
        // separate leg/rung sub-stages - a deliberate greybox simplification, see
        // docs/DECISIONS.md) -> seat (a real solid platform to stand/walk on) -> a second
        // short Climbable zone up the table's near edge (seat -> table top).
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
            leg.AddComponent<Climbable>();

            // Seat: solid platform, flush with the table's near edge so reaching the
            // table from here is a short climb, not a jump across a gap.
            var seat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seat.name = "Chair_Seat";
            seat.transform.SetParent(parent, false);
            seat.transform.localPosition = new Vector3(chairX, ChairSeatHeight - 0.25f, chairZ);
            seat.transform.localScale = new Vector3(4f, 0.5f, 4f);
            MaterialUtil.ApplyLitColor(seat.GetComponent<Renderer>(), chairColor);

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
            seatToTable.AddComponent<Climbable>();

            table.name = "Table"; // no-op, keeps the parameter used/readable at the call site
        }

        // Non-interactive greybox placeholder - out of bounds early milestones per
        // section 3's own table, so no collision logic beyond a plain solid block.
        private static void BuildFridge(Transform parent)
        {
            var fridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fridge.name = "Fridge_Greybox";
            fridge.transform.SetParent(parent, false);
            fridge.transform.localPosition = new Vector3(FloorWidth / 2f - 6f, 12.5f, FloorDepth / 2f - 6f);
            fridge.transform.localScale = new Vector3(9f, 25f, 7f);
            MaterialUtil.ApplyLitColor(fridge.GetComponent<Renderer>(), new Color(0.8f, 0.8f, 0.85f));
        }

        private static void BuildCounter(Transform parent)
        {
            var counter = GameObject.CreatePrimitive(PrimitiveType.Cube);
            counter.name = "Counter_Greybox";
            counter.transform.SetParent(parent, false);
            counter.transform.localPosition = new Vector3(0f, CounterHeight / 2f, FloorDepth / 2f - 3f);
            counter.transform.localScale = new Vector3(40f, CounterHeight, 6f);
            MaterialUtil.ApplyLitColor(counter.GetComponent<Renderer>(), new Color(0.7f, 0.68f, 0.62f));
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
