using System.Collections.Generic;
using System.Reflection;
using CubeArena.Shared.Tuning;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace CubeArena.Shared
{
    // Multi-carrier loot (POCKET_HEIST_MASTER_PROMPT.md sections 6/7): the Milestone 3
    // replacement for CrateController's single-owner authority model, which fundamentally
    // can't support 2-5 simultaneous carriers (NetworkObject.ChangeOwnership() is exactly
    // one owner at a time — see docs/NETCODE.md's former "known limitation" entry, which
    // this class resolves). CrateController itself is left in the tree, unreferenced, same
    // as ArenaBuilder/PickupController — see docs/DECISIONS.md.
    //
    // Authority model: permanently server-owned/server-authoritative, never transferred.
    // NetworkTransform.AuthorityModes.Server (the default — CrateController deliberately
    // overrode it to .Owner; this class deliberately doesn't) replicates whatever the
    // server does to every client, gripping or not. Grip membership is a plain server-side
    // set — any player within GripRange, roughly facing the item, can add themselves.
    // Each tick the server moves the item toward the average position of its current
    // grippers, at DragSpeed (below requiredCarriers) or CarrySpeed (at/above it) — this
    // stands in for "the server averages grippers' input intent" from the brief in a way
    // that's simple to implement correctly for any gripper count and naturally handles a
    // carrier dropping out (the average, and therefore the carry/drag state, just
    // recomputes next tick) without special-casing disconnects or under-staffing.
    //
    // Milestone 4 change: the carried/dragged target height is now the *grippers' own
    // current Y* plus a small offset (CarryHeightOffset/DragHeightOffset), not a fixed
    // absolute height. This is what makes "carry it down the chair" and "lower it down
    // the tablecloth" (both new table-top descent methods) work for free — a gripper
    // physically climbing a Climbable zone (PlayerController.IsNearClimbable, unchanged
    // since Milestone 2, proximity-gated and not route-specific) naturally drags the item
    // down with them, no route-aware loot code needed. See docs/DECISIONS.md.
    [RequireComponent(typeof(Rigidbody))]
    public class LootItem : NetworkBehaviour
    {
        // Which placeholder visual to show — purely cosmetic, deterministic per-client
        // from this replicated value (same pattern as PlayerController's pose-driven
        // visual squash), not a real distinct mesh per kind (no jewelry/watch meshes in
        // the imported KayKit set — see docs/DECISIONS.md).
        public enum LootKind : byte
        {
            Coin,
            Ring,
            Wristwatch,
        }

        // Server-side only: every currently-spawned loot item, mirrors PlayerController.
        // ActiveServerPlayers — lets PlayerController's grip-request handler find the
        // nearest ungripped-by-me item without a scene-wide FindObjectsByType scan.
        public static readonly List<LootItem> ActiveServerLootItems = new();

        private Rigidbody _rigidbody;

        // Server-only: per-instance so Milestone 4's other four items (different
        // requiredCarriers/value/kind) are just more instances of this same class, not a
        // fork of it. Set via ServerInitialize, before Spawn (same convention as
        // PickupController/CrateController's own ServerInitialize).
        private int _requiredCarriers;
        private int _value;

        private readonly NetworkVariable<LootKind> _kind = new(
            writePerm: NetworkVariableWritePermission.Server);

        // Server-only: clientId -> the gripping player's own PlayerController, so the
        // per-tick average (below) can read live positions without a lookup each time,
        // and so a bank/despawn can tell every current gripper to clear their own grip
        // state (see PlayerController.ServerClearGrip).
        private readonly Dictionary<ulong, PlayerController> _grippers = new();

        // Server-only: set by ServerShove, cleared once free-fall settles enough to be
        // re-grippable normally again. While true, FixedUpdate does nothing at all and
        // lets Unity's own Rigidbody physics (gravity + collision, already enabled -
        // Server AuthorityMode leaves the server's own copy non-kinematic) carry the item
        // off the table edge for real, rather than a scripted glide — see ServerShove.
        private bool _isFreeFalling;

        private static LootSettings _lootSettings;

        // Milestone 3 diagnostic — same style as PlayerController's [Climb]/[Pos] logs.
        private float _lastPositionLogTime;
        private bool _wasCarrying;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            if (_lootSettings == null)
            {
                _lootSettings = Resources.Load<LootSettings>("LootSettings");
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                ActiveServerLootItems.Add(this);
            }

            ApplyVisual(_kind.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
            {
                return;
            }

            ActiveServerLootItems.Remove(this);

            // Defensive: banking (the only path that currently despawns an item) already
            // clears every gripper's own state before calling Despawn, but if this item is
            // ever destroyed some other way in the future, don't leave a player stuck
            // thinking they're still gripping something that no longer exists.
            foreach (var player in _grippers.Values)
            {
                player.ServerClearGrip();
            }

            _grippers.Clear();
        }

        // Server-only: called right after Instantiate, before Spawn — same ordering as
        // PickupController/CrateController's own ServerInitialize.
        public void ServerInitialize(Vector3 position, int requiredCarriers, int value, LootKind kind)
        {
            transform.position = position;
            _requiredCarriers = requiredCarriers;
            _value = value;
            _kind.Value = kind;
        }

        // Server-only: called by PlayerController's grip-toggle handler once it's found
        // this as the nearest ungripped-by-that-player item in range. Idempotent (a
        // Dictionary keyed by clientId) rather than assuming the caller already checked
        // membership.
        public bool ServerAddGripper(ulong clientId, PlayerController player)
        {
            _isFreeFalling = false; // re-gripping mid-fall (e.g. off the floor after a shove) resumes normal control
            _grippers[clientId] = player;
            return true;
        }

        // Server-only: called by PlayerController's grip-toggle handler (releasing) and
        // by ServerBootstrap-driven disconnect cleanup (PlayerController.OnNetworkDespawn).
        // A carrier disconnecting or letting go mid-carry just shrinks this set — the
        // per-tick average below recomputes from whoever's left, which is what "the item
        // continues (fewer carriers) or drops to a drag" actually reduces to, with no
        // separate disconnect-specific code path.
        public void ServerRemoveGripper(ulong clientId)
        {
            _grippers.Remove(clientId);
        }

        // Server-only: Milestone 4's "shove it off the edge" descent method
        // (POCKET_HEIST_MASTER_PROMPT.md section 6) — called by PlayerController's shove
        // request handler. Clears every gripper (loot is never both gripped and
        // free-falling) and hands the item to real Rigidbody physics with an outward
        // velocity, instead of the average-position glide FixedUpdate normally does.
        //
        // Milestone 5 TODO: this is where the brief's "+60 noise spike" (section 8) plugs
        // in once the noise model exists — same hook-point pattern already left for
        // under-staffed dragging.
        public void ServerShove(Vector3 horizontalDirection)
        {
            foreach (var player in _grippers.Values)
            {
                player.ServerClearGrip();
            }

            _grippers.Clear();

            _isFreeFalling = true;
            _freeFallStartTime = Time.time;
            var shoveSpeed = _lootSettings != null ? _lootSettings.ShoveSpeed : 6f;
            _rigidbody.linearVelocity = horizontalDirection.normalized * shoveSpeed + Vector3.up * 1f;

            Debug.Log($"[Loot] {name} shoved, velocity={_rigidbody.linearVelocity}");
        }

        private void FixedUpdate()
        {
            if (!IsServer)
            {
                return;
            }

            if (_isFreeFalling)
            {
                // Real Rigidbody physics (gravity + collision) is doing the work here -
                // deliberately no MovePosition call while this is true. Considered
                // "settled" once it's basically stopped moving; a max-duration safety
                // net avoids a rare case (e.g. an odd collision angle) leaving it falling
                // forever off the edge of the playable floor.
                if (_rigidbody.linearVelocity.sqrMagnitude < 0.05f || Time.time - _freeFallStartTime > FreeFallMaxDuration)
                {
                    _isFreeFalling = false;
                }

                return;
            }

            if (_grippers.Count == 0)
            {
                // Nobody's gripping it and it's not mid-shove - leave it exactly where it
                // is (real Rigidbody physics, not overridden by MovePosition, handles any
                // remaining settling - e.g. a drag abandoned mid-slide off a raised
                // surface). This also means an item dropped on the table stays on the
                // table instead of being forced toward a hardcoded floor height, unlike
                // Milestone 3's original version - see docs/DECISIONS.md.
                if (_wasCarrying)
                {
                    Debug.Log($"[Loot] {name} carrying=False grippers=0/{_requiredCarriers} pos={transform.position}");
                }

                _wasCarrying = false;
                return;
            }

            var average = Vector3.zero;
            foreach (var player in _grippers.Values)
            {
                average += player.transform.position;
            }

            average /= _grippers.Count;

            var carrying = _grippers.Count >= _requiredCarriers;
            var carryOffset = _lootSettings != null ? _lootSettings.CarryHeightOffset : 1.2f;
            var dragOffset = _lootSettings != null ? _lootSettings.DragHeightOffset : 0.1f;
            var dragSpeed = _lootSettings != null ? _lootSettings.DragSpeed : 1.2f;
            var carrySpeed = _lootSettings != null ? _lootSettings.CarrySpeed : 3.5f;

            var targetY = average.y + (carrying ? carryOffset : dragOffset);
            var target = new Vector3(average.x, targetY, average.z);
            var speed = carrying ? carrySpeed : dragSpeed;
            _rigidbody.MovePosition(Vector3.MoveTowards(_rigidbody.position, target, speed * Time.fixedDeltaTime));

            CheckBanking();

            if (carrying != _wasCarrying)
            {
                Debug.Log($"[Loot] {name} carrying={carrying} grippers={_grippers.Count}/{_requiredCarriers} pos={transform.position}");
            }

            _wasCarrying = carrying;

            if (Time.time - _lastPositionLogTime > 2f)
            {
                _lastPositionLogTime = Time.time;
                Debug.Log($"[Loot] {name} pos={transform.position} grippers={_grippers.Count}/{_requiredCarriers} carrying={carrying}");
            }
        }

        private float _freeFallStartTime;
        private const float FreeFallMaxDuration = 3f;

        private void CheckBanking()
        {
            var bankRadius = _lootSettings != null ? _lootSettings.BankRadius : 2.5f;
            var toMousehole = transform.position - KitchenBuilder.MouseholePosition;
            toMousehole.y = 0f;
            if (toMousehole.sqrMagnitude > bankRadius * bankRadius)
            {
                return;
            }

            Debug.Log($"[Bank] {name} banked for {_value} (grippers={_grippers.Count}).");

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.AddBankedLoot(_value);
            }

            foreach (var player in _grippers.Values)
            {
                player.ServerClearGrip();
            }

            _grippers.Clear();

            var networkObject = GetComponent<NetworkObject>();
            if (networkObject.IsSpawned)
            {
                networkObject.Despawn(); // destroys the instance too (default destroyWithScene-equivalent behavior)
            }
        }

        // Placeholder visuals, differentiated by colour/size only (no jewelry/watch mesh
        // in the imported KayKit set — see docs/DECISIONS.md). Runs identically on every
        // client (including the server's own view), driven purely by the replicated
        // `_kind`, the same "deterministic local application of replicated state" pattern
        // PlayerController already uses for pose-driven visual squash.
        private void ApplyVisual(LootKind kind)
        {
            var renderer = GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            switch (kind)
            {
                case LootKind.Ring:
                    transform.localScale = new Vector3(RingRadius * 2f, CoinThickness / 2f, RingRadius * 2f);
                    MaterialUtil.ApplyLitColor(renderer, new Color(0.95f, 0.95f, 0.85f)); // bright silver/white gold
                    break;
                case LootKind.Wristwatch:
                    transform.localScale = new Vector3(WristwatchRadius * 2f, CoinThickness / 2f, WristwatchRadius * 2f);
                    MaterialUtil.ApplyLitColor(renderer, new Color(0.25f, 0.25f, 0.3f)); // dark gunmetal
                    break;
                default:
                    transform.localScale = new Vector3(CoinRadius * 2f, CoinThickness / 2f, CoinRadius * 2f);
                    MaterialUtil.ApplyLitColor(renderer, new Color(1f, 0.85f, 0.1f)); // gold
                    break;
            }
        }

        // Same runtime-prefab requirements as PlayerController.CreateTemplate — see its
        // comment for the full explanation (GlobalObjectIdHash + staying active).
        private const uint LootItemGlobalObjectIdHash = 0x100747E1;
        private static readonly FieldInfo GlobalObjectIdHashField =
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Vector3 TemplateParkPosition = new(0f, -1000f, 0f);

        // Flattened cylinder, tinted per LootKind (ApplyVisual) — placeholder visuals
        // (real coin/ring/watch meshes are a future art pass, not this milestone's job).
        private const float CoinRadius = 0.4f;
        private const float RingRadius = 0.25f;
        private const float WristwatchRadius = 0.35f;
        private const float CoinThickness = 0.15f;

        public static GameObject CreateTemplate()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            root.name = "Loot_Item";
            root.transform.position = TemplateParkPosition;
            // A cylinder's default is 2m tall, 1m diameter — squash it into a coin.
            // ApplyVisual overrides this per-instance once _kind replicates in, so this
            // starting scale only matters for the parked template itself.
            root.transform.localScale = new Vector3(CoinRadius * 2f, CoinThickness / 2f, CoinRadius * 2f);
            MaterialUtil.ApplyLitColor(root.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.1f));

            // CreatePrimitive(Cylinder) auto-adds a CapsuleCollider, which does not
            // handle this extreme a non-uniform squash correctly — under Unity's
            // automatic axis handling it effectively collided like a near-sphere of
            // radius ~CoinRadius (confirmed live: the coin rested at y~0.39, not the
            // ~0.075 half-thickness expected), floating well above the intended flat
            // coin shape. A BoxCollider respects localScale per-axis directly, so it
            // actually matches the squashed visual.
            Object.Destroy(root.GetComponent<CapsuleCollider>());
            var boxCollider = root.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(1f, 2f, 1f); // matches the cylinder mesh's own local (pre-scale) bounds

            var networkObject = root.AddComponent<NetworkObject>();
            GlobalObjectIdHashField.SetValue(networkObject, LootItemGlobalObjectIdHash);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 2f;
            rb.linearDamping = 0.5f;
            rb.angularDamping = 0.5f;
            rb.constraints = RigidbodyConstraints.FreezeRotation; // held/dragged items shouldn't tumble; a shoved one still translates and falls correctly with rotation frozen

            // Default AuthorityModes.Server (not overridden, unlike CrateController's
            // deliberate .Owner) — the server is always who moves this, so this is
            // exactly the mode that wants: FixedUpdate above only ever runs the movement
            // logic on the server (IsServer guard), replicated to every client including
            // whoever's gripping.
            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.Interpolate = true;

            // Requires NetworkTransform + Rigidbody on the same GameObject (both already
            // added above) — keeps the Rigidbody kinematic everywhere except the server,
            // consistent with Server authority mode.
            root.AddComponent<NetworkRigidbody>();

            root.AddComponent<LootItem>();
            return root;
        }
    }
}
