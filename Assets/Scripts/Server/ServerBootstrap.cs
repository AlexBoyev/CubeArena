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

        // A disconnect (deliberate Leave Match, a crash, a timeout — anything) destroys
        // that player's PlayerController entirely, along with its score. Without this,
        // rejoining a still-running match (the existing confirm/release/rejoin grace
        // period already lets you reconnect into the same session) respawned a fresh
        // PlayerController at 0, which read as "leaving and rejoining resets your score"
        // even though the match itself hadn't reset. Mirrored live via each player's
        // ScoreChanged event, keyed by userId (survives past any one connection), and
        // cleared in OnMatchEnded so a rejoin after a new round starts doesn't wrongly
        // restore a score from the round before.
        private readonly Dictionary<Guid, int> _savedScores = new();
        private FleetClient _fleet;
        private NetworkManager _networkManager;
        private GameObject _pickupTemplate;
        private GameObject _crateTemplate;
        private int _crateCount;
        private MatchManager _matchManager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
#if UNITY_SERVER
            // Real builds only ever include BootConfig.BootSceneName (see
            // Editor/BuildScript.cs), so this check is a no-op there — it only
            // matters in the Editor, where opening any other scene (a preview/
            // test scene) must play normally instead of also standing up the
            // whole dedicated server on top of it.
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != BootConfig.BootSceneName)
            {
                return;
            }

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

            KitchenBuilder.Build();

            var playerTemplate = PlayerController.CreateTemplate();
            _pickupTemplate = PickupController.CreateTemplate();
            _crateTemplate = CrateController.CreateTemplate();
            var matchManagerTemplate = MatchManager.CreateTemplate();
            _crateCount = config.CrateCount;

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
            networkManager.AddNetworkPrefab(_crateTemplate);
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
                var player = SpawnPlayer(playerTemplate, clientId, info.SlotIndex);
                player.ScoreChanged += newScore => _savedScores[info.UserId] = newScore;
                if (_savedScores.TryGetValue(info.UserId, out var savedScore))
                {
                    player.SetScore(savedScore);
                }

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

            // See BandwidthLogger — this reports the server's aggregate total across every
            // connected client, not a per-client figure (that's what each client's own log
            // is for). Runs unconditionally; harmless at normal gameplay traffic levels.
            StartCoroutine(BandwidthLogger.LogPeriodically("server", networkManager, 5f));

            _heartbeatCts = new CancellationTokenSource();
            _ = _fleet.RunHeartbeatLoopAsync(
                TimeSpan.FromSeconds(10),
                () => networkManager.ConnectedClientsIds.Count,
                _heartbeatCts.Token);

            var matchManagerInstance = UnityEngine.Object.Instantiate(matchManagerTemplate);
            matchManagerInstance.GetComponent<NetworkObject>().Spawn();
            _matchManager = matchManagerInstance.GetComponent<MatchManager>();
            _matchManager.MatchEnded += () => EndMatch(BuildMatchEndReason());
            _matchManager.VoteEndTriggered += () => EndMatch(BuildVoteEndReason());

            // Gold pickups and crates are Cube Arena gameplay this run is replacing
            // (docs/GAME_DESIGN.md section 11) - not spawned in the kitchen. Left
            // callable (not deleted) rather than ripped out: PickupController/
            // CrateController's underlying tech (network prefab template pattern,
            // CrateController's NetworkTransform/NetworkRigidbody physics) is exactly
            // what Milestone 3's loot redesign needs to adapt, per docs/DECISIONS.md's
            // multi-carrier-loot entry. Full removal of the dead pickup-scoring
            // gameplay itself (not just disabling its spawn) is deferred to that pass,
            // where the replacement (bankable loot) actually exists.
            // SpawnPickups();
            // SpawnCrates();
        }

        private void OnApplicationQuit()
        {
            _heartbeatCts?.Cancel();
        }

        // Same shape as SpawnPickups — one shared template/hash, Instantiate +
        // ServerInitialize + Spawn in a loop. _crateCount defaults to a gameplay-sane
        // number but is set to 30 for the physics-bandwidth load test (see
        // docs/NETCODE.md) via CUBEARENA_CRATE_COUNT, no rebuild needed.
        private void SpawnCrates()
        {
            for (var i = 0; i < _crateCount; i++)
            {
                var instance = UnityEngine.Object.Instantiate(_crateTemplate);
                instance.GetComponent<CrateController>().ServerInitialize(PickupController.GetRandomPosition());
                instance.GetComponent<NetworkObject>().Spawn();
            }
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
        // This handles both ways a round actually ends: the clock running out
        // (MatchManager.MatchEnded) or a player-vote majority (MatchManager.
        // VoteEndTriggered) — same cleanup either way, just a different reason string
        // (see BuildMatchEndReason/BuildVoteEndReason), which is what tells the client
        // whether to offer a rejoin (timer) or send everyone to the main menu (vote — see
        // ClientBootstrap.OnDisconnected). The server process itself is untouched either
        // way — quick play can match players into it again immediately after.
        private void EndMatch(string reason)
        {
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

            // A rejoin into the round that's about to start fresh should never inherit a
            // score from the round that just ended.
            _savedScores.Clear();

            _matchManager.ResetForNewRound();
        }

        private static string BuildMatchEndReason() =>
            FormatMatchResult("Match ended (5 minute time limit)") + " — quick play again to start a new match.";

        private static string BuildVoteEndReason() =>
            FormatMatchResult("Vote ended the match");

        private static string FormatMatchResult(string prefix)
        {
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
                return $"{prefix} — nobody scored.";
            }

            if (tiedWithHighScore)
            {
                return $"{prefix} — tied at {highScore} point(s)!";
            }

            var winnerName = string.IsNullOrEmpty(winner.DisplayName) ? PlayerColors.GetName(winner.SlotIndex) : winner.DisplayName;
            return $"{prefix} — {winnerName} wins with {highScore} point(s)!";
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

        private static PlayerController SpawnPlayer(GameObject playerTemplate, ulong clientId, int slotIndex)
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
            var player = instance.GetComponent<PlayerController>();
            player.ServerInitialize(slotIndex, spawnPosition);
            instance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
            return player;
        }
    }
}
