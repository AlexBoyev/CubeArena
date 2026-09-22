using System;
using System.Collections.Generic;
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

        // Server-only: ServerBootstrap subscribes to end the match early via player vote
        // (see CastEndMatchVoteServerRpc) — a distinct event/disconnect reason from
        // MatchEnded so the client can tell "the 5-minute clock ran out" (offers a rejoin
        // into the fresh lobby) apart from "everyone agreed to stop" (sends everyone to
        // the main menu instead).
        public event Action VoteEndTriggered;

        private readonly NetworkVariable<float> _timeRemaining = new(
            MatchDurationSeconds, writePerm: NetworkVariableWritePermission.Server);

        // A lobby gate: players connect and spawn into the arena immediately (unchanged),
        // but the clock doesn't start until whoever's hosting explicitly starts it — so a
        // friend still connecting, or router/port-forwarding trouble on their end, doesn't
        // silently burn match time before everyone's actually in.
        private readonly NetworkVariable<bool> _matchStarted = new(
            writePerm: NetworkVariableWritePermission.Server);

        // Server-only: who's voted to end the match early this round — a plain field, not
        // a NetworkVariable, since only the count (below) needs replicating, not each
        // individual voter's identity.
        private readonly HashSet<ulong> _endMatchVotes = new();

        private readonly NetworkVariable<int> _endMatchVoteCount = new(
            writePerm: NetworkVariableWritePermission.Server);

        // The team's shared banked-loot total (Milestone 3) — master prompt section 11's
        // "Scoreboard -> repurpose as the team loot total." A new NetworkVariable rather
        // than a literal repurpose of PlayerController's per-player _score (Cube Arena's
        // old competitive-score field, left in place but no longer the HUD's main
        // readout — see docs/DECISIONS.md): a per-player score and a shared team total
        // are different shapes of data, and MatchManager is already the one
        // match-wide-state singleton every client reads, so it's the natural home for it.
        private readonly NetworkVariable<int> _bankedLootTotal = new(
            writePerm: NetworkVariableWritePermission.Server);

        private bool _hasEnded;

        public float TimeRemaining => _timeRemaining.Value;
        public bool MatchStarted => _matchStarted.Value;
        public int EndMatchVoteCount => _endMatchVoteCount.Value;
        public int BankedLootTotal => _bankedLootTotal.Value;

        // Server-only: called by LootItem when an item's carriers bring it within
        // BankRadius of the mousehole.
        public void AddBankedLoot(int amount) => _bankedLootTotal.Value += amount;

        public override void OnNetworkSpawn()
        {
            Instance = this;
        }

        private void Update()
        {
            if (!IsServer || _hasEnded || !_matchStarted.Value)
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

        // Client-callable: ClientBootstrap's lobby "Start Match" button calls this — only
        // shown to the host in the first place, but RequestStartMatchServerRpc re-checks
        // who's actually host server-side too, so a non-host client can't start the match
        // just by calling this method directly instead of clicking the (hidden-from-them)
        // button.
        public void RequestStart() => RequestStartMatchServerRpc();

        [ServerRpc(RequireOwnership = false)]
        private void RequestStartMatchServerRpc(ServerRpcParams rpcParams = default)
        {
            if (_matchStarted.Value || NetworkManager == null)
            {
                return;
            }

            if (rpcParams.Receive.SenderClientId != LowestConnectedClientId())
            {
                return; // not the host — ignore
            }

            _matchStarted.Value = true;

            // Anyone who wandered around and grabbed a pickup while waiting in the lobby
            // shouldn't keep that as a head start once the match officially begins.
            foreach (var player in PlayerController.ActiveServerPlayers)
            {
                player.ResetScore();
            }
        }

        // Client-callable: the pause menu's "Vote to End Match" button, and the vote
        // popup's own "Vote Yes" button, both call this. Idempotent (a HashSet, not a
        // counter) so clicking it more than once can't inflate the tally.
        public void CastEndMatchVote() => CastEndMatchVoteServerRpc();

        [ServerRpc(RequireOwnership = false)]
        private void CastEndMatchVoteServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!_matchStarted.Value || _hasEnded || NetworkManager == null)
            {
                return; // nothing to vote to end
            }

            _endMatchVotes.Add(rpcParams.Receive.SenderClientId);
            _endMatchVoteCount.Value = _endMatchVotes.Count;

            // Majority of currently connected players, not a fixed threshold — fair
            // regardless of whether all 4 slots are filled.
            if (_endMatchVotes.Count * 2 > NetworkManager.ConnectedClientsIds.Count)
            {
                _hasEnded = true;
                VoteEndTriggered?.Invoke();
            }
        }

        // "Host" = whoever's been connected longest (the lowest client id), recomputed on
        // demand rather than cached — good enough for a private game among friends without
        // needing any extra state if the original host happens to leave before starting.
        private ulong LowestConnectedClientId()
        {
            var min = ulong.MaxValue;
            foreach (var id in NetworkManager.ConnectedClientsIds)
            {
                if (id < min)
                {
                    min = id;
                }
            }

            return min;
        }

        // Server-only: called by ServerBootstrap after handling MatchEnded, once everyone
        // has been disconnected, so the next round starts with a fresh clock and goes
        // through the lobby again rather than auto-starting.
        public void ResetForNewRound()
        {
            _timeRemaining.Value = MatchDurationSeconds;
            _matchStarted.Value = false;
            _hasEnded = false;
            _endMatchVotes.Clear();
            _endMatchVoteCount.Value = 0;
            _bankedLootTotal.Value = 0;
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
