using CubeArena.Shared.Tuning;
using UnityEditor;
using UnityEngine;

namespace PocketHeist.EditorTools
{
    // One-off: creates Assets/Resources/LootSettings.asset with the brief's starting
    // values (see LootSettings.cs defaults) so PlayerController/LootItem's Resources.Load
    // call has something to find. Re-run is safe (edits in place rather than
    // overwriting/duplicating) — same pattern as ClimbSettingsAssetCreator.
    public static class LootSettingsAssetCreator
    {
        private const string Path = "Assets/Resources/LootSettings.asset";

        public static void Create()
        {
            System.IO.Directory.CreateDirectory("Assets/Resources");

            var existing = AssetDatabase.LoadAssetAtPath<LootSettings>(Path);
            if (existing == null)
            {
                var settings = ScriptableObject.CreateInstance<LootSettings>();
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
