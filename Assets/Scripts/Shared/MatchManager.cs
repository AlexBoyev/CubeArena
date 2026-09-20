using System;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace CubeArena.Shared
{
    // Server-authoritative match clock, replicated to every client so everyone sees the
    // same countdown (ClientBootstrap's HUD reads TimeRemaining). One instance, spawned
    // once by ServerBootstrap at startup and reused across match rounds — ResetForNewRound
    // restarts the clock instead of respawning the object.
    public class MatchManager : NetworkBehaviour
    {
        public const float MatchDurationSeconds = 300f; // 5 minutes — see docs/ROADMAP.md

        public static MatchManager Instance { get; private set; }

        // Server-only: ServerBootstrap subscribes to disconnect everyone and start the
        // next round once the clock runs out.
        public event Action MatchEnded;

        private readonly NetworkVariable<float> _timeRemaining = new(
            MatchDurationSeconds, writePerm: NetworkVariableWritePermission.Server);

        private bool _hasEnded;

        public float TimeRemaining => _timeRemaining.Value;

        public override void OnNetworkSpawn()
        {
            Instance = this;
        }

        private void Update()
        {
            if (!IsServer || _hasEnded)
            {
                return;
            }

            _timeRemaining.Value = Mathf.Max(0f, _timeRemaining.Value - Time.deltaTime);
            if (_timeRemaining.Value <= 0f)
            {
                _hasEnded = true;
                MatchEnded?.Invoke();
            }
        }

        // Server-only: called by ServerBootstrap after handling MatchEnded, once everyone
        // has been disconnected, so the next round starts with a fresh clock.
        public void ResetForNewRound()
        {
            _timeRemaining.Value = MatchDurationSeconds;
            _hasEnded = false;
        }

        // Same runtime-prefab requirements as PlayerController.CreateTemplate — see its
        // comment for the full explanation. This object has no visual children and is
        // never deactivated, so only the GlobalObjectIdHash half of that fix applies here.
        private const uint MatchManagerGlobalObjectIdHash = 0xA17CEA01;
        private static readonly FieldInfo GlobalObjectIdHashField =
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.NonPublic | BindingFlags.Instance);

        public static GameObject CreateTemplate()
        {
            var root = new GameObject("MatchManager");
            var networkObject = root.AddComponent<NetworkObject>();
            GlobalObjectIdHashField.SetValue(networkObject, MatchManagerGlobalObjectIdHash);
            root.AddComponent<MatchManager>();
            return root;
        }
    }
}
