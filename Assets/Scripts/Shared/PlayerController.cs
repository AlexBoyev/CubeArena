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

        private readonly NetworkVariable<Vector3> _serverPosition = new(
            writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _slotIndex = new(
            writePerm: NetworkVariableWritePermission.Server);

        private CharacterController _characterController;
        private Renderer[] _renderers;
        private Vector2 _lastSentInput;
        private Vector2 _currentInput; // server-side: latest input received from the owner
        private float _verticalVelocity; // server-side: jump/gravity state
        private bool _jumpRequested; // server-side: set by JumpServerRpc, consumed next tick

        public int SlotIndex => _slotIndex.Value;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _renderers = GetComponentsInChildren<Renderer>();
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
                    JumpServerRpc();
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
        private void PredictAndReconcile()
        {
            var move = new Vector3(_lastSentInput.x, 0, _lastSentInput.y) * (MovementConstants.MoveSpeed * Time.deltaTime);
            _characterController.Move(move);

            var error = _serverPosition.Value - transform.position;
            if (error.sqrMagnitude > ReconcileSnapThresholdSqr)
            {
                transform.position = _serverPosition.Value;
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

        private void SimulateMovement(float deltaTime)
        {
            if (_characterController.isGrounded)
            {
                if (_jumpRequested)
                {
                    _verticalVelocity = MovementConstants.JumpSpeed;
                }
                else if (_verticalVelocity < 0f)
                {
                    _verticalVelocity = -2f; // small downward push keeps isGrounded true
                }
            }
            else
            {
                _verticalVelocity += MovementConstants.Gravity * deltaTime;
            }

            _jumpRequested = false;

            var move = new Vector3(_currentInput.x, 0, _currentInput.y) * (MovementConstants.MoveSpeed * deltaTime)
                       + Vector3.up * (_verticalVelocity * deltaTime);
            _characterController.Move(move);
            _serverPosition.Value = transform.position;
        }

        private void ApplyColor(int slotIndex)
        {
            var color = PlayerColors.Get(slotIndex);
            foreach (var renderer in _renderers)
            {
                renderer.material.color = color;
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

        public static GameObject CreateTemplate()
        {
            var root = new GameObject("Player");
            root.transform.position = TemplateParkPosition;
            var networkObject = root.AddComponent<NetworkObject>();
            GlobalObjectIdHashField.SetValue(networkObject, PlayerTemplateGlobalObjectIdHash);

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.5f;
            controller.radius = 0.5f;
            controller.center = new Vector3(0, 0.75f, 0);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 0.5f, 0);
            body.transform.localScale = Vector3.one;
            UnityEngine.Object.Destroy(body.GetComponent<Collider>());
            MaterialUtil.ApplyLitColor(body.GetComponent<Renderer>(), Color.white);

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0, 1.25f, 0);
            head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            UnityEngine.Object.Destroy(head.GetComponent<Collider>());
            MaterialUtil.ApplyLitColor(head.GetComponent<Renderer>(), Color.white);

            root.AddComponent<PlayerController>();
            return root;
        }
    }
}
