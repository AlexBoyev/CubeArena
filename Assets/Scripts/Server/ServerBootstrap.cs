using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CubeArena.Shared;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace CubeArena.Server
{
    // Constructs the dedicated server entirely at runtime (NetworkManager + UnityTransport
    // added via code) rather than through a scene/prefab, so nothing here requires hand
    // editing a .unity or .prefab asset. See CLAUDE.md.
    public class ServerBootstrap : MonoBehaviour
    {
        // No win/score conditions exist yet (see docs/ROADMAP.md) — this is a simple
        // round-timer placeholder for now, not a real match-end condition.
        private const float MatchDurationSeconds = 300f; // 5 minutes

        private CancellationTokenSource _heartbeatCts;
        private readonly Dictionary<ulong, Guid> _connectedUsers = new();
        private readonly Dictionary<ulong, (Guid UserId, int SlotIndex)> _approvedPendingConnect = new();
        private FleetClient _fleet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
#if UNITY_SERVER
            var go = new GameObject(nameof(ServerBootstrap));
            DontDestroyOnLoad(go);
            go.AddComponent<ServerBootstrap>();
#endif
        }

        private void Start()
        {
            _ = RunAsync(ServerConfig.FromEnvironment());
        }

        public async Task RunAsync(ServerConfig config)
        {
            Time.fixedDeltaTime = 1f / MovementConstants.ServerTickRate;

            ArenaBuilder.Build();

            var playerTemplate = PlayerController.CreateTemplate();

            var networkManager = GetComponent<NetworkManager>() ?? gameObject.AddComponent<NetworkManager>();
            var transport = GetComponent<UnityTransport>() ?? gameObject.AddComponent<UnityTransport>();

            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            // NGO hashes ConnectionApproval into its NetworkConfig compatibility check, so
            // the Phase 5 client MUST also set ConnectionApproval = true — otherwise the
            // connection fails silently with a generic disconnect before ConnectionApprovalCallback
            // ever runs (no server-side log at all, since it's rejected before reaching that code).
            networkManager.NetworkConfig.ConnectionApproval = true;
            // The whole arena is built from code at runtime (ArenaBuilder), not through
            // Unity's actual scene-loading system, so there's no real scene for NGO's
            // scene-synchronization handshake to manage — disabled since it doesn't apply
            // here. Also part of NGO's NetworkConfig hash check, so the client MUST set the
            // same value.
            networkManager.NetworkConfig.EnableSceneManagement = false;
            networkManager.AddNetworkPrefab(playerTemplate);

            transport.SetConnectionData(config.AdvertiseHost, config.ListenPort, listenAddress: "0.0.0.0");

            var publicKey = await JwksClient.FetchPublicKeyAsync(
                $"{config.BackendUrl}/.well-known/jwks.json", config.TicketKeyId);
            var validator = new TicketValidator(publicKey, config.TicketIssuer, config.TicketAudience);

            _fleet = new FleetClient(config.BackendUrl, config.FleetApiKey);
            await _fleet.RegisterAsync(config.AdvertiseHost, config.ListenPort, config.Capacity);

            var approval = new ConnectionApprovalHandler(validator, networkManager, _fleet.SessionId, config.Capacity);
            networkManager.ConnectionApprovalCallback = approval.Approve;

            // Deliberately not spawned here: NGO hasn't finished establishing the
            // connection yet at this point in ConnectionApprovalCallback, so a player
            // object spawned synchronously inside it can spawn server-side and never
            // reach that specific client. OnClientConnectedCallback below is the
            // documented-safe point to spawn per-client objects once the connection is
            // actually finalized.
            approval.ClientApproved += (clientId, userId, slotIndex) =>
            {
                _approvedPendingConnect[clientId] = (userId, slotIndex);
            };

            networkManager.OnClientConnectedCallback += clientId =>
            {
                if (!_approvedPendingConnect.Remove(clientId, out var info))
                {
                    return;
                }

                _connectedUsers[clientId] = info.UserId;
                SpawnPlayer(playerTemplate, clientId, info.SlotIndex);
                _ = ConfirmSlotSafeAsync(info.UserId);
            };

            networkManager.OnClientDisconnectCallback += clientId =>
            {
                _approvedPendingConnect.Remove(clientId);
                if (_connectedUsers.Remove(clientId, out var userId))
                {
                    _ = ReleaseSlotSafeAsync(userId);
                }
            };

            if (!networkManager.StartServer())
            {
                Debug.LogError("[ServerBootstrap] StartServer() failed.");
                return;
            }

            Debug.Log($"[ServerBootstrap] Listening on 0.0.0.0:{config.ListenPort}, " +
                      $"advertising {config.AdvertiseHost}:{config.ListenPort}, session {_fleet.SessionId}");

            _heartbeatCts = new CancellationTokenSource();
            _ = _fleet.RunHeartbeatLoopAsync(
                TimeSpan.FromSeconds(10),
                () => networkManager.ConnectedClientsIds.Count,
                _heartbeatCts.Token);
            _ = RunMatchTimerLoopAsync(networkManager, _heartbeatCts.Token);
        }

        private void OnApplicationQuit()
        {
            _heartbeatCts?.Cancel();
        }

        // A leaving player only ever affects their own slot (OnClientDisconnectCallback
        // above) — the server, and everyone else's session, keeps running regardless.
        // This loop is the other half: a hard cap on how long a match can run before
        // everyone's sent back to character select and a fresh match window starts,
        // rather than one match running forever. The server process itself is untouched
        // either way — quick play can match players into it again immediately after.
        private async Task RunMatchTimerLoopAsync(NetworkManager networkManager, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(MatchDurationSeconds), token);

                    var connectedClientIds = new List<ulong>(networkManager.ConnectedClientsIds);
                    foreach (var clientId in connectedClientIds)
                    {
                        networkManager.DisconnectClient(clientId,
                            "Match ended (5 minute time limit) — quick play again to start a new match.");
                    }

                    if (connectedClientIds.Count > 0)
                    {
                        Debug.Log($"[ServerBootstrap] Match timer expired — disconnected {connectedClientIds.Count} client(s). Starting a new match window.");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Expected on shutdown — OnApplicationQuit cancels this token.
            }
        }

        private async Task ConfirmSlotSafeAsync(Guid userId)
        {
            try
            {
                await _fleet.ConfirmSlotAsync(_fleet.SessionId, userId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerBootstrap] ConfirmSlot failed for user {userId}: {e.Message}");
            }
        }

        private async Task ReleaseSlotSafeAsync(Guid userId)
        {
            try
            {
                await _fleet.ReleaseSlotAsync(_fleet.SessionId, userId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerBootstrap] ReleaseSlot failed for user {userId}: {e.Message}");
            }
        }

        private static void SpawnPlayer(GameObject playerTemplate, ulong clientId, int slotIndex)
        {
            var spawnPosition = SpawnPoints.Get(slotIndex);

            // Set the NetworkVariables' initial values before spawning, not after: the
            // initial spawn message then already carries the correct values instead of
            // needing a separate post-spawn replication update (also avoids NGO's harmless
            // but noisy "NetworkVariable is written to, but doesn't know its NetworkBehaviour
            // yet" warning). See PlayerController.CreateTemplate for the real fixes needed
            // to make a runtime-only prefab spawn correctly at all — GlobalObjectIdHash and
            // staying active, not spawn ordering.
            var instance = UnityEngine.Object.Instantiate(playerTemplate, spawnPosition, Quaternion.identity);
            instance.GetComponent<PlayerController>().ServerInitialize(slotIndex, spawnPosition);
            instance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
        }
    }
}
