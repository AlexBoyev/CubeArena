using CubeArena.Shared.Tuning;
using UnityEditor;
using UnityEngine;

namespace PocketHeist.EditorTools
{
    // One-off: creates Assets/Resources/ClimbSettings.asset with the brief's starting
    // values (see ClimbSettings.cs defaults) so PlayerController's Resources.Load call
    // has something to find. Re-run is safe (edits in place rather than
    // overwriting/duplicating) — see SleepPreviewBuilder's DeleteIfExists comment for
    // why overwrite-at-same-path was avoided here too.
    public static class ClimbSettingsAssetCreator
    {
        private const string Path = "Assets/Resources/ClimbSettings.asset";

        public static void Create()
        {
            System.IO.Directory.CreateDirectory("Assets/Resources");

            var existing = AssetDatabase.LoadAssetAtPath<ClimbSettings>(Path);
            if (existing == null)
            {
                var settings = ScriptableObject.CreateInstance<ClimbSettings>();
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
