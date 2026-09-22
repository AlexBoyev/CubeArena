using System;
using System.Collections.Generic;
using System.Reflection;
using CubeArena.Shared.Tuning;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CubeArena.Shared
{
    // Server-authoritative movement per section 3.3/6: the client sends input intent
    // only; the server simulates at a fixed 30Hz tick and is the sole writer of the
    // replicated position. See docs/NETCODE.md for the prediction/reconciliation design.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour
    {
        // Three tiers, not just crouch/stand: crawl is lower and slower than crouch,
        // and the crawl tunnels (ArenaBuilder.BuildCrawlTunnel) are built low enough
        // that only PlayerPose.Crawling fits under them — crouching alone isn't enough,
        // by design, so crawl has an actual reason to exist as its own input.
        public enum PlayerPose : byte
        {
            Standing,
            Crouching,
            Crawling,
        }

        // Raised on the owning client only, once its own player object has spawned —
        // the client bootstrap uses this to attach the camera and show the HUD colour.
        public static event Action<PlayerController> LocalPlayerSpawned;

        // Server-side only: every currently-spawned player, kept up to date in
        // OnNetworkSpawn/OnNetworkDespawn. PickupController scans this to find who's
        // standing close enough to collect it, and ServerBootstrap scans it to read
        // everyone's score when a match ends.
        public static readonly List<PlayerController> ActiveServerPlayers = new();


        private const float ReconcileSnapThresholdSqr = 4f; // snap if off by more than 2m (e.g. on spawn)
        private const float ReconcileBlendSpeed = 5f;
        private const float StandingControllerHeight = 1.9f;
        private const float CrouchControllerHeight = 1.1f;
        private const float CrawlControllerHeight = 0.6f; // prone-low, for the crawl tunnels specifically — crouch height doesn't fit under them
        private const float WalkDetectSpeed = 0.15f; // horizontal m/s above which AnimateVisuals treats the player as walking

        private readonly NetworkVariable<Vector3> _serverPosition = new(
            writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _slotIndex = new(
            writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<PlayerPose> _pose = new(
            writePerm: NetworkVariableWritePermission.Server);

        // What pose the CharacterController actually ended up at, as opposed to _pose
        // (what the player is asking for) — they differ exactly when ApplyPoseToController
        // refuses to grow into a taller pose because there's no headroom yet. Remote
        // viewers' AnimateVisuals reads this (the owner reads its own local prediction
        // instead — see _predictedEffectivePose) so a player who releases crouch while
        // still under a low roof keeps looking crouched, instead of the model popping up
        // to standing height and visibly poking through the roof while the actual collider
        // (correctly) stays put.
        private readonly NetworkVariable<PlayerPose> _effectivePose = new(
            writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _sprintHeld = new(
            writePerm: NetworkVariableWritePermission.Server);

        // Server-authoritative: whether this tick's movement used climb mode instead of
        // normal grounded movement. See the "Climbing" section below.
        private readonly NetworkVariable<bool> _isClimbing = new(
            writePerm: NetworkVariableWritePermission.Server);

        // Server-authoritative: whether this player currently has a grip on a LootItem —
        // read client-side purely so the owner's own E-key handler knows whether to send
        // a grip or release request (see RequestToggleGripServerRpc). The actual
        // authoritative record of who's gripping what lives on LootItem itself
        // (_grippedLootItemServer below, server-only); this bool is just the client-
        // visible mirror of that.
        private readonly NetworkVariable<bool> _isGrippingLoot = new(
            writePerm: NetworkVariableWritePermission.Server);

        // Server-authoritative sprint resource — only SimulateMovement (server) ever
        // writes it; the owner's own prediction only reads it (to decide whether it's
        // allowed to predict a sprint speed boost), never spends it locally, so there's
        // nothing to reconcile.
        private readonly NetworkVariable<float> _stamina = new(
            MovementConstants.StaminaMax, writePerm: NetworkVariableWritePermission.Server);

        // Hysteresis latch on top of _stamina: true from the moment it hits 0 until it
        // recovers to MovementConstants.StaminaResumeFraction, gating sprint the whole
        // time it's true regardless of _stamina ticking back above 0 in between. Without
        // this, drain (tick N) and regen (tick N+1) fighting right at the 0 boundary let
        // it bounce between ~0 and a fraction of a regen-tick forever, which read as
        // "infinite sprint at 0-1%" — a real exploit, not just a display glitch.
        private readonly NetworkVariable<bool> _sprintExhausted = new(
            writePerm: NetworkVariableWritePermission.Server);

        // Placeholders for future combat/abilities — nothing currently damages a player or
        // spends Mana, so both always read as full. Wired through as real replicated
        // values (not just hardcoded UI constants) so whatever adds that gameplay later
        // only needs to write here, not touch the HUD at all.
        public const float MaxHealth = 100f;
        public const float MaxMana = 100f;

        private readonly NetworkVariable<float> _health = new(
            MaxHealth, writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _mana = new(
            MaxMana, writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _score = new(
            writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString32Bytes> _displayName = new(
            writePerm: NetworkVariableWritePermission.Server);

        // Degrees around Y — the character always faces wherever CameraFollow's
        // mouse-look currently points (see ReadAndSendInput/CameraRelativeXZ), same
        // convention as most third-person games (WASD is relative to facing, and
        // facing is the camera's own yaw).
        private readonly NetworkVariable<float> _facingYaw = new(
            writePerm: NetworkVariableWritePermission.Server);

        // Milestone 2 diagnostic - see the periodic [Pos] log in SimulateMovement.
        private float _lastPositionLogTime;

        // Loaded once, shared by every PlayerController instance (client and server both
        // load their own copy of the same asset — see ClimbSettings.cs). Null-checked at
        // each use site with a hardcoded fallback rather than assumed non-null, in case
        // the Resources asset is ever missing (e.g. a fresh checkout before
        // ClimbSettingsAssetCreator has been run).
        private static ClimbSettings _climbSettings;

        // Same loading convention as _climbSettings - see its comment.
        private static ThiefAnimationSettings _thiefAnimSettings;

        // Same loading convention as _climbSettings - see its comment.
        private static LootSettings _lootSettings;

        // Server-only: the LootItem this player currently has a grip on, or null. The
        // authoritative record (LootItem itself only ever learns about grips through
        // ServerAddGripper/ServerRemoveGripper) — this is just this player's own side of
        // that relationship, kept so RequestToggleGripServerRpc and disconnect cleanup
        // (OnNetworkDespawn) don't need to scan LootItem.ActiveServerLootItems to find it.
        private LootItem _grippedLootItemServer;

        private CharacterController _characterController;
        private Renderer[] _renderers;
        private Vector2 _lastSentInput;
        private float _lastSentYaw;
        private Vector2 _currentInput; // server-side: latest input received from the owner
        private float _verticalVelocity; // server-side: jump/gravity state
        private bool _jumpRequested; // server-side: set by JumpServerRpc, consumed next tick
        private float _predictedVerticalVelocity; // owner-client-side: local jump/gravity prediction
        private bool _predictedJumpRequested; // owner-client-side: consumed in PredictAndReconcile
        private PlayerPose _lastSentPose; // owner-client-side: last-sent state, for change detection
        private bool _lastSentSprint; // owner-client-side: last-sent state, for change detection
        private PlayerPose _predictedEffectivePose = PlayerPose.Standing; // owner-client-side: this frame's local clearance-check result, see _effectivePose
        private bool _inputPaused; // owner-client-side: see SetInputPaused

        // Purely cosmetic (client-only — see AnimateVisuals/UpdateLocomotionAnimation).
        private Transform _visual;
        private Animator _animator;
        private Vector3 _lastVisualPosition;

        // Locomotion-animation state, all client-only (see UpdateLocomotionAnimation).
        private bool _wasAirborne;
        private float _airborneElapsed;
        private float _landStateElapsed = -1f; // negative = not currently in the post-land hold
        private string _currentAnimState;

        // Nameplate: a sibling of Visual (not a child of it) so it doesn't shrink/move
        // with the crouch squash — see CreateTemplate.
        private Transform _nameplate;
        private Text _nameplateText;

        public int SlotIndex => _slotIndex.Value;
        public int Score => _score.Value;
        public float Stamina => _stamina.Value;
        public float Health => _health.Value;
        public float Mana => _mana.Value;
        public string DisplayName => _displayName.Value.ToString();

        // Server-only: ServerBootstrap listens for this to mirror each player's score
        // into a userId-keyed dictionary that survives past this NetworkObject's own
        // lifetime — needed because a disconnect (including a deliberate Leave Match)
        // destroys this GameObject entirely, and without something outside it
        // remembering the score, rejoining a still-running match respawned a fresh
        // PlayerController at 0 even though the match itself hadn't reset. See
        // ServerBootstrap's _savedScores.
        public event Action<int> ScoreChanged;

        private void Awake()
        {
            if (_climbSettings == null)
            {
                _climbSettings = Resources.Load<ClimbSettings>("ClimbSettings");
            }

            if (_thiefAnimSettings == null)
            {
                _thiefAnimSettings = Resources.Load<ThiefAnimationSettings>("ThiefAnimationSettings");
            }

            if (_lootSettings == null)
            {
                _lootSettings = Resources.Load<LootSettings>("LootSettings");
            }

            _characterController = GetComponent<CharacterController>();
            _renderers = GetComponentsInChildren<Renderer>();

            _visual = transform.Find("Visual");
            if (_visual != null)
            {
                _animator = _visual.GetComponentInChildren<Animator>();
            }

            _nameplate = transform.Find("Nameplate");
            _nameplateText = _nameplate != null ? _nameplate.GetComponentInChildren<Text>() : null;

            _lastVisualPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            ApplyColor(_slotIndex.Value);
            _slotIndex.OnValueChanged += (_, newValue) => ApplyColor(newValue);

            ApplyDisplayName(_displayName.Value);
            _displayName.OnValueChanged += (_, newValue) => ApplyDisplayName(newValue);

            if (IsServer)
            {
                ActiveServerPlayers.Add(this);
                _score.OnValueChanged += (_, newValue) => ScoreChanged?.Invoke(newValue);
            }

            if (!IsServer)
            {
                transform.position = _serverPosition.Value;
            }

            if (IsOwner && !IsServer)
            {
                LocalPlayerSpawned?.Invoke(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                // A carrier disconnecting mid-carry drops their grip cleanly (master
                // prompt section 7) — LootItem.FixedUpdate just recomputes its average
                // over whoever's left next tick, no special-casing needed there.
                if (_grippedLootItemServer != null)
                {
                    _grippedLootItemServer.ServerRemoveGripper(OwnerClientId);
                    _grippedLootItemServer = null;
                }

                ActiveServerPlayers.Remove(this);
            }
        }

        // Server-only: called by LootItem when this player's grip ends for a reason
        // LootItem itself initiated (banking, or defensive despawn cleanup) rather than
        // this player releasing it themselves — keeps _grippedLootItemServer/
        // _isGrippingLoot in sync either way, so a player whose item just got banked
        // sees their own next E-press try a fresh grip rather than a stale release.
        public void ServerClearGrip()
        {
            _grippedLootItemServer = null;
            _isGrippingLoot.Value = false;
        }

        // Server-only: called by PickupController when this player collects one.
        public void AddScore(int amount)
        {
            _score.Value += amount;
        }

        // Server-only: called by ServerBootstrap when a new match round starts.
        public void ResetScore()
        {
            _score.Value = 0;
        }

        // Server-only: called by ServerBootstrap right after spawning a rejoining
        // player, to restore whatever score they had before they disconnected (see
        // ScoreChanged/_savedScores) instead of leaving them at the fresh-spawn default
        // of 0 mid-match.
        public void SetScore(int score)
        {
            _score.Value = score;
        }

        // Owner-client-side: called by ClientBootstrap once, right after this player's
        // own object spawns, to publish the name chosen at Character Select — nothing
        // sends it anywhere before that (see docs/ROADMAP.md's Phase 5 notes on
        // DisplayName previously being purely cosmetic/local).
        public void SubmitDisplayName(string name) => SetDisplayNameServerRpc(name);

        // Owner-client-side: Escape's local pause (ClientBootstrap) calls this — it only
        // ever stops *this* client from reading/sending new WASD input, exactly the
        // "local pause, my player only" the multiplayer session itself can't do (other
        // players, and the server simulation, are completely unaffected). Explicitly
        // sends one zero-movement update on pausing so the character doesn't keep
        // sliding in whatever direction was held when Escape was pressed.
        public void SetInputPaused(bool paused)
        {
            _inputPaused = paused;
            if (paused && _lastSentInput != Vector2.zero)
            {
                _lastSentInput = Vector2.zero;
                SubmitInputServerRpc(Vector2.zero, _lastSentYaw);
            }
        }

        [ServerRpc]
        private void SetDisplayNameServerRpc(FixedString32Bytes name)
        {
            _displayName.Value = name;
        }

        private void ApplyDisplayName(FixedString32Bytes displayName)
        {
            if (_nameplateText != null)
            {
                _nameplateText.text = displayName.ToString();
            }
        }

        // Server-only: called by ServerBootstrap's spawn logic, before the object is
        // actually spawned on the network — see ServerBootstrap.SpawnPlayer.
        public void ServerInitialize(int slotIndex, Vector3 spawnPosition)
        {
            _slotIndex.Value = slotIndex;
            _serverPosition.Value = spawnPosition;
            transform.position = spawnPosition;
        }

        private void Update()
        {
            // The actual cause of "white person standing in the middle of the map": every
            // client keeps a local, never-spawned copy of this prefab around (see
            // CreateTemplate's comment — ConnectToGameServer builds one purely so NGO has
            // something to register), parked far below the arena at
            // TemplateParkPosition. IsOwner/IsServer/IsSpawned all default to false on a
            // NetworkBehaviour that's never been through NGO's spawn flow, so without this
            // guard the "else if (!IsServer)" branch below ran on it too, Lerping it every
            // frame toward _serverPosition.Value's C# default (Vector3.zero — the exact
            // center of the arena) and leaving it sitting there indefinitely, uncolored
            // (ApplyColor only ever runs from OnNetworkSpawn, which this object never
            // gets) — a plain, default-material humanoid parked at the origin forever, not
            // a stale player from anyone's disconnect at all.
            if (!IsSpawned)
            {
                return;
            }

            if (IsOwner && !IsServer)
            {
                if (!_inputPaused)
                {
                    ReadAndSendInput();
                }

                PredictAndReconcile();
            }
            else if (!IsServer)
            {
                // Remote players: simple interpolation toward the authoritative position/facing.
                transform.position = Vector3.Lerp(transform.position, _serverPosition.Value, Time.deltaTime * 10f);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, _facingYaw.Value, 0), Time.deltaTime * 10f);
            }

            if (!IsServer)
            {
                AnimateVisuals();
            }
        }

        private void FixedUpdate()
        {
            if (IsServer)
            {
                SimulateMovement(Time.fixedDeltaTime);
            }
        }

        // Set once, client-side, from ClientConfig at startup — when true, ReadAndSendInput
        // drives itself via RunBotBehavior instead of reading real keyboard/mouse input.
        // Exists purely for the 30-crate bandwidth load test (docs/NETCODE.md): it lets
        // several client processes generate realistic movement/push/grab/throw traffic
        // without needing synthetic OS-level input injection, which risks landing on the
        // wrong window if a real client happens to be focused at the same time.
        public static bool BotModeEnabled;

        // Bot mode only: which scripted routine RunBotBehavior runs — see
        // ClientConfig.BotTestMode's declaration for the full explanation. "" (default,
        // Milestone 3's coin-test routine), "lootdescent" (Milestone 4's shove-descent
        // verification routine), or "banktest" (Milestone 4's chair-climb + new-item
        // grip/carry/bank verification routine — both share RunLootDescentTestBotBehavior's
        // climb-to-table-item phases, diverging only after the grip: shove vs. carry home).
        public static string BotTestMode = "";

        private void ReadAndSendInput()
        {
            if (BotModeEnabled)
            {
                RunBotBehavior();
                return;
            }

            var keyboard = Keyboard.current;
            var input = Vector2.zero;
            if (keyboard != null)
            {
                var x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                var y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                input = new Vector2(x, y);
                if (input.sqrMagnitude > 1f)
                {
                    input.Normalize();
                }

                if (keyboard.spaceKey.wasPressedThisFrame)
                {
                    _predictedJumpRequested = true; // consumed locally in PredictAndReconcile
                    JumpServerRpc();
                }

                // C (crawl) takes priority over Ctrl (crouch) if both are somehow held —
                // crawl is the lower/slower of the two, so it's always the "safer" choice.
                var desiredPose = keyboard.cKey.isPressed
                    ? PlayerPose.Crawling
                    : keyboard.leftCtrlKey.isPressed
                        ? PlayerPose.Crouching
                        : PlayerPose.Standing;
                if (desiredPose != _lastSentPose)
                {
                    _lastSentPose = desiredPose;
                    SetPoseServerRpc(desiredPose);
                }

                var sprintHeld = keyboard.leftShiftKey.isPressed;
                if (sprintHeld != _lastSentSprint)
                {
                    _lastSentSprint = sprintHeld;
                    SetSprintServerRpc(sprintHeld);
                }

                // Same key grips and releases — the server (not this input handler)
                // decides whether a grip request succeeds and toggles based on whether
                // this player already has one, via _isGrippingLoot's replicated value
                // (see RequestToggleGripServerRpc). No throw/velocity computation needed
                // any more — loot is only ever carried, never thrown (unlike the old
                // CrateController crates).
                if (keyboard.eKey.wasPressedThisFrame)
                {
                    RequestToggleGripServerRpc();
                }

                // Milestone 4's "shove it off the edge" descent method (section 6) — a
                // distinct, deliberate action from the gentle E-release, only meaningful
                // while already gripping something (RequestShoveLootServerRpc no-ops
                // otherwise). Fast/instant and risky, unlike carrying it down the chair
                // or lowering it down the tablecloth (both just the ordinary grip + climb
                // path — see LootItem's gripper-relative carry height, docs/DECISIONS.md).
                if (keyboard.fKey.wasPressedThisFrame)
                {
                    RequestShoveLootServerRpc();
                }
            }

            // Resolved to a camera-relative world-space direction here (client-side, where
            // Camera.main is known) rather than on the server, so WASD moves relative to
            // wherever CameraFollow's mouse-look is currently pointed. The server stays
            // camera-agnostic — it just simulates whatever world-space direction arrives,
            // exactly as before this was raw WASD-as-world-axes. Since that direction now
            // changes continuously while the camera turns (not just when keys change), the
            // change-detection below sends far more often while turning-and-moving at once
            // — acceptable bandwidth for this prototype's scale.
            var worldInput = CameraRelativeXZ(input);

            // The model itself never rotated before this — only the camera orbited
            // around a fixed-facing character, which looked like the world spun around
            // them rather than them turning. Facing always tracks the camera's yaw (not
            // just while moving), same convention as most third-person games.
            var facingYaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : transform.eulerAngles.y;
            CommitWorldInputAndFacing(worldInput, facingYaw);
        }

        // Shared by both the real-keyboard path above and RunBotBehavior below: predicts
        // facing locally, and sends a new SubmitInputServerRpc only when the world-space
        // input or facing actually changed.
        private void CommitWorldInputAndFacing(Vector2 worldInput, float facingYaw)
        {
            transform.rotation = Quaternion.Euler(0, facingYaw, 0); // instant local prediction — it's just mirroring our own camera (or, for a bot, its own facing decision), no reconciliation needed

            if (worldInput != _lastSentInput || !Mathf.Approximately(facingYaw, _lastSentYaw))
            {
                _lastSentInput = worldInput;
                _lastSentYaw = facingYaw;
                SubmitInputServerRpc(worldInput, facingYaw);
            }
        }

        // Owner-client-side only, only reached when BotModeEnabled — see its declaration.
        // Milestone 3's verification vehicle for "the coin test": walk to the nearest
        // ungripped loot item, grip it, then walk toward the mousehole while still
        // gripping (the item itself follows the average gripper position — see
        // LootItem.FixedUpdate — so simply moving the bot's own body toward the
        // mousehole while gripped is enough to drag/carry the item along; no separate
        // "carry" bot state needed). Once the item banks, LootItem.ServerClearGrip
        // resets _isGrippingLoot to false server-side, which this reads next tick to go
        // pick a new target — in practice, for M3's single coin, that just means
        // standing near the mousehole with nothing left to do.
        private LootItem _botTargetLoot;
        private float _botStateTimer;
        // Separate from _botStateTimer: without this, RunBotBehavior would call
        // RequestToggleGripServerRpc() on every single Update() while in range and not
        // yet gripping — Update() is uncapped in a -batchmode -nographics build and can
        // run tens of thousands of times/sec (see the original crate-bot version of this
        // same problem, docs/NETCODE.md). A toggle RPC spammed that fast would just
        // grip-then-immediately-release-then-immediately-grip every call, never settling
        // — this cooldown gives one request time to actually land and _isGrippingLoot to
        // replicate back before trying again.
        private float _botGripRequestCooldown;

        private void RunBotBehavior()
        {
            if (BotTestMode == "lootdescent" || BotTestMode == "banktest")
            {
                RunLootDescentTestBotBehavior();
                return;
            }

            _botStateTimer -= Time.deltaTime;
            _botGripRequestCooldown -= Time.deltaTime;

            if (_isGrippingLoot.Value)
            {
                // Already gripping something — walk it toward the mousehole. The item
                // follows the average of its grippers' positions on its own (server-
                // side), so this bot just needs to keep moving there like any other
                // destination.
                var toMousehole = KitchenBuilder.MouseholePosition - transform.position;
                toMousehole.y = 0f;
                if (toMousehole.sqrMagnitude < 0.01f)
                {
                    CommitWorldInputAndFacing(Vector2.zero, transform.eulerAngles.y);
                    return;
                }

                var mouseholeDir = toMousehole.normalized;
                var mouseholeYaw = Quaternion.LookRotation(mouseholeDir, Vector3.up).eulerAngles.y;
                CommitWorldInputAndFacing(new Vector2(mouseholeDir.x, mouseholeDir.z), mouseholeYaw);
                return;
            }

            if (_botTargetLoot == null || !_botTargetLoot.IsSpawned || _botStateTimer <= 0f)
            {
                _botTargetLoot = FindNearestVisibleLootItem();
                _botStateTimer = 10f; // give up and re-pick after this long regardless
            }

            if (_botTargetLoot == null)
            {
                // Nothing left to do (e.g. the one coin's already banked) — stand still
                // rather than wandering.
                CommitWorldInputAndFacing(Vector2.zero, transform.eulerAngles.y);
                return;
            }

            var toTarget = _botTargetLoot.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f)
            {
                CommitWorldInputAndFacing(Vector2.zero, transform.eulerAngles.y);
                return;
            }

            var direction = toTarget.normalized;
            var facingYaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;

            var gripRange = _lootSettings != null ? _lootSettings.GripRange : 2.5f;
            if (toTarget.magnitude <= gripRange * 0.8f)
            {
                if (_botGripRequestCooldown <= 0f)
                {
                    RequestToggleGripServerRpc();
                    _botGripRequestCooldown = 0.5f; // let the grip resolve before retrying
                }

                CommitWorldInputAndFacing(Vector2.zero, facingYaw);
                return;
            }

            // World-space input directly toward the target — no camera to resolve
            // relative to, since a bot has no camera.
            CommitWorldInputAndFacing(new Vector2(direction.x, direction.z), facingYaw);
        }

        // Owner-client-side only, only reached when BotModeEnabled and BotTestMode ==
        // "lootdescent" or "banktest" — Milestone 4's verification routines: climb to the
        // table (reusing the exact Milestone 2 chair route, still proximity-gated and
        // unchanged), grip a table-top loot item, then either shove it off the edge
        // ("lootdescent", exercising ServerShove's real-physics fall) or carry it back down
        // the same climb route and bank it at the mousehole ("banktest", exercising a new
        // loot item's grip/carry/bank end-to-end on real geometry — added after a live
        // default-mode run showed FindNearestVisibleLootItem preferring the M3 floor coin
        // over any table item for bots spawned at the actual spawn points, so the coin-test
        // routine alone couldn't exercise a new item without this explicit target). Both
        // confirmed via the server's [Climb]/[Loot]/[Bank] log lines.
        private enum LootDescentPhase
        {
            ToChair,
            ToItem, // climbs straight up the chair column to table height — see below
            AcrossTable, // walks from directly above the chair to the item's real XZ, at table height
            Grip,
            Shove,
            ToMousehole, // banktest only — carries the item back down the same climb route
            Done,
        }

        private LootDescentPhase _lootDescentPhase;
        private float _lootDescentActionCooldown;

        // Deliberately KitchenBuilder.SeatToTableClimbX, not the chair leg's own (further
        // out) X — see LootDescentTableTopArrivalPoint's comment. ComputeClimbMove's
        // forward-aligned dot product means this bot produces ~zero real lateral drift
        // once climbing starts (facing always tracks input by construction, so
        // Dot(input, right) ≈ 0 the whole time) — the *only* phase that can actually move
        // the bot sideways is this one, ordinary grounded walking before climbing engages.
        // Landing here first, already lined up with the climb column's own center, is what
        // keeps the entire subsequent straight-up climb solidly supported.
        private static Vector3 LootDescentChairWaypoint =>
            new(KitchenBuilder.SeatToTableClimbX, 0f, KitchenBuilder.TableCenter.z);

        // Directly above the SeatToTable_Climbable zone's own center (not the chair leg's
        // own X, which is 1.1 units further out — see docs/DECISIONS.md's "seam" entry for
        // why that distinction matters: climbing dead-center on the leg's X left the
        // character resting at the very edge of the zone above it, an unreliable sliver of
        // support) at table height — climbing straight up here (zero XZ drift) keeps the
        // bot solidly inside the SeatToTable_Climbable zone the whole way, avoiding a
        // separate real bug found via live testing: aiming the climb straight at an
        // off-center table item's full 3D position (e.g. WalletCoin1, 6 units off the
        // chair's own Z) makes ComputeClimbMove's forward-aligned dot product route nearly
        // all movement intent into vertical climb with ~zero lateral shift (since the bot's
        // facing already points toward that diagonal target, forward ≈ input), so the bot
        // never actually drifts toward the item while climbing — then the known cosmetic
        // leg→seat boundary flicker (docs/DECISIONS.md's "Climb zones" entry) drops
        // IsNearClimbable for a moment, normal gravity + normal horizontal walk-toward-
        // target movement immediately take over, and the bot walks off the climb column's
        // XZ before it can re-enter it — ending up back on the floor, not part-way up. A
        // real player naturally avoids this by climbing straight up first and walking
        // across the table afterward; the bot now does the same explicitly.
        private static Vector3 LootDescentTableTopArrivalPoint =>
            new(KitchenBuilder.SeatToTableClimbX, KitchenBuilder.TableTopHeight, LootDescentChairWaypoint.z);

        private void RunLootDescentTestBotBehavior()
        {
            _lootDescentActionCooldown -= Time.deltaTime;

            Vector3 target;
            switch (_lootDescentPhase)
            {
                case LootDescentPhase.ToChair:
                    target = LootDescentChairWaypoint;
                    break;
                case LootDescentPhase.ToItem:
                    // Straight up the chair column (zero XZ drift) — see
                    // LootDescentTableTopArrivalPoint's comment for why this must stay
                    // directly above the chair rather than aiming at the item's own XZ.
                    // IsNearClimbable engages automatically based on proximity to any
                    // Climbable collider, exactly like a real player (Milestone 2).
                    target = LootDescentTableTopArrivalPoint;
                    break;
                case LootDescentPhase.AcrossTable:
                    // Now at table height and off any Climbable zone — this is just
                    // ordinary horizontal walking across the table's own flat top
                    // collider, the same as walking on any other floor surface.
                    target = KitchenBuilder.WalletCoin1SpawnPosition;
                    break;
                case LootDescentPhase.Grip:
                    if (!_isGrippingLoot.Value)
                    {
                        if (_lootDescentActionCooldown <= 0f)
                        {
                            RequestToggleGripServerRpc();
                            _lootDescentActionCooldown = 0.5f;
                        }

                        CommitWorldInputAndFacing(Vector2.zero, transform.eulerAngles.y);
                        return;
                    }

                    _lootDescentPhase = BotTestMode == "banktest" ? LootDescentPhase.ToMousehole : LootDescentPhase.Shove;
                    return;
                case LootDescentPhase.Shove:
                    if (_lootDescentActionCooldown <= 0f)
                    {
                        RequestShoveLootServerRpc();
                        _lootDescentPhase = LootDescentPhase.Done;
                    }

                    CommitWorldInputAndFacing(Vector2.zero, transform.eulerAngles.y);
                    return;
                case LootDescentPhase.ToMousehole:
                    // No explicit "bank" action — LootItem itself banks on proximity while
                    // still gripped (the same mechanism the default coin-test routine
                    // relies on), so this phase just needs to keep walking toward the
                    // mousehole while still gripping.
                    target = KitchenBuilder.MouseholePosition;
                    break;
                default:
                    CommitWorldInputAndFacing(Vector2.zero, transform.eulerAngles.y);
                    return;
            }

            var toTarget = target - transform.position;
            var toTargetXZ = new Vector3(toTarget.x, 0f, toTarget.z);

            if (_lootDescentPhase == LootDescentPhase.ToChair && toTargetXZ.magnitude <= WaypointSwitchDistance)
            {
                _lootDescentPhase = LootDescentPhase.ToItem;
                return;
            }

            if (_lootDescentPhase == LootDescentPhase.ToItem && toTarget.magnitude <= WaypointSwitchDistance)
            {
                _lootDescentPhase = LootDescentPhase.AcrossTable;
                return;
            }

            if (_lootDescentPhase == LootDescentPhase.AcrossTable && toTarget.magnitude <= WaypointSwitchDistance)
            {
                _lootDescentPhase = LootDescentPhase.Grip;
                return;
            }

            if (_lootDescentPhase == LootDescentPhase.ToMousehole && toTargetXZ.magnitude <= WaypointSwitchDistance)
            {
                // Banking itself already happened server-side via LootItem's own proximity
                // check by the time distance closes this far, or is about to on the next
                // tick — either way, this phase's job is done.
                _lootDescentPhase = LootDescentPhase.Done;
                return;
            }

            if (toTargetXZ.sqrMagnitude < 0.01f)
            {
                CommitWorldInputAndFacing(Vector2.zero, transform.eulerAngles.y);
                return;
            }

            var direction = toTargetXZ.normalized;
            var facingYaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;
            CommitWorldInputAndFacing(new Vector2(direction.x, direction.z), facingYaw);
        }

        private const float WaypointSwitchDistance = 3f;

        private LootItem FindNearestVisibleLootItem()
        {
            var items = FindObjectsByType<LootItem>(FindObjectsSortMode.None);
            LootItem nearest = null;
            var nearestDistSqr = float.MaxValue;
            foreach (var item in items)
            {
                // Every client keeps one never-spawned LootItem template parked at
                // (0,-1000,0) purely so NGO has something to register as a network
                // prefab — see LootItem.CreateTemplate, same convention as
                // PlayerController's own template, and the exact IsSpawned pitfall
                // documented in docs/DECISIONS.md ("FindNearestVisibleCrate must check
                // IsSpawned") that this class fixes from the start rather than
                // rediscovering.
                if (!item.IsSpawned)
                {
                    continue;
                }

                var distSqr = (item.transform.position - transform.position).sqrMagnitude;
                if (distSqr < nearestDistSqr)
                {
                    nearestDistSqr = distSqr;
                    nearest = item;
                }
            }

            return nearest;
        }

        private static Vector2 CameraRelativeXZ(Vector2 input)
        {
            var cam = Camera.main;
            if (cam == null || input == Vector2.zero)
            {
                return input;
            }

            var forward = cam.transform.forward;
            forward.y = 0;
            forward.Normalize();
            var right = cam.transform.right;
            right.y = 0;
            right.Normalize();

            var world = forward * input.y + right * input.x;
            return new Vector2(world.x, world.z);
        }

        // Local prediction from the same input, immediately, for responsiveness — then a
        // soft continuous correction toward the server's last known truth. This is not a
        // full input-replay reconciliation (no input history buffer); see docs/NETCODE.md
        // for what that would add and why this simpler version was chosen instead.
        //
        // Vertical (jump/gravity) is predicted the same way, mirroring SimulateMovement's
        // logic — without this, the owner would only ever see their own jump once the
        // server's replicated position caught up, which reads as sluggish/late for
        // something as immediate as a jump.
        private void PredictAndReconcile()
        {
            _predictedEffectivePose = ApplyPoseToController(_pose.Value);

            // Sprint is gated on stamina but never spent here — _stamina is server-
            // authoritative (see SimulateMovement) and this is only a same-frame local
            // guess so the speed boost feels instant; the server's own gate is what
            // actually matters for fairness, and the position-reconcile below already
            // absorbs the odd mispredicted tick. Doesn't require actually moving — holding
            // Shift while standing still spends stamina too, same as SimulateMovement
            // below, so there's no "why didn't it drain" case where the bar just looks
            // stuck. Gated on _sprintExhausted (server-authoritative hysteresis), not a
            // raw _stamina > 0 check — see its declaration.
            var sprinting = _sprintHeld.Value && _predictedEffectivePose == PlayerPose.Standing && !_sprintExhausted.Value;
            var speedMultiplier = SpeedMultiplierFor(_predictedEffectivePose) * (sprinting ? MovementConstants.SprintSpeedMultiplier : 1f);

            // Frozen while the lobby's still up (MatchManager.Instance.MatchStarted is
            // false) — mirrors the same gate in SimulateMovement so the owner's own
            // prediction doesn't drift ahead of the server and then snap back. Gravity
            // still applies so the player stays grounded instead of floating.
            var matchActive = IsMatchActive();
            var predictedInput = matchActive ? _lastSentInput : Vector2.zero;
            if (!matchActive)
            {
                _predictedJumpRequested = false;
            }

            // Mirrors SimulateMovement's climb branch — see its comment. Predicted purely
            // locally (IsNearClimbable reads real colliders, same on both sides), same as
            // every other predicted movement here; the reconcile below still absorbs any
            // mismatch against the server's own climbing decision.
            var predictedClimbing = matchActive && _predictedEffectivePose == PlayerPose.Standing && IsNearClimbable();
            if (predictedClimbing)
            {
                _predictedVerticalVelocity = 0f;
                _characterController.Move(ComputeClimbMove(predictedInput, Time.deltaTime));
            }
            else
            {
                var grounded = _characterController.isGrounded;
                if (_predictedJumpRequested && grounded)
                {
                    _predictedVerticalVelocity = MovementConstants.JumpSpeed;
                    _predictedJumpRequested = false;
                }
                else if (grounded)
                {
                    if (_predictedVerticalVelocity < 0f)
                    {
                        _predictedVerticalVelocity = -2f;
                    }
                }
                else
                {
                    _predictedVerticalVelocity += MovementConstants.Gravity * Time.deltaTime;
                }

                var move = new Vector3(predictedInput.x, 0, predictedInput.y) * (MovementConstants.MoveSpeed * speedMultiplier * Time.deltaTime)
                           + Vector3.up * (_predictedVerticalVelocity * Time.deltaTime);
                _characterController.Move(move);
            }

            var error = _serverPosition.Value - transform.position;
            if (error.sqrMagnitude > ReconcileSnapThresholdSqr)
            {
                transform.position = _serverPosition.Value;
                _predictedVerticalVelocity = 0f; // avoid compounding stale predicted velocity across a hard snap
            }
            else
            {
                transform.position += error * Mathf.Clamp01(Time.deltaTime * ReconcileBlendSpeed);
            }
        }

        [ServerRpc]
        private void SubmitInputServerRpc(Vector2 input, float facingYaw)
        {
            // Reject/clamp inputs whose implied speed exceeds the allowed maximum (section 3.3).
            if (input.sqrMagnitude > 1.001f)
            {
                input = input.normalized;
            }

            _currentInput = input;
            _facingYaw.Value = facingYaw;
            transform.rotation = Quaternion.Euler(0, facingYaw, 0);
        }

        [ServerRpc]
        private void JumpServerRpc()
        {
            _jumpRequested = true;
        }

        [ServerRpc]
        private void SetPoseServerRpc(PlayerPose pose)
        {
            _pose.Value = pose;
        }

        [ServerRpc]
        private void SetSprintServerRpc(bool held)
        {
            _sprintHeld.Value = held;
        }

        // Default RequireOwnership=true is exactly right here (unlike e.g. MatchManager's
        // vote RPC) — a player can only ever request a grip through their own
        // PlayerController, which they own by definition. Single toggle RPC (unlike the
        // old CrateController's separate grab/throw pair) since loot has no throw —
        // press E to grip the nearest ungripped item in range, press it again to release
        // whatever this player is currently gripping.
        [ServerRpc]
        private void RequestToggleGripServerRpc()
        {
            if (_grippedLootItemServer != null)
            {
                _grippedLootItemServer.ServerRemoveGripper(OwnerClientId);
                _grippedLootItemServer = null;
                _isGrippingLoot.Value = false;
                return;
            }

            LootItem nearest = null;
            var gripRange = _lootSettings != null ? _lootSettings.GripRange : 2.5f;
            var nearestDistSqr = gripRange * gripRange;
            foreach (var item in LootItem.ActiveServerLootItems)
            {
                var toItem = item.transform.position - transform.position;
                var distSqr = toItem.sqrMagnitude;
                if (distSqr > nearestDistSqr)
                {
                    continue;
                }

                // Roughly in front of the player, not something behind them they'd have
                // no way of aiming away from. Same gate CrateController's grab used.
                if (Vector3.Dot(transform.forward, toItem.normalized) < 0.3f)
                {
                    continue;
                }

                nearestDistSqr = distSqr;
                nearest = item;
            }

            if (nearest == null)
            {
                return;
            }

            nearest.ServerAddGripper(OwnerClientId, this);
            _grippedLootItemServer = nearest;
            _isGrippingLoot.Value = true;
        }

        // Milestone 4's "shove it off the edge" descent method — no-ops unless this
        // player is already gripping something (a shove without a grip makes no sense;
        // grip first via E, then shove via F). Direction is the player's own facing so a
        // shove sends the item outward the way the player's actually facing at the table
        // edge, not just straight down.
        [ServerRpc]
        private void RequestShoveLootServerRpc()
        {
            if (_grippedLootItemServer == null)
            {
                return;
            }

            var item = _grippedLootItemServer;
            _grippedLootItemServer = null;
            _isGrippingLoot.Value = false;
            item.ServerShove(transform.forward);
        }

        private static float SpeedMultiplierFor(PlayerPose pose) => pose switch
        {
            PlayerPose.Crawling => MovementConstants.CrawlSpeedMultiplier,
            PlayerPose.Crouching => MovementConstants.CrouchSpeedMultiplier,
            _ => 1f,
        };

        private static float HeightFor(PlayerPose pose) => pose switch
        {
            PlayerPose.Crawling => CrawlControllerHeight,
            PlayerPose.Crouching => CrouchControllerHeight,
            _ => StandingControllerHeight,
        };

        private void SimulateMovement(float deltaTime)
        {
            var effectivePose = ApplyPoseToController(_pose.Value);
            _effectivePose.Value = effectivePose;

            // Hysteresis latch: once stamina is fully drained, sprint stays locked out
            // until it's recovered back up to StaminaResumeFraction, not just "> 0" — see
            // _sprintExhausted's declaration for why a plain > 0 check let sprint drain
            // and regen fight each other forever right at the 0 boundary.
            if (_stamina.Value <= 0f)
            {
                _sprintExhausted.Value = true;
            }
            else if (_stamina.Value >= MovementConstants.StaminaMax * MovementConstants.StaminaResumeFraction)
            {
                _sprintExhausted.Value = false;
            }

            // Sprint only while actually standing — holding Shift while crouched/crawling
            // drains nothing (deliberately doesn't require movement too: holding Shift
            // always spends stamina, so there's no silent no-op case that reads as "sprint
            // just doesn't work").
            var wantsSprint = _sprintHeld.Value && effectivePose == PlayerPose.Standing && !_sprintExhausted.Value;
            bool sprinting;
            if (wantsSprint)
            {
                _stamina.Value = Mathf.Max(0f, _stamina.Value - MovementConstants.StaminaDrainPerSecond * deltaTime);
                sprinting = true;
            }
            else
            {
                _stamina.Value = Mathf.Min(MovementConstants.StaminaMax, _stamina.Value + MovementConstants.StaminaRegenPerSecond * deltaTime);
                sprinting = false;
            }

            var speedMultiplier = SpeedMultiplierFor(effectivePose) * (sprinting ? MovementConstants.SprintSpeedMultiplier : 1f);

            // Frozen while the lobby's still up — "during lobby i can move and collect"
            // was a real complaint: a lobby that lets you play isn't really a lobby.
            // Gravity/grounding still runs below so the player stays put on the ground
            // rather than floating, they just can't walk or jump until the host starts.
            var matchActive = IsMatchActive();
            var horizontalInput = matchActive ? _currentInput : Vector2.zero;
            if (!matchActive)
            {
                _jumpRequested = false; // no queued jump carries over into the match starting
            }

            // Climbing takes over movement entirely for this tick — gravity/jump/normal
            // horizontal movement are all skipped while it's active. Only reachable while
            // the match is active (matchActive gates horizontalInput above; a Climbable
            // in range during the lobby doesn't let a player start climbing before the
            // host starts the match) and only while standing (crouch/crawl height changes
            // and climbing don't need to interact for this milestone's scope).
            var climbing = matchActive && effectivePose == PlayerPose.Standing && IsNearClimbable();
            if (climbing != _isClimbing.Value)
            {
                Debug.Log($"[Climb] {DisplayName} climbing={climbing} y={transform.position.y:F2}");
            }

            // Milestone 2 diagnostic: periodic position trace so the climb-route
            // verification is debuggable from the server log instead of guessing from
            // bandwidth numbers alone. BotModeEnabled is a client-side-only static (each
            // process has its own copy), unset on the server, so it can't gate this
            // server-side log - unconditional instead, cheap at one line per ~2s per
            // connected player.
            if (Time.time - _lastPositionLogTime > 2f)
            {
                _lastPositionLogTime = Time.time;
                Debug.Log($"[Pos] {DisplayName} pos={transform.position} climbing={_isClimbing.Value}");
            }

            _isClimbing.Value = climbing;

            if (climbing)
            {
                _verticalVelocity = 0f; // no gravity carries over into a subsequent fall
                _characterController.Move(ComputeClimbMove(horizontalInput, deltaTime));
                _serverPosition.Value = transform.position;
                return;
            }

            var grounded = _characterController.isGrounded;

            // _jumpRequested is only cleared once it's actually consumed below, not
            // whenever this method happens to run — CharacterController.isGrounded
            // reflects the *previous* Move() call's result, so it can read false for a
            // tick or two right as the player lands or the request arrives. Clearing it
            // unconditionally (as an earlier version did) silently ate the jump on
            // exactly those ticks, which is what made jumping feel unreliable/glitchy.
            if (_jumpRequested && grounded)
            {
                _verticalVelocity = MovementConstants.JumpSpeed;
                _jumpRequested = false;
            }
            else if (grounded)
            {
                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity = -2f; // small downward push keeps isGrounded true
                }
            }
            else
            {
                _verticalVelocity += MovementConstants.Gravity * deltaTime;
            }

            var move = new Vector3(horizontalInput.x, 0, horizontalInput.y) * (MovementConstants.MoveSpeed * speedMultiplier * deltaTime)
                       + Vector3.up * (_verticalVelocity * deltaTime);
            _characterController.Move(move);
            _serverPosition.Value = transform.position;
        }

        // "Match active" = either there's no MatchManager yet (fail open — never lock a
        // player out of moving just because of spawn ordering) or it exists and has
        // actually been started (see the lobby's Start Match button).
        private static bool IsMatchActive() => MatchManager.Instance == null || MatchManager.Instance.MatchStarted;

        // Resizes the CharacterController itself for the current pose (so it can, e.g.,
        // fit under something a standing player couldn't) — called from both
        // SimulateMovement and PredictAndReconcile since both are the two places that
        // actually call Move() and therefore care about the collider's real dimensions.
        // Purely visual squash (the model) is separate — see AnimateVisuals.
        //
        // Growing into a taller pose is refused if there isn't headroom for it (e.g.
        // still under a tunnel roof) — CharacterController.height doesn't do its own
        // collision sweep when resized, so growing back to standing height under
        // something too low let the collider silently interpenetrate the roof, with the
        // visual head poking out through it (the original bug report: "head is visible
        // above wall" while crouched under something). Player stays at their current
        // (smaller) height until they've actually moved somewhere with room to grow.
        //
        // Tallest-to-shortest, so a blocked growth attempt can fall back to the next
        // tier down instead of giving up entirely.
        private static readonly PlayerPose[] PoseTiersTallToShort =
        {
            PlayerPose.Standing, PlayerPose.Crouching, PlayerPose.Crawling,
        };

        // Returns the pose that's actually now applied (which may be shorter than
        // requested, if growth was refused) — callers replicate/predict visuals from
        // this, not from the raw requested pose, so the model's squash always matches
        // reality instead of popping to the requested pose the instant a key is
        // released/pressed regardless of whether the collider could actually follow.
        //
        // Tries the requested pose first, then progressively shorter ones: releasing
        // crawl (wanting Standing) while still under a roof that only clears Crouching
        // used to leave the player stuck at Crawling forever, since the old version only
        // ever attempted the exact requested height and gave up completely if that one
        // didn't fit — even though Crouching, one tier down, would have. This walks back
        // one tier at a time instead of jumping straight from the request to "do nothing".
        private PlayerPose ApplyPoseToController(PlayerPose pose)
        {
            var startIndex = Array.IndexOf(PoseTiersTallToShort, pose);
            for (var i = startIndex; i < PoseTiersTallToShort.Length; i++)
            {
                var candidate = PoseTiersTallToShort[i];
                var candidateHeight = HeightFor(candidate);
                if (candidateHeight > _characterController.height && !HasClearanceForHeight(candidateHeight))
                {
                    continue; // this tier doesn't fit either — try the next shorter one
                }

                if (!Mathf.Approximately(_characterController.height, candidateHeight))
                {
                    _characterController.height = candidateHeight;
                    _characterController.center = new Vector3(0, candidateHeight / 2f, 0);
                }

                return candidate;
            }

            // Crawling (the shortest tier) is always reachable — shrinking never needs a
            // clearance check — so the loop above always returns before falling through.
            return PlayerPose.Crawling;
        }

        private static readonly Collider[] ClearanceOverlapBuffer = new Collider[8];

        private bool HasClearanceForHeight(float height)
        {
            var radius = _characterController.radius * 0.95f;
            var bottom = transform.position + Vector3.up * radius;
            var top = transform.position + Vector3.up * Mathf.Max(height - radius, radius);
            var count = Physics.OverlapCapsuleNonAlloc(
                bottom, top, radius, ClearanceOverlapBuffer, ~0, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                if (ClearanceOverlapBuffer[i].GetComponentInParent<PlayerController>() != this)
                {
                    return false; // something else (a roof, another player) is in the way
                }
            }

            return true;
        }

        // Climbing: a distinct movement mode (not a PlayerPose tier — it's about *how*
        // the character moves, not how tall its collider is) for scaling the chair
        // (docs/GAME_DESIGN.md section 3's "leg -> rung -> seat -> table edge" route).
        // At this project's x25 world scale the existing jump (~1.1m apex, see
        // MovementConstants.JumpSpeed/Gravity) can't reach anywhere near the 11.25m
        // chair seat, let alone the 18.75m table top — a real vertical-traversal
        // mechanic is needed, not just more/taller jump-steps like ArenaBuilder's
        // BuildClimbableTower uses at native scale.
        //
        // Design: any collider carrying a Climbable component (Assets/Scripts/Shared/
        // Climbable.cs — a plain marker, not a Unity tag/layer, see its own comment)
        // within DetectionRange of the player enables climbing. While climbing,
        // gravity is suspended and the *world-space* input already computed for normal
        // movement (CameraRelativeXZ's output) is reprojected onto the player's own
        // facing direction via a dot product, so pressing "forward" toward the surface
        // being faced climbs up it and "back" climbs down — reusing the exact same
        // input already sent every tick rather than adding a second input scheme/RPC.
        private static readonly Collider[] ClimbOverlapBuffer = new Collider[8];

        private bool IsNearClimbable()
        {
            var range = _climbSettings != null ? _climbSettings.DetectionRange : 1.2f;
            var center = transform.position + Vector3.up * (_characterController.height * 0.5f);
            var count = Physics.OverlapSphereNonAlloc(center, range, ClimbOverlapBuffer, ~0, QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++)
            {
                if (ClimbOverlapBuffer[i].GetComponentInParent<Climbable>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        // worldInput is the same world-space XZ vector normal movement uses (see
        // CameraRelativeXZ) — dotted against facing so "press toward the surface" reads
        // as climbing up it regardless of camera angle, without a second input scheme.
        private Vector3 ComputeClimbMove(Vector2 worldInput, float deltaTime)
        {
            var climbSpeed = _climbSettings != null ? _climbSettings.ClimbSpeed : 3f;
            var shiftSpeed = _climbSettings != null ? _climbSettings.HorizontalShiftSpeed : 1.5f;

            var worldInput3 = new Vector3(worldInput.x, 0f, worldInput.y);
            var forward = transform.forward;
            var right = transform.right;

            var verticalIntent = Vector3.Dot(worldInput3, forward);
            var lateralIntent = Vector3.Dot(worldInput3, right);

            return Vector3.up * (verticalIntent * climbSpeed * deltaTime)
                   + right * (lateralIntent * shiftSpeed * deltaTime);
        }

        private void ApplyColor(int slotIndex)
        {
            var color = PlayerColors.Get(slotIndex);
            foreach (var renderer in _renderers)
            {
                renderer.material.color = color;
            }
        }

        // Purely cosmetic, client-side only (the server never renders — see Update's
        // !IsServer guard). Vertical delta drives the airborne/jump heuristic the same
        // way the pre-existing horizontal delta already drove walk detection — no new
        // networked state needed (see UpdateLocomotionAnimation).
        private void AnimateVisuals()
        {
            if (_visual == null)
            {
                return;
            }

            var rawDelta = transform.position - _lastVisualPosition;
            _lastVisualPosition = transform.position;
            var verticalSpeed = Time.deltaTime > 0f ? rawDelta.y / Time.deltaTime : 0f;
            rawDelta.y = 0f;
            var horizontalSpeed = Time.deltaTime > 0f ? rawDelta.magnitude / Time.deltaTime : 0f;

            UpdateLocomotionAnimation(horizontalSpeed, verticalSpeed);

            // Billboard: always face the viewer, same as most games' nameplates — a
            // World Space Canvas doesn't do this on its own.
            if (_nameplate != null && Camera.main != null)
            {
                _nameplate.rotation = Camera.main.transform.rotation;
            }
        }

        // Drives the shared ThiefLocomotion AnimatorController purely via
        // Animator.CrossFade(stateName, ...) — the controller has no built-in
        // transition graph (see ThiefAnimatorBuilder), so this is the only thing
        // deciding which state plays. Re-fades only when the target state actually
        // changes (not every frame), so a held state isn't restarted repeatedly.
        //
        // Priority order: climbing > airborne/jump > pose-based idle/walk/sprint/
        // crouch. Climbing and pose are read from the same owner-predicted-vs-
        // replicated split the rest of the class already uses for movement (cosmetic
        // lag here is imperceptible, unlike actual movement, so climbing itself is
        // read straight from the replicated _isClimbing.Value on every instance,
        // owner included, rather than adding a further predicted-climbing field).
        // Airborne has no replicated state at all — see AnimateVisuals — inferred
        // identically for the owner's predicted position and remote players'
        // interpolated one.
        private void UpdateLocomotionAnimation(float horizontalSpeed, float verticalSpeed)
        {
            if (_animator == null)
            {
                return;
            }

            var crossfade = _thiefAnimSettings != null ? _thiefAnimSettings.CrossfadeDuration : 0.15f;
            var airborneThreshold = _thiefAnimSettings != null ? _thiefAnimSettings.AirborneVerticalThreshold : 1.5f;
            var jumpStartDuration = _thiefAnimSettings != null ? _thiefAnimSettings.JumpStartDuration : 0.25f;
            var jumpLandDuration = _thiefAnimSettings != null ? _thiefAnimSettings.JumpLandDuration : 0.2f;

            var climbing = _isClimbing.Value;
            var airborne = !climbing && Mathf.Abs(verticalSpeed) > airborneThreshold;

            string targetState;
            if (climbing)
            {
                // No dedicated climb clip in the Quaternius library (43 clips, see
                // ThiefAnimatorBuilder) — Walk_Loop reused as a "limbs are moving"
                // visual cue. The actual vertical motion comes from ComputeClimbMove,
                // not from this clip (applyRootMotion is off). See docs/DECISIONS.md.
                targetState = "Walk_Loop";
                _airborneElapsed = 0f;
                _landStateElapsed = -1f;
            }
            else if (_landStateElapsed >= 0f)
            {
                _landStateElapsed += Time.deltaTime;
                targetState = "Jump_Land";
                if (_landStateElapsed >= jumpLandDuration)
                {
                    _landStateElapsed = -1f;
                }
            }
            else if (airborne)
            {
                _airborneElapsed += Time.deltaTime;
                targetState = _airborneElapsed < jumpStartDuration && verticalSpeed > 0f ? "Jump_Start" : "Jump_Loop";
            }
            else if (_wasAirborne)
            {
                _landStateElapsed = 0f;
                targetState = "Jump_Land";
            }
            else
            {
                _airborneElapsed = 0f;

                // Driven by the *effective* pose (what the collider actually achieved),
                // not the raw requested _pose — see _effectivePose's own declaration for
                // why (releasing crouch under a low roof must keep reading as crouched
                // until the collider can actually grow). Owner reads its own zero-latency
                // local result; remote viewers read the replicated one.
                var effectivePose = IsOwner && !IsServer ? _predictedEffectivePose : _effectivePose.Value;
                var moving = horizontalSpeed > WalkDetectSpeed;
                if (effectivePose == PlayerPose.Standing)
                {
                    targetState = moving ? (_sprintHeld.Value ? "Sprint_Loop" : "Walk_Loop") : "Idle_Loop";
                }
                else
                {
                    // Crouching and Crawling share the same two clips — no dedicated
                    // crawl/prone clip exists in the library either. The collider height
                    // (what actually matters for fitting under the crawl tunnels) is
                    // unaffected either way. See docs/DECISIONS.md.
                    targetState = moving ? "Crouch_Fwd_Loop" : "Crouch_Idle_Loop";
                }
            }

            _wasAirborne = airborne;

            if (targetState != _currentAnimState)
            {
                _currentAnimState = targetState;
                _animator.CrossFade(targetState, crossfade);
            }
        }

        // This template is built 100% at runtime (CLAUDE.md forbids hand-edited prefab
        // assets), which hits two separate NGO requirements that a real, Editor-authored
        // prefab asset would satisfy automatically. Both are needed together — hitting
        // either one alone still leaves a connecting client stuck on "Connecting..."
        // forever, with no error on either side, which is exactly what made this so slow
        // to track down.
        //
        // 1. NetworkObject.GlobalObjectIdHash must be non-zero and consistent between
        //    client and server. It's normally assigned by Unity's Editor tooling when a
        //    NetworkObject is part of a real prefab asset on disk; a purely runtime one
        //    never gets it, and NGO silently aborts every spawn server-side ("Detected
        //    NetworkObject GlobalObjectIdHash value of 0!...runtime generated network
        //    prefab assets...not supported") via its own internal logging channel, not a
        //    normal exception or Debug.LogError. NGO's own test suite hits this same
        //    problem and works around it by directly assigning the (internal) field (see
        //    NetcodeIntegrationTestHelpers.MakeNetworkObjectTestPrefab); since that field
        //    isn't accessible outside NGO's assembly, reflection is the only option here.
        //    A fixed constant works because both the client and server independently call
        //    this same shared method, so they land on the identical value with no
        //    negotiation needed.
        //
        // 2. The template GameObject must be *active*. NGO's InvokeBehaviourNetworkSpawn
        //    silently skips calling OnNetworkSpawn on any NetworkBehaviour whose GameObject
        //    isn't active in the hierarchy. The server used to work around this by
        //    explicitly re-activating its own manually-instantiated spawn copies — but the
        //    client has no equivalent code, since NGO instantiates its local copy of a
        //    newly-spawned object internally, straight from the registered network prefab
        //    reference (this exact template), with nothing to reactivate it if the source
        //    was inactive. The template is parked far below the arena instead, so it stays
        //    active (satisfying NGO) without being visible to anyone.
        private const uint PlayerTemplateGlobalObjectIdHash = 0xC0BEA53A;
        private static readonly FieldInfo GlobalObjectIdHashField =
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Vector3 TemplateParkPosition = new(0f, -1000f, 0f);

        // Resources.Load names — see ThiefAnimatorBuilder for how ThiefLocomotion is
        // built, and docs/ASSETS.md "Placeholder character structure" for
        // ThiefModel_Placeholder's swappable-mesh wrapper design.
        private const string ThiefModelResourceName = "ThiefModel_Placeholder";
        private const string ThiefControllerResourceName = "ThiefLocomotion";

        public static GameObject CreateTemplate()
        {
            var root = new GameObject("Player");
            root.transform.position = TemplateParkPosition;
            var networkObject = root.AddComponent<NetworkObject>();
            GlobalObjectIdHashField.SetValue(networkObject, PlayerTemplateGlobalObjectIdHash);

            var controller = root.AddComponent<CharacterController>();
            controller.height = StandingControllerHeight;
            controller.radius = 0.35f;
            controller.center = new Vector3(0, StandingControllerHeight / 2f, 0);

            // All visible geometry lives under "Visual" so it stays a separate
            // transform from the CharacterController's own collision shape, which
            // ApplyPoseToController resizes directly.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            // Real placeholder mesh (Quaternius Superhero, Humanoid-rigged) — replaces
            // the earlier blocky-cube-primitive body now that CLAUDE.md allows imported
            // prefabs (see docs/DECISIONS.md). Ground-rooted (localPosition zero) since
            // the Humanoid rig's own root is feet-at-origin, the same convention
            // SleepPreviewBuilder's giant stations use. Per-slot recolouring needs no
            // special handling here — ApplyColor/_renderers below already generalizes
            // over whatever Renderers exist under root, cube or skinned mesh alike.
            var modelPrefab = Resources.Load<GameObject>(ThiefModelResourceName);
            if (modelPrefab != null)
            {
                var modelInstance = Instantiate(modelPrefab, visual.transform);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;

                var animator = modelInstance.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(ThiefControllerResourceName);
                    animator.applyRootMotion = false;
                }
                else
                {
                    Debug.LogWarning($"{ThiefModelResourceName} has no Animator — thief will be visible but won't animate.");
                }
            }
            else
            {
                Debug.LogWarning($"{ThiefModelResourceName} not found in Resources — thief will have no visible mesh. " +
                                  "Run PocketHeist.EditorTools.SleepPreviewBuilder.Build to regenerate it.");
            }

            CreateNameplate(root.transform);

            root.AddComponent<PlayerController>();
            return root;
        }

        // A sibling of Visual, not a child of it, so it stays at a fixed height above
        // the model regardless of pose. World Space Canvas doesn't auto-face the
        // camera, so AnimateVisuals rotates it manually each frame.
        private static void CreateNameplate(Transform parent)
        {
            var nameplateGo = new GameObject("Nameplate", typeof(Canvas));
            nameplateGo.transform.SetParent(parent, false);
            nameplateGo.transform.localPosition = new Vector3(0, 2.3f, 0);
            nameplateGo.transform.localScale = Vector3.one * 0.01f; // world-space canvas units -> ~2m-wide plate

            var canvas = nameplateGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = nameplateGo.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(220, 50);

            var textGo = new GameObject("NameplateText", typeof(Text));
            textGo.transform.SetParent(nameplateGo.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 30;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

    }
}
