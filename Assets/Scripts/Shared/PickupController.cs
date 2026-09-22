using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace CubeArena.Shared
{
    // Server-authoritative collectible: walking within CollectRadius of one awards the
    // walking player a point (PlayerController.AddScore) and respawns it at a new random
    // spot. Position is manually replicated via a NetworkVariable, the same pattern as
    // PlayerController._serverPosition — this project doesn't use Unity's built-in
    // NetworkTransform anywhere, so pickups don't either, for consistency.
    public class PickupController : NetworkBehaviour
    {
        private const float CollectRadius = 1.2f;
        private const float CollectRadiusSqr = CollectRadius * CollectRadius;

        private readonly NetworkVariable<Vector3> _position = new(
            writePerm: NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            transform.position = _position.Value;
            if (!IsServer)
            {
                _position.OnValueChanged += (_, newValue) => transform.position = newValue;
            }
        }

        // Server-only: called right after Instantiate, before Spawn — same ordering as
        // PlayerController.ServerInitialize, so the initial spawn message already carries
        // the correct position instead of everyone briefly seeing it at the origin.
        public void ServerInitialize(Vector3 position)
        {
            _position.Value = position;
            transform.position = position;
        }

        private void Update()
        {
            // Not collectible until the host actually starts the match — walking around
            // grabbing free points during the lobby ("during lobby i can move and
            // collect") isn't just cosmetically odd, it's a real head-start exploit.
            if (!IsServer || (MatchManager.Instance != null && !MatchManager.Instance.MatchStarted))
            {
                return;
            }

            foreach (var player in PlayerController.ActiveServerPlayers)
            {
                if ((player.transform.position - transform.position).sqrMagnitude > CollectRadiusSqr)
                {
                    continue;
                }

                player.AddScore(1);
                ServerInitialize(GetRandomPosition());
                break;
            }
        }

        // Rejects any candidate that overlaps arena geometry (the ring obstacles, tower,
        // houses, tunnels, etc.) instead of just picking a bare random point — an earlier
        // version didn't check this at all, so pickups would occasionally spawn inside or
        // behind a solid obstacle where they were effectively uncollectable. Checked at
        // y=0.5 (where the pickup's visual actually sits) with a radius a bit larger than
        // its own 0.5m cube so it doesn't spawn flush against a wall either.
        private const float SpawnClearanceRadius = 0.6f;
        private const int MaxSpawnAttempts = 20;

        public static Vector3 GetRandomPosition()
        {
            // Kitchen floor bounds (KitchenBuilder.FloorWidth/FloorDepth), not the old
            // Cube Arena's centered ArenaHalfExtent square - left over from before the
            // kitchen replaced the arena, this used to spawn things in a tiny ~17x17
            // region that no longer bears any relation to the actual (much larger,
            // not-centered-the-same-way) kitchen floor. Not currently called (crate/
            // pickup spawning is disabled in ServerBootstrap pending Milestone 3's loot
            // redesign - see docs/PROGRESS.md), fixed anyway so it's correct whenever
            // something does call it again.
            const float margin = 3f;
            var halfX = KitchenBuilder.FloorWidth / 2f - margin;
            var halfZ = KitchenBuilder.FloorDepth / 2f - margin;
            for (var attempt = 0; attempt < MaxSpawnAttempts; attempt++)
            {
                var x = Random.Range(-halfX, halfX);
                var z = Random.Range(-halfZ, halfZ);
                var candidate = new Vector3(x, 0f, z);
                if (!Physics.CheckSphere(candidate + Vector3.up * 0.5f, SpawnClearanceRadius, ~0, QueryTriggerInteraction.Ignore))
                {
                    return candidate;
                }
            }

            // Exhausted every attempt (arena is unexpectedly crowded) — fall back to a
            // plain random point rather than looping forever.
            return new Vector3(Random.Range(-halfX, halfX), 0f, Random.Range(-halfZ, halfZ));
        }

        // Same runtime-prefab requirements as PlayerController.CreateTemplate.
        private const uint PickupGlobalObjectIdHash = 0xB00B1E55;
        private static readonly FieldInfo GlobalObjectIdHashField =
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Vector3 TemplateParkPosition = new(0f, -1000f, 0f);

        public static GameObject CreateTemplate()
        {
            var root = new GameObject("Pickup");
            root.transform.position = TemplateParkPosition;
            var networkObject = root.AddComponent<NetworkObject>();
            GlobalObjectIdHashField.SetValue(networkObject, PickupGlobalObjectIdHash);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            visual.transform.localPosition = new Vector3(0, 0.5f, 0); // rest on the ground
            Object.Destroy(visual.GetComponent<Collider>());
            MaterialUtil.ApplyLitColor(visual.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.1f)); // gold

            root.AddComponent<PickupController>();
            return root;
        }
    }
}
