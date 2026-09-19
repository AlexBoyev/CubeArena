using UnityEngine;

namespace CubeArena.Client
{
    public class CameraFollow : MonoBehaviour
    {
        private static readonly Vector3 Offset = new(0, 8, -8);

        public Transform Target;

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            transform.position = Target.position + Offset;
            transform.LookAt(Target.position + Vector3.up);
        }
    }
}
