using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace CubeArena.Shared
{
    // A pushable/grabbable/throwable physics prop. Deliberately built on NGO's built-in
    // NetworkTransform + NetworkRigidbody instead of this project's usual hand-rolled
    // NetworkVariable replication (see PlayerController/PickupController) — rigidbody
    // state (velocity, angular velocity, collision response) is meaningfully harder to
    // hand-roll well than the simple kinematic pose this project replicates elsewhere.
    // See docs/NETCODE.md for the full writeup.
    //
    // Authority model: AuthorityMode is permanently Owner — never toggled at runtime.
    // Instead, authority moves by changing *who owns it*:
    //   - Not held: owner = Unity.Netcode.NetworkManager.ServerClientId, so "owner-authoritative" ==
    //     server-authoritative physics (at rest, pushed, mid-throw).
    //   - Held: owner = the holding player's client, so their own client simulates the
    //     carry with zero added latency, while everyone else sees it via NetworkTransform's
    //     normal owner-authoritative replication + interpolation.
    // Ownership transfer forces a full teleport/resync on NetworkTransform and flips the
    // Rigidbody's kinematic state on both old and new owner (confirmed via the installed
    // NGO package's own source) — expect one visible snap at the instant of grab and of
    // release. Only changing ownership at those two discrete moments (not continuously)
    // keeps that acceptable.
    [RequireComponent(typeof(Rigidbody))]
    public class CrateController : NetworkBehaviour
    {
        public const float CrateSize = 0.8f;
        public const float GrabRange = 2.5f;
        private const float HoldDistance = 1.5f;
        private const float HoldHeight = 1.2f;

        // Server-side only: mirrors PlayerController.ActiveServerPlayers — lets
        // PlayerController's grab-request handler find the nearest un-held crate without
        // a scene-wide FindObjectsByType scan.
        public static readonly List<CrateController> ActiveServerCrates = new();

        private Rigidbody _rigidbody;

        // Owner-client-side only: this client's own local player transform, used to
        // compute the hold point while holding a crate. Cached once via
        // PlayerController.LocalPlayerSpawned rather than looked up every frame.
        private Transform _localHolderTransform;

        // Not held by anyone == owned by the server. NetworkBehaviour.OwnerClientId is
        // already replicated, so this needs no extra state of its own.
        public bool IsHeld => OwnerClientId != Unity.Netcode.NetworkManager.ServerClientId;

        // Client-side only: whichever crate (if any) this local client currently owns as
        // a "hold", not just as a leftover from being the last one to touch it. Kept in
        // sync via OnOwnershipChanged below, not tracked by PlayerController itself — the
        // server is what actually decides whether a grab succeeds, so the client-side
        // input handler just reacts to whatever ownership state actually lands.
        public static CrateController LocalHeldCrate { get; private set; }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            PlayerController.LocalPlayerSpawned += OnLocalPlayerSpawned;
        }

        public override void OnDestroy()
        {
            PlayerController.LocalPlayerSpawned -= OnLocalPlayerSpawned;
            if (LocalHeldCrate == this)
            {
                LocalHeldCrate = null;
            }

            base.OnDestroy();
        }

        protected override void OnOwnershipChanged(ulong previous, ulong current)
        {
            base.OnOwnershipChanged(previous, current);

            if (IsOwner && current != Unity.Netcode.NetworkManager.ServerClientId)
            {
                LocalHeldCrate = this;
            }
            else if (LocalHeldCrate == this)
            {
                LocalHeldCrate = null;
            }
        }

        private void OnLocalPlayerSpawned(PlayerController player)
        {
            _localHolderTransform = player.transform;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                ActiveServerCrates.Add(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                ActiveServerCrates.Remove(this);
            }
        }

        // Server-only: called right after Instantiate, before Spawn — same ordering as
        // PlayerController.ServerInitialize/PickupController.ServerInitialize, so the
        // initial spawn message already carries the correct position.
        public void ServerInitialize(Vector3 position)
        {
            transform.position = position;
        }

        // Server-only: called by PlayerController's grab-request handler once it's found
        // this as the nearest un-held crate within range. Just an ownership transfer —
        // NetworkRigidbody reacts to that automatically (flips kinematic state on both
        // sides), nothing else needs to happen here.
        public void ServerGrab(ulong clientId)
        {
            NetworkObject.ChangeOwnership(clientId);
        }

        // Server-only: called by PlayerController's throw-request handler. releaseVelocity
        // is whatever the holder's own client computed and sent explicitly — NetworkRigidbody
        // isn't confirmed to carry velocity through an ownership handoff on its own, so this
        // doesn't rely on that; it's set directly, after regaining authority, so the throw
        // continues smoothly under server simulation instead of the crate just stopping dead
        // at the moment of handoff.
        public void ServerRelease(Vector3 releaseVelocity)
        {
            NetworkObject.ChangeOwnership(Unity.Netcode.NetworkManager.ServerClientId);
            _rigidbody.linearVelocity = releaseVelocity;
        }

        private void FixedUpdate()
        {
            // Only the current holder positions the crate — everyone else just sees the
            // result via NetworkTransform's normal owner-authoritative replication. Rigidbody
            // .MovePosition/.MoveRotation rather than writing transform directly: this
            // Rigidbody is non-kinematic while held (NetworkRigidbody made the holder
            // authoritative), and teleporting a dynamic Rigidbody's transform every frame
            // instead of moving it through the physics system causes exactly the jittery
            // collision behavior MovePosition/MoveRotation exist to avoid.
            if (!IsOwner || IsServer || !IsHeld || _localHolderTransform == null)
            {
                return;
            }

            var holdPoint = _localHolderTransform.position
                             + _localHolderTransform.forward * HoldDistance
                             + Vector3.up * HoldHeight;
            _rigidbody.MovePosition(holdPoint);
            _rigidbody.MoveRotation(_localHolderTransform.rotation);
        }

        // Same runtime-prefab requirements as PlayerController.CreateTemplate — see its
        // comment for the full explanation (GlobalObjectIdHash + staying active).
        private const uint CrateGlobalObjectIdHash = 0xC4A7E001;
        private static readonly FieldInfo GlobalObjectIdHashField =
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Vector3 TemplateParkPosition = new(0f, -1000f, 0f);

        public static GameObject CreateTemplate()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Crate";
            root.transform.position = TemplateParkPosition;
            root.transform.localScale = new Vector3(CrateSize, CrateSize, CrateSize);
            MaterialUtil.ApplyLitColor(root.GetComponent<Renderer>(), new Color(0.55f, 0.4f, 0.22f)); // wood crate

            var networkObject = root.AddComponent<NetworkObject>();
            GlobalObjectIdHashField.SetValue(networkObject, CrateGlobalObjectIdHash);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.linearDamping = 0.3f;
            rb.angularDamping = 0.3f;

            // AuthorityMode = Owner is the crux of the whole authority model — see the
            // class comment. Interpolate stays on (default) for smooth remote viewing.
            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.Interpolate = true;

            // Requires NetworkTransform + Rigidbody on the same GameObject (both already
            // added above) — mirrors whatever AuthorityMode the NetworkTransform uses and
            // automatically flips Rigidbody.isKinematic on the non-authoritative side.
            root.AddComponent<NetworkRigidbody>();

            root.AddComponent<CrateController>();
            return root;
        }
    }
}
