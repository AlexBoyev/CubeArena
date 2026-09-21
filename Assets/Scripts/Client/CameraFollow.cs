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

        // Set by ClientBootstrap's TogglePause. Without this, the ESC pause overlay
        // unlocks the cursor but this kept reading Mouse.current.delta anyway — so moving
        // the mouse to click Resume/Leave Match silently spun the camera in the
        // background, and resuming snapped the view to wherever that ended up. That's the
        // "ESC/resume is glitchy" report.
        public bool Paused;

        private float _yaw;
        private float _pitch = 20f;
        private bool _skipNextDelta;

        // Called right as the cursor re-locks on resume — re-locking snaps the OS cursor
        // back to the window center, which on some platforms registers as a single huge
        // synthetic delta on the very next read. Discarding just that one frame avoids a
        // second, separate resume-glitch on top of the pause-drift one above.
        public void NotifyResumed() => _skipNextDelta = true;

        private void Update()
        {
            if (Target == null || Mouse.current == null || Paused)
            {
                return;
            }

            var delta = Mouse.current.delta.ReadValue();
            if (_skipNextDelta)
            {
                _skipNextDelta = false;
                return;
            }

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
