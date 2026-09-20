using UnityEngine;

namespace CubeArena.Shared
{
    // GameObject.CreatePrimitive() assigns Unity's legacy "Standard" shader, which URP
    // doesn't support — it renders as the pink/magenta "shader not found" error material.
    // Every runtime-built primitive needs an explicit URP shader instead, since CLAUDE.md
    // forbids using real material assets for this code-only project.
    //
    // The dedicated server build strips all shaders (Dedicated Server Optimizations), so
    // UrpLitShader is null there — ApplyLitColor no-ops in that case, which is fine since
    // the server never renders anything anyway.
    public static class MaterialUtil
    {
        private static readonly Shader UrpLitShader = Shader.Find("Universal Render Pipeline/Lit");

        public static void ApplyLitColor(Renderer renderer, Color color)
        {
            if (UrpLitShader == null)
            {
                return;
            }

            renderer.material = new Material(UrpLitShader) { color = color };
        }
    }
}
