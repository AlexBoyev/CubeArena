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
        private const int PickupCount = 6;

        private CancellationTokenSource _heartbeatCts;
        private readonly Dictionary<ulong, Guid> _connectedUsers = new();
        private readonly Dictionary<ulong, (Guid UserId, int SlotIndex)> _approvedPendingConnect = new();
        private FleetClient _fleet;
        private NetworkManager _networkManager;
        private GameObject _pickupTemplate;
        private MatchManager _matchManager;

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
            _pickupTemplate = PickupController.CreateTemplate();
            var matchManagerTemplate = MatchManager.CreateTemplate();

            var networkManager = GetComponent<NetworkManager>() ?? gameObject.AddComponent<NetworkManager>();
            _networkManager = networkManager;
            var transport = GetComponent<UnityTransport>() ?? gameObject.AddComponent<UnityTransport>();
            // UTP's default is 30 seconds of inactivity before it declares a connection
            // dead — fine for a flaky-network hiccup, but far too long for the common case
            // here of a client process just disappearing (crash, force-kill, alt-F4)
            // without sending a clean disconnect. For half a minute the server (and every
            // other client) keeps rendering that player standing frozen in place, which is
            // exactly the recurring "ghost/stale player" complaint. 5s is still generous
            // for a real network blip but cleans up a dead client far faster.
            transport.DisconnectTimeoutMS = 5000;

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
            networkManager.AddNetworkPrefab(_pickupTemplate);
            networkManager.AddNetworkPrefab(matchManagerTemplate);

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

            var matchManagerInstance = UnityEngine.Object.Instantiate(matchManagerTemplate);
            matchManagerInstance.GetComponent<NetworkObject>().Spawn();
            _matchManager = matchManagerInstance.GetComponent<MatchManager>();
            _matchManager.MatchEnded += OnMatchEnded;

            SpawnPickups();
        }

        private void OnApplicationQuit()
        {
            _heartbeatCts?.Cancel();
        }

        private void SpawnPickups()
        {
            for (var i = 0; i < PickupCount; i++)
            {
                var instance = UnityEngine.Object.Instantiate(_pickupTemplate);
                instance.GetComponent<PickupController>().ServerInitialize(PickupController.GetRandomPosition());
                instance.GetComponent<NetworkObject>().Spawn();
            }
        }

        // A leaving player only ever affects their own slot (OnClientDisconnectCallback
        // above) — the server, and everyone else's session, keeps running regardless.
        // This is the other half: a hard cap (MatchManager.MatchDurationSeconds) on how
        // long a match can run before everyone's sent back to character select and a
        // fresh round starts, rather than one match running forever. The server process
        // itself is untouched either way — quick play can match players into it again
        // immediately after.
        private void OnMatchEnded()
        {
            var reason = BuildMatchEndReason();
            var connectedClientIds = new List<ulong>(_networkManager.ConnectedClientsIds);
            foreach (var clientId in connectedClientIds)
            {
                _networkManager.DisconnectClient(clientId, reason);
            }

            Debug.Log($"[ServerBootstrap] {reason} ({connectedClientIds.Count} client(s) disconnected.)");

            foreach (var player in new List<PlayerController>(PlayerController.ActiveServerPlayers))
            {
                player.ResetScore();
            }

            _matchManager.ResetForNewRound();
        }

        private static string BuildMatchEndReason()
        {
            const string suffix = " — quick play again to start a new match.";

            PlayerController winner = null;
            var highScore = 0;
            var tiedWithHighScore = false;
            foreach (var player in PlayerController.ActiveServerPlayers)
            {
                if (player.Score > highScore)
                {
                    highScore = player.Score;
                    winner = player;
                    tiedWithHighScore = false;
                }
                else if (player.Score == highScore && highScore > 0)
                {
                    tiedWithHighScore = true;
                }
            }

            if (winner == null)
            {
                return "Match ended (5 minute time limit) — nobody scored." + suffix;
            }

            if (tiedWithHighScore)
            {
                return $"Match ended (5 minute time limit) — tied at {highScore} point(s)!" + suffix;
            }

            return $"Match ended (5 minute time limit) — Slot {winner.SlotIndex} wins with {highScore} point(s)!" + suffix;
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
