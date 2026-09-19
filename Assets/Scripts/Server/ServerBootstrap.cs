using System;
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
        private CancellationTokenSource _heartbeatCts;

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
            playerTemplate.SetActive(false);

            var networkManager = GetComponent<NetworkManager>() ?? gameObject.AddComponent<NetworkManager>();
            var transport = GetComponent<UnityTransport>() ?? gameObject.AddComponent<UnityTransport>();

            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            // NGO hashes ConnectionApproval into its NetworkConfig compatibility check, so
            // the Phase 5 client MUST also set ConnectionApproval = true — otherwise the
            // connection fails silently with a generic disconnect before ConnectionApprovalCallback
            // ever runs (no server-side log at all, since it's rejected before reaching that code).
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.AddNetworkPrefab(playerTemplate);

            transport.SetConnectionData(config.AdvertiseHost, config.ListenPort, listenAddress: "0.0.0.0");

            var publicKey = await JwksClient.FetchPublicKeyAsync(
                $"{config.BackendUrl}/.well-known/jwks.json", config.TicketKeyId);
            var validator = new TicketValidator(publicKey, config.TicketIssuer, config.TicketAudience);

            var fleet = new FleetClient(config.BackendUrl, config.FleetApiKey);
            await fleet.RegisterAsync(config.AdvertiseHost, config.ListenPort, config.Capacity);

            var approval = new ConnectionApprovalHandler(validator, networkManager, fleet.SessionId, config.Capacity);
            networkManager.ConnectionApprovalCallback = approval.Approve;
            approval.ClientApproved += (clientId, userId, slotIndex) =>
                SpawnPlayer(networkManager, playerTemplate, clientId, slotIndex);

            if (!networkManager.StartServer())
            {
                Debug.LogError("[ServerBootstrap] StartServer() failed.");
                return;
            }

            Debug.Log($"[ServerBootstrap] Listening on 0.0.0.0:{config.ListenPort}, " +
                      $"advertising {config.AdvertiseHost}:{config.ListenPort}, session {fleet.SessionId}");

            _heartbeatCts = new CancellationTokenSource();
            _ = fleet.RunHeartbeatLoopAsync(
                TimeSpan.FromSeconds(10),
                () => networkManager.ConnectedClientsIds.Count,
                _heartbeatCts.Token);
        }

        private void OnApplicationQuit()
        {
            _heartbeatCts?.Cancel();
        }

        private static void SpawnPlayer(NetworkManager networkManager, GameObject playerTemplate, ulong clientId, int slotIndex)
        {
            var spawnPosition = SpawnPoints.Get(slotIndex);
            var networkObject = NetworkObject.InstantiateAndSpawn(
                playerTemplate, networkManager, ownerClientId: clientId, isPlayerObject: true, position: spawnPosition);

            networkObject.GetComponent<PlayerController>().ServerInitialize(slotIndex, spawnPosition);
        }
    }
}
