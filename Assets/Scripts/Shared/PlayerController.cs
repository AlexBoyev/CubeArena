using System;
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

        // Server-only: called by ConnectionApprovalHandler's spawn logic right after
        // NetworkObject.InstantiateAndSpawn, before any client has a chance to read it.
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
            }

            if (input != _lastSentInput)
            {
                _lastSentInput = input;
                SubmitInputServerRpc(input);
            }
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

        private void SimulateMovement(float deltaTime)
        {
            var move = new Vector3(_currentInput.x, 0, _currentInput.y) * (MovementConstants.MoveSpeed * deltaTime);
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

        public static GameObject CreateTemplate()
        {
            var root = new GameObject("Player");
            root.AddComponent<NetworkObject>();

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

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0, 1.25f, 0);
            head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            UnityEngine.Object.Destroy(head.GetComponent<Collider>());

            root.AddComponent<PlayerController>();
            return root;
        }
    }
}
