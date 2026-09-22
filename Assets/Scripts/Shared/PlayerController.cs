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
        private const float CrouchVisualScaleY = 0.6f;
        private const float CrawlVisualScaleY = 0.32f;
        private const float WalkSwingSpeed = 9f; // walk-cycle phase advance per meter traveled
        private const float MaxSwingAngleDeg = 35f;
        private const float PoseLerpSpeed = 8f; // limb-swing smoothing only — pose height/squash snap instantly, see AnimateVisuals
        private const float ThrowForce = 9f; // meters/second, along camera-forward
        private const float ThrowUpwardBoost = 2.5f; // meters/second, added so throws arc instead of skimming the ground
        private const float PushForce = 3f; // impulse applied to an un-held CrateController's Rigidbody on CharacterController contact

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

        // Purely cosmetic (client-only — see AnimateVisuals): the swingable limb joints
        // and walk-cycle state.
        private Transform _visual;
        private Transform _armLeftPivot;
        private Transform _armRightPivot;
        private Transform _legLeftPivot;
        private Transform _legRightPivot;
        private Vector3 _lastVisualPosition;
        private float _walkCyclePhase;

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

            _characterController = GetComponent<CharacterController>();
            _renderers = GetComponentsInChildren<Renderer>();

            _visual = transform.Find("Visual");
            if (_visual != null)
            {
                _armLeftPivot = _visual.Find("ArmLeft");
                _armRightPivot = _visual.Find("ArmRight");
                _legLeftPivot = _visual.Find("LegLeft");
                _legRightPivot = _visual.Find("LegRight");
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
                ActiveServerPlayers.Remove(this);
            }
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

                // Same key grabs and throws — press E with nothing held to grab the
                // nearest crate in range/in front; press it again while holding one to
                // throw it. CrateController.LocalHeldCrate is this client's own
                // (client-side only) record of what it's currently holding, kept in sync
                // by CrateController.OnOwnershipChanged rather than tracked here, since
                // the server — not this input handler — is what actually decides whether
                // a grab succeeds.
                if (keyboard.eKey.wasPressedThisFrame)
                {
                    if (CrateController.LocalHeldCrate != null)
                    {
                        var cam = Camera.main;
                        var throwVelocity = (cam != null ? cam.transform.forward : transform.forward) * ThrowForce
                                             + Vector3.up * ThrowUpwardBoost;
                        RequestThrowServerRpc(throwVelocity);
                    }
                    else
                    {
                        RequestGrabServerRpc();
                    }
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
        // Deliberately simple: it only needs to generate realistic movement + push/grab/
        // throw network traffic for the bandwidth load test, not play well. Wanders
        // toward the nearest un-held crate, grabs it once in range, holds briefly, throws
        // it, repeats.
        private CrateController _botTargetCrate;
        private float _botStateTimer;
        // Separate from _botStateTimer (which governs target/throw pacing): without this,
        // RunBotBehavior would call RequestGrabServerRpc() on every single Update() while
        // in range and unheld. Update() is uncapped in a -batchmode -nographics build (no
        // Application.targetFrameRate set, no vsync) and can run tens of thousands of
        // times/sec — confirmed via a real 2-bot smoke test, where the contesting bot's
        // bandwidth ran ~10x its rival's before this fix. The real-keyboard path doesn't
        // have this problem since it's edge-triggered on wasPressedThisFrame (one keypress
        // = one RPC); a bot has no "key press" to edge-detect against, so it needs an
        // explicit cooldown instead — long enough for a grab's ownership change to
        // round-trip and flip LocalHeldCrate (which is what actually stops the retries).
        private float _botGrabRequestCooldown;

        private void RunBotBehavior()
        {
            _botStateTimer -= Time.deltaTime;
            _botGrabRequestCooldown -= Time.deltaTime;

            if (CrateController.LocalHeldCrate != null)
            {
                CommitWorldInputAndFacing(Vector2.zero, transform.eulerAngles.y);
                if (_botStateTimer <= 0f)
                {
                    var throwVelocity = transform.forward * ThrowForce + Vector3.up * ThrowUpwardBoost;
                    RequestThrowServerRpc(throwVelocity);
                    _botTargetCrate = null;
                    _botStateTimer = UnityEngine.Random.Range(1.5f, 3f); // cooldown before picking a new target
                }

                return;
            }

            if (_botTargetCrate == null || _botStateTimer <= 0f)
            {
                _botTargetCrate = FindNearestVisibleCrate();
                _botStateTimer = 6f; // give up and re-pick after this long regardless
            }

            // No crates exist while Milestone 3's loot redesign is pending (see
            // ServerBootstrap - SpawnCrates is disabled), so bots fall back to a fixed
            // climb-route test target instead of idling: this is the actual Milestone 2
            // verification mechanism (walk from the mousehole, through the chair's
            // climbable leg, onto the seat, up the seat-to-table climb, onto the table
            // top), driven entirely by this same crate-seeking movement code with a
            // different destination - not bespoke climb-specific bot logic.
            if (_botTargetCrate == null)
            {
                RunClimbTestBotBehavior();
                return;
            }

            var toTarget = _botTargetCrate.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f)
            {
                CommitWorldInputAndFacing(Vector2.zero, transform.eulerAngles.y);
                return;
            }

            var direction = toTarget.normalized;
            var facingYaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;

            if (toTarget.magnitude <= CrateController.GrabRange * 0.8f)
            {
                if (_botGrabRequestCooldown <= 0f)
                {
                    RequestGrabServerRpc();
                    _botGrabRequestCooldown = 0.5f; // let ownership resolve before retrying
                }

                CommitWorldInputAndFacing(Vector2.zero, facingYaw);
                return;
            }

            // World-space input directly toward the target — no camera to resolve
            // relative to, since a bot has no camera.
            CommitWorldInputAndFacing(new Vector2(direction.x, direction.z), facingYaw);
        }

        private CrateController FindNearestVisibleCrate()
        {
            var crates = FindObjectsByType<CrateController>(FindObjectsSortMode.None);
            CrateController nearest = null;
            var nearestDistSqr = float.MaxValue;
            foreach (var crate in crates)
            {
                // Every client keeps one never-spawned CrateController template parked
                // at (0,-1000,0) purely so NGO has something to register as a network
                // prefab (see CrateController.CreateTemplate, same convention as
                // PlayerController's own template) - kept *active* in the scene (NGO
                // requires that), which means a plain FindObjectsByType scan picks it
                // up like a real crate. IsHeld doesn't filter it out either: a
                // never-spawned NetworkObject's OwnerClientId defaults to 0, which is
                // also NetworkManager.ServerClientId, so it reads as "not held".
                // First Milestone 2 climb-route bot test walked every bot straight to
                // this template's (0, z=0) horizontal position instead of the actual
                // climb-test waypoints - IsSpawned is what actually distinguishes a
                // real, network-spawned crate from this template.
                if (!crate.IsSpawned || crate.IsHeld)
                {
                    continue;
                }

                var distSqr = (crate.transform.position - transform.position).sqrMagnitude;
                if (distSqr < nearestDistSqr)
                {
                    nearestDistSqr = distSqr;
                    nearest = crate;
                }
            }

            return nearest;
        }

        // Two ground-level waypoints, same Z as the chair so a direct path actually
        // passes through it (a path straight from the mousehole to the table's own
        // center does not — the diagonal only reaches the chair's Z right at the very
        // end, well past the chair's X). Switches to the table once within a few
        // meters of the chair rather than exactly on top of it, so forward input never
        // drops to zero while still inside the leg's climbable zone. Climbing itself
        // needs no special-case bot code at all: ComputeClimbMove only ever consumes
        // the up/right components of world input while _isClimbing (see
        // SimulateMovement), never resolving horizontal distance-to-target, so the
        // exact same "walk toward target, don't stop until arrived" logic that
        // chases crates keeps pressing forward into the climbable surface for the
        // whole climb.
        // Computed on demand, not as static readonly fields initialized from
        // KitchenBuilder's own static fields: a first attempt at this had both bots
        // walking straight for world-origin (0,0,0) instead of the chair, traced back
        // to C#'s "beforefieldinit" semantics - a type with no explicit static
        // constructor (both KitchenBuilder and PlayerController qualify) gives the
        // runtime latitude to run its static field initializers any time before first
        // use, not strictly "before the first class that references it", so
        // PlayerController's own static fields ended up reading KitchenBuilder.
        // TableCenter as its default Vector3.zero instead of (10,0,15). Local
        // properties evaluated at call time (well after KitchenBuilder.Build() has
        // long since run) sidestep the whole cross-class static-init-order question.
        private static Vector3 ClimbTestWaypointChair =>
            new(KitchenBuilder.TableCenter.x - KitchenBuilder.TableWidth / 2f - 1.5f, 0f, KitchenBuilder.TableCenter.z);
        private static Vector3 ClimbTestWaypointTable => KitchenBuilder.TableCenter;
        private const float WaypointSwitchDistance = 3f;
        private bool _climbTestReachedChair;

        private void RunClimbTestBotBehavior()
        {
            var target = _climbTestReachedChair ? ClimbTestWaypointTable : ClimbTestWaypointChair;
            var toTarget = target - transform.position;
            toTarget.y = 0f;

            if (!_climbTestReachedChair && toTarget.magnitude <= WaypointSwitchDistance)
            {
                _climbTestReachedChair = true;
                return; // re-evaluate next frame against the new (table) target
            }

            if (toTarget.sqrMagnitude < 0.01f)
            {
                CommitWorldInputAndFacing(Vector2.zero, transform.eulerAngles.y);
                return;
            }

            var direction = toTarget.normalized;
            var facingYaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;
            CommitWorldInputAndFacing(new Vector2(direction.x, direction.z), facingYaw);
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
        // vote RPC) — a player can only ever request a grab/throw through their own
        // PlayerController, which they own by definition.
        [ServerRpc]
        private void RequestGrabServerRpc()
        {
            CrateController nearest = null;
            var nearestDistSqr = CrateController.GrabRange * CrateController.GrabRange;
            foreach (var crate in CrateController.ActiveServerCrates)
            {
                if (crate.IsHeld)
                {
                    continue;
                }

                var toCrate = crate.transform.position - transform.position;
                var distSqr = toCrate.sqrMagnitude;
                if (distSqr > nearestDistSqr)
                {
                    continue;
                }

                // Roughly in front of the player, not something behind them they'd have
                // no way of aiming away from.
                if (Vector3.Dot(transform.forward, toCrate.normalized) < 0.3f)
                {
                    continue;
                }

                nearestDistSqr = distSqr;
                nearest = crate;
            }

            nearest?.ServerGrab(OwnerClientId);
        }

        [ServerRpc]
        private void RequestThrowServerRpc(Vector3 releaseVelocity)
        {
            foreach (var crate in CrateController.ActiveServerCrates)
            {
                if (crate.OwnerClientId == OwnerClientId)
                {
                    crate.ServerRelease(releaseVelocity);
                    break;
                }
            }
        }

        // CharacterController.Move() does not automatically push Rigidbodies it collides
        // with — this is the hook Unity expects a script to implement for that. Server-only
        // since the server's own SimulateMovement is what actually calls .Move() with
        // authority; un-held crates are server-owned anyway, so this is the correct side to
        // apply the push from. Held crates are excluded — pushing something someone's
        // actively carrying would fight the hold-point following in CrateController.
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!IsServer || hit.rigidbody == null)
            {
                return;
            }

            if (!hit.rigidbody.TryGetComponent<CrateController>(out var crate) || crate.IsHeld)
            {
                return;
            }

            var pushDirection = hit.moveDirection;
            pushDirection.y = 0f;
            if (pushDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            hit.rigidbody.AddForce(pushDirection.normalized * PushForce, ForceMode.Impulse);
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
        // !IsServer guard). Walk-cycle limb swing is driven by actual horizontal
        // movement, which works identically for the owner's predicted position and
        // remote players' interpolated one without needing any extra networked state.
        // Crouch squashes the whole Visual wrapper — see CreateTemplate's comment on why
        // it's a separate transform from the CharacterController's own collision shape.
        private void AnimateVisuals()
        {
            if (_visual == null)
            {
                return;
            }

            var delta = transform.position - _lastVisualPosition;
            _lastVisualPosition = transform.position;
            delta.y = 0f;
            var horizontalSpeed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            var isWalking = horizontalSpeed > 0.15f;

            if (isWalking)
            {
                _walkCyclePhase += horizontalSpeed * WalkSwingSpeed * Time.deltaTime;
            }

            var swing = isWalking ? Mathf.Sin(_walkCyclePhase) * MaxSwingAngleDeg : 0f;
            SetLimbSwing(_legLeftPivot, swing);
            SetLimbSwing(_legRightPivot, -swing);
            SetLimbSwing(_armLeftPivot, -swing);
            SetLimbSwing(_armRightPivot, swing);

            // Snapped instantly, not lerped: ApplyPoseToController resizes the actual
            // CharacterController collider instantly too, and letting this visual squash
            // lag a fraction of a second behind it meant the head kept its full standing
            // height for a moment right as the (already-shrunk) collider carried the
            // player under a low roof — visually clipping through it even though the
            // real collision shape was already clear. Matching them exactly removes that
            // window entirely.
            //
            // Driven by the *effective* pose (what the collider actually achieved), not
            // the raw requested _pose — otherwise releasing crouch while still under a
            // low roof popped the model to standing height even though the collider
            // (correctly) refused to grow, which is the "player needs to stay crouched
            // until the collision ends" bug. The owner reads its own zero-latency local
            // result; remote viewers read the replicated one.
            var effectivePose = IsOwner && !IsServer ? _predictedEffectivePose : _effectivePose.Value;
            var targetScaleY = effectivePose switch
            {
                PlayerPose.Crawling => CrawlVisualScaleY,
                PlayerPose.Crouching => CrouchVisualScaleY,
                _ => 1f,
            };
            var scale = _visual.localScale;
            scale.y = targetScaleY;
            _visual.localScale = scale;

            // Billboard: always face the viewer, same as most games' nameplates — a
            // World Space Canvas doesn't do this on its own.
            if (_nameplate != null && Camera.main != null)
            {
                _nameplate.rotation = Camera.main.transform.rotation;
            }
        }

        private static void SetLimbSwing(Transform pivot, float targetAngleDeg)
        {
            if (pivot == null)
            {
                return;
            }

            pivot.localRotation = Quaternion.Slerp(
                pivot.localRotation, Quaternion.Euler(targetAngleDeg, 0, 0), Time.deltaTime * PoseLerpSpeed);
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

            // All visible geometry lives under "Visual" so a crouch/crawl can squash just
            // this wrapper (AnimateVisuals scales it on Y) without touching the
            // CharacterController's own collision shape, which ApplyPoseToController
            // resizes directly and separately.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            // Blocky humanoid (torso/head/arms/legs) instead of a plain 2-cube stack —
            // still built entirely from Cube primitives (CLAUDE.md: no mesh/prefab
            // assets), just with human-like proportions instead of a "totem pole" look.
            // Arms/legs are a pivot-at-the-joint + a cube hanging from it (CreateLimb),
            // so AnimateVisuals' walk-cycle swing rotates them naturally from the
            // shoulder/hip instead of spinning the cube around its own center.
            CreateBodyPart(visual.transform, "Torso", new Vector3(0, 1.25f, 0), new Vector3(0.5f, 0.7f, 0.3f));
            CreateBodyPart(visual.transform, "Head", new Vector3(0, 1.775f, 0), new Vector3(0.35f, 0.35f, 0.35f));
            CreateLimb(visual.transform, "ArmLeft", new Vector3(-0.45f, 1.575f, 0), new Vector3(0.2f, 0.65f, 0.2f));
            CreateLimb(visual.transform, "ArmRight", new Vector3(0.45f, 1.575f, 0), new Vector3(0.2f, 0.65f, 0.2f));
            CreateLimb(visual.transform, "LegLeft", new Vector3(-0.15f, 0.9f, 0), new Vector3(0.25f, 0.9f, 0.25f));
            CreateLimb(visual.transform, "LegRight", new Vector3(0.15f, 0.9f, 0), new Vector3(0.25f, 0.9f, 0.25f));

            CreateNameplate(root.transform);

            root.AddComponent<PlayerController>();
            return root;
        }

        // A sibling of Visual, not a child of it, so the crouch squash (AnimateVisuals
        // scales Visual on Y) doesn't shrink or drop the nameplate — it stays at a fixed
        // height above the standing model either way. World Space Canvas doesn't
        // auto-face the camera, so AnimateVisuals rotates it manually each frame.
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

        private static void CreateBodyPart(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            // Collision is handled entirely by the CharacterController above — per-part
            // colliders would just fight it, so they're removed like Body/Head were before.
            UnityEngine.Object.Destroy(part.GetComponent<Collider>());
            MaterialUtil.ApplyLitColor(part.GetComponent<Renderer>(), Color.white);
        }

        // A pivot at the joint (shoulder/hip) with the visible cube hanging down from
        // it — rotating the returned pivot swings the limb from the joint instead of
        // spinning the cube around its own center. The pivot keeps the limb's name
        // (e.g. "ArmLeft") so Awake's transform.Find calls still resolve it directly.
        private static void CreateLimb(Transform parent, string name, Vector3 pivotLocalPosition, Vector3 size)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = pivotLocalPosition;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name + "Cube";
            cube.transform.SetParent(pivot.transform, false);
            cube.transform.localPosition = new Vector3(0, -size.y / 2f, 0);
            cube.transform.localScale = size;
            UnityEngine.Object.Destroy(cube.GetComponent<Collider>());
            MaterialUtil.ApplyLitColor(cube.GetComponent<Renderer>(), Color.white);
        }
    }
}
