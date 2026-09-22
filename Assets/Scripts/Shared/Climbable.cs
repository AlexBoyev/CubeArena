using UnityEngine;

namespace CubeArena.Shared
{
    // Marks a collider as a surface PlayerController's climbing mode can attach to — see
    // PlayerController's climbing section. A plain marker component rather than a Unity
    // tag/layer: creating those means hand-editing ProjectSettings/TagManager.asset,
    // which CLAUDE.md's no-hand-editing rule covers just as much as .prefab/.unity
    // files; a MonoBehaviour marker needs no project-settings changes at all.
    public class Climbable : MonoBehaviour
    {
    }
}
