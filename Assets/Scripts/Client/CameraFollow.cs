using UnityEngine;
using UnityEngine.InputSystem;

namespace CubeArena.Client
{
    // Third-person orbit camera: mouse movement rotates the view around the player
    // (yaw/pitch), and PlayerController.CameraRelativeXZ resolves WASD relative to
    // whatever direction this is currently facing. Cursor lock is managed by
    // ClientBootstrap (locked on spawn, released on disconnect), not here.
    public class CameraFollow : MonoBehaviour
    {
        private const float Distance = 8f;
        private const float HeightOffset = 2f;
        private const float MouseSensitivity = 0.15f;
        private const float MinPitch = -20f;
        private const float MaxPitch = 70f;

        public Transform Target;

        private float _yaw;
        private float _pitch = 20f;

        private void Update()
        {
            if (Target == null || Mouse.current == null)
            {
                return;
            }

            var delta = Mouse.current.delta.ReadValue();
            _yaw += delta.x * MouseSensitivity;
            _pitch -= delta.y * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
        }

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            var rotation = Quaternion.Euler(_pitch, _yaw, 0);
            var focusPoint = Target.position + Vector3.up * HeightOffset;
            transform.position = focusPoint - rotation * Vector3.forward * Distance;
            transform.rotation = rotation;
        }
    }
}
