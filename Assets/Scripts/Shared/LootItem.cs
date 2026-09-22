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
    // grippers, at DragSpeed (below requiredCarriers, item stays on the floor) or
    // CarrySpeed (at/above requiredCarriers, item lifts to CarryHeight) — this stands in
    // for "the server averages grippers' input intent" from the brief in a way that's
    // simple to implement correctly for any gripper count and naturally handles a
    // carrier dropping out (the average, and therefore the carry/drag state, just
    // recomputes next tick) without special-casing disconnects or under-staffing. See
    // docs/DECISIONS.md for the trade-off write-up.
    [RequireComponent(typeof(Rigidbody))]
    public class LootItem : NetworkBehaviour
    {
        // Server-side only: every currently-spawned loot item, mirrors PlayerController.
        // ActiveServerPlayers — lets PlayerController's grip-request handler find the
        // nearest ungripped-by-me item without a scene-wide FindObjectsByType scan.
        public static readonly List<LootItem> ActiveServerLootItems = new();

        private Rigidbody _rigidbody;

        // Server-only: per-instance so Milestone 4's other three items (different
        // requiredCarriers/value) are just more instances of this same class, not a
        // fork of it. Set via ServerInitialize, before Spawn (same convention as
        // PickupController/CrateController's own ServerInitialize).
        private int _requiredCarriers;
        private int _value;

        // Server-only: clientId -> the gripping player's own PlayerController, so the
        // per-tick average (below) can read live positions without a lookup each time,
        // and so a bank/despawn can tell every current gripper to clear their own grip
        // state (see PlayerController.ServerClearGrip).
        private readonly Dictionary<ulong, PlayerController> _grippers = new();

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
        public void ServerInitialize(Vector3 position, int requiredCarriers, int value)
        {
            transform.position = position;
            _requiredCarriers = requiredCarriers;
            _value = value;
        }

        // Server-only: called by PlayerController's grip-toggle handler once it's found
        // this as the nearest ungripped-by-that-player item in range. Idempotent (a
        // Dictionary keyed by clientId) rather than assuming the caller already checked
        // membership.
        public bool ServerAddGripper(ulong clientId, PlayerController player)
        {
            _grippers[clientId] = player;
            return true;
        }

        // Server-only: called by PlayerController's grip-toggle handler (releasing) and
        // by ServerBootstrap-driven disconnect cleanup (PlayerController.OnNetworkDespawn).
        // A carrier disconnecting or letting go mid-carry just shrinks this set — the
        // per-tick average below recomputes from whoever's left, which is what "the item
        // continues (fewer carriers) or drops to a drag/fall" actually reduces to, with
        // no separate disconnect-specific code path.
        public void ServerRemoveGripper(ulong clientId)
        {
            _grippers.Remove(clientId);
        }

        private void FixedUpdate()
        {
            if (!IsServer)
            {
                return;
            }

            var carrying = _grippers.Count >= _requiredCarriers && _grippers.Count > 0;

            if (_grippers.Count == 0)
            {
                // Nobody's gripping it — settle straight down to the floor if it was
                // mid-air (the brief's "or falls according to the remaining count", for
                // the specific case where the remaining count is zero), otherwise leave
                // it exactly where it is. Reuses DragSpeed as the fall rate rather than
                // adding a separate tunable for a case that's rare and not gameplay-critical.
                var floorTarget = new Vector3(transform.position.x, GroundedHeight, transform.position.z);
                var fallSpeed = _lootSettings != null ? _lootSettings.DragSpeed : 1.2f;
                _rigidbody.MovePosition(Vector3.MoveTowards(_rigidbody.position, floorTarget, fallSpeed * Time.fixedDeltaTime));
            }
            else
            {
                var average = Vector3.zero;
                foreach (var player in _grippers.Values)
                {
                    average += player.transform.position;
                }

                average /= _grippers.Count;

                var carryHeight = _lootSettings != null ? _lootSettings.CarryHeight : 1.2f;
                var dragSpeed = _lootSettings != null ? _lootSettings.DragSpeed : 1.2f;
                var carrySpeed = _lootSettings != null ? _lootSettings.CarrySpeed : 3.5f;

                var targetY = carrying ? carryHeight : GroundedHeight;
                var target = new Vector3(average.x, targetY, average.z);
                var speed = carrying ? carrySpeed : dragSpeed;
                _rigidbody.MovePosition(Vector3.MoveTowards(_rigidbody.position, target, speed * Time.fixedDeltaTime));

                CheckBanking();
            }

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

        // Resting height for a flat coin whose collider is CoinThickness tall, centered
        // on its own transform — half its thickness above y=0 so it sits flush on the
        // floor instead of half-buried in it.
        private const float GroundedHeight = CoinThickness / 2f;

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

        // Same runtime-prefab requirements as PlayerController.CreateTemplate — see its
        // comment for the full explanation (GlobalObjectIdHash + staying active).
        private const uint LootItemGlobalObjectIdHash = 0x100747E1;
        private static readonly FieldInfo GlobalObjectIdHashField =
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Vector3 TemplateParkPosition = new(0f, -1000f, 0f);

        // Flattened cylinder, tinted gold — a placeholder visual (real coin meshes are
        // Milestone 4's "replace greybox with real assets" pass, per docs/PROGRESS.md).
        private const float CoinRadius = 0.4f;
        private const float CoinThickness = 0.15f;

        public static GameObject CreateTemplate()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            root.name = "Loot_Coin";
            root.transform.position = TemplateParkPosition;
            // A cylinder's default is 2m tall, 1m diameter — squash it into a coin.
            root.transform.localScale = new Vector3(CoinRadius * 2f, CoinThickness / 2f, CoinRadius * 2f);
            MaterialUtil.ApplyLitColor(root.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.1f)); // gold, matches the old PickupController's color

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
            rb.constraints = RigidbodyConstraints.FreezeRotation; // a coin sliding/lifting shouldn't tumble — it's not being thrown or pushed, only ever moved via MovePosition

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
