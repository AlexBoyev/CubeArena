using CubeArena.Shared.Tuning;
using UnityEditor;
using UnityEngine;

namespace PocketHeist.EditorTools
{
    // One-off: creates Assets/Resources/ThiefAnimationSettings.asset with the brief's
    // starting values (see ThiefAnimationSettings.cs defaults) so PlayerController's
    // Resources.Load call has something to find. Re-run is safe (edits in place
    // rather than overwriting/duplicating) - see ClimbSettingsAssetCreator.
    public static class ThiefAnimationSettingsAssetCreator
    {
        private const string Path = "Assets/Resources/ThiefAnimationSettings.asset";

        public static void Create()
        {
            System.IO.Directory.CreateDirectory("Assets/Resources");

            var existing = AssetDatabase.LoadAssetAtPath<ThiefAnimationSettings>(Path);
            if (existing == null)
            {
                var settings = ScriptableObject.CreateInstance<ThiefAnimationSettings>();
                AssetDatabase.CreateAsset(settings, Path);
                Debug.Log($"Created {Path}");
            }
            else
            {
                Debug.Log($"{Path} already exists, left as-is (values may already be tuned).");
            }

            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
    }
}
