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
            if (!IsServer)
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

        // Doesn't avoid ArenaBuilder's fixed obstacle cubes — an occasional pickup landing
        // inside/behind one is an acceptable simplification for a first pass, since it
        // self-corrects on the next respawn.
        public static Vector3 GetRandomPosition()
        {
            var half = MovementConstants.ArenaHalfExtent - 3f; // margin from the walls
            var x = Random.Range(-half, half);
            var z = Random.Range(-half, half);
            return new Vector3(x, 0f, z);
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
