using System;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CubeArena.Shared
{
    // Server-authoritative movement per section 3.3/6: the client sends input intent
    // only; the server simulates at a fixed 30Hz tick and is the sole writer of the
    // replicated position. See docs/NETCODE.md for the prediction/reconciliation design.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour
    {
        // Raised on the owning client only, once its own player object has spawned —
        // the client bootstrap uses this to attach the camera and show the HUD colour.
        public static event Action<PlayerController> LocalPlayerSpawned;


        private const float ReconcileSnapThresholdSqr = 4f; // snap if off by more than 2m (e.g. on spawn)
        private const float ReconcileBlendSpeed = 5f;
        private const float StandingControllerHeight = 1.9f;
        private const float CrouchControllerHeight = 1.1f;
        private const float CrouchVisualScaleY = 0.6f;
        private const float WalkSwingSpeed = 9f; // walk-cycle phase advance per meter traveled
        private const float MaxSwingAngleDeg = 35f;
        private const float PoseLerpSpeed = 8f;

        private readonly NetworkVariable<Vector3> _serverPosition = new(
            writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _slotIndex = new(
            writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isCrouching = new(
            writePerm: NetworkVariableWritePermission.Server);

        private CharacterController _characterController;
        private Renderer[] _renderers;
        private Vector2 _lastSentInput;
        private Vector2 _currentInput; // server-side: latest input received from the owner
        private float _verticalVelocity; // server-side: jump/gravity state
        private bool _jumpRequested; // server-side: set by JumpServerRpc, consumed next tick
        private float _predictedVerticalVelocity; // owner-client-side: local jump/gravity prediction
        private bool _predictedJumpRequested; // owner-client-side: consumed in PredictAndReconcile
        private bool _crouchHeld; // owner-client-side: last-sent state, for change detection

        // Purely cosmetic (client-only — see AnimateVisuals): the swingable limb joints
        // and walk-cycle state.
        private Transform _visual;
        private Transform _armLeftPivot;
        private Transform _armRightPivot;
        private Transform _legLeftPivot;
        private Transform _legRightPivot;
        private Vector3 _lastVisualPosition;
        private float _walkCyclePhase;

        public int SlotIndex => _slotIndex.Value;

        private void Awake()
        {
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

            _lastVisualPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            ApplyColor(_slotIndex.Value);
            _slotIndex.OnValueChanged += (_, newValue) => ApplyColor(newValue);

            if (!IsServer)
            {
                transform.position = _serverPosition.Value;
            }

            if (IsOwner && !IsServer)
            {
                LocalPlayerSpawned?.Invoke(this);
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
            if (IsOwner && !IsServer)
            {
                ReadAndSendInput();
                PredictAndReconcile();
            }
            else if (!IsServer)
            {
                // Remote players: simple interpolation toward the authoritative position.
                transform.position = Vector3.Lerp(transform.position, _serverPosition.Value, Time.deltaTime * 10f);
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

        private void ReadAndSendInput()
        {
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

                var crouching = keyboard.leftCtrlKey.isPressed;
                if (crouching != _crouchHeld)
                {
                    _crouchHeld = crouching;
                    SetCrouchServerRpc(crouching);
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
            if (worldInput != _lastSentInput)
            {
                _lastSentInput = worldInput;
                SubmitInputServerRpc(worldInput);
            }
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
            ApplyCrouchToController(_isCrouching.Value);
            var speedMultiplier = _isCrouching.Value ? MovementConstants.CrouchSpeedMultiplier : 1f;

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

            var move = new Vector3(_lastSentInput.x, 0, _lastSentInput.y) * (MovementConstants.MoveSpeed * speedMultiplier * Time.deltaTime)
                       + Vector3.up * (_predictedVerticalVelocity * Time.deltaTime);
            _characterController.Move(move);

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
        private void SubmitInputServerRpc(Vector2 input)
        {
            // Reject/clamp inputs whose implied speed exceeds the allowed maximum (section 3.3).
            if (input.sqrMagnitude > 1.001f)
            {
                input = input.normalized;
            }

            _currentInput = input;
        }

        [ServerRpc]
        private void JumpServerRpc()
        {
            _jumpRequested = true;
        }

        [ServerRpc]
        private void SetCrouchServerRpc(bool crouching)
        {
            _isCrouching.Value = crouching;
        }

        private void SimulateMovement(float deltaTime)
        {
            ApplyCrouchToController(_isCrouching.Value);
            var speedMultiplier = _isCrouching.Value ? MovementConstants.CrouchSpeedMultiplier : 1f;

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

            var move = new Vector3(_currentInput.x, 0, _currentInput.y) * (MovementConstants.MoveSpeed * speedMultiplier * deltaTime)
                       + Vector3.up * (_verticalVelocity * deltaTime);
            _characterController.Move(move);
            _serverPosition.Value = transform.position;
        }

        // Resizes the CharacterController itself for a crouch (so it can, e.g., fit under
        // something a standing player couldn't) — called from both SimulateMovement and
        // PredictAndReconcile since both are the two places that actually call Move() and
        // therefore care about the collider's real dimensions. Purely visual crouch
        // (squashing the model) is separate — see AnimateVisuals.
        private void ApplyCrouchToController(bool crouching)
        {
            var targetHeight = crouching ? CrouchControllerHeight : StandingControllerHeight;
            if (Mathf.Approximately(_characterController.height, targetHeight))
            {
                return;
            }

            _characterController.height = targetHeight;
            _characterController.center = new Vector3(0, targetHeight / 2f, 0);
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

            var targetScaleY = _isCrouching.Value ? CrouchVisualScaleY : 1f;
            var scale = _visual.localScale;
            scale.y = Mathf.Lerp(scale.y, targetScaleY, Time.deltaTime * PoseLerpSpeed);
            _visual.localScale = scale;
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

            // All visible geometry lives under "Visual" so a crouch can squash just this
            // wrapper (AnimateVisuals scales it on Y) without touching the
            // CharacterController's own collision shape, which ApplyCrouchToController
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

            root.AddComponent<PlayerController>();
            return root;
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
