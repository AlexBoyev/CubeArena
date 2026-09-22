using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PocketHeist.EditorTools
{
    // Milestone 4: wraps the already-imported KayKit/Kenney raw models (Assets/ThirdParty/,
    // not under any Resources/ folder, so Resources.Load can't reach them directly - see
    // docs/ASSETS.md) in thin runtime-loadable prefabs under Assets/Resources/Kitchen/,
    // same "wrapper prefab nests one instance of the raw imported model" convention already
    // used for GiantModel_Placeholder/ThiefModel_Placeholder (docs/ASSETS.md's "Placeholder
    // character structure"). KitchenBuilder.cs loads these via Resources.Load at runtime,
    // exactly like it already does for GiantModel_Placeholder - this has to happen in a real
    // build (client + dedicated server both call KitchenBuilder.Build()), where
    // AssetDatabase doesn't exist, so Resources.Load is the only option.
    //
    // Every wrapper is visual-only (any collider on the raw import is stripped) -
    // KitchenBuilder adds its own colliders sized for each piece's actual role: a real
    // matching BoxCollider for fridge/counter/wall/door, or none at all for table/chair,
    // which deliberately keep using the already-verified invisible primitive geometry
    // from Milestone 2 for collision/climbing (see docs/DECISIONS.md - re-deriving climb
    // heights from an unlabelled single-mesh chair model's bounding box was judged too
    // risky to the working climb route for this pass).
    public static class KitchenAssetPrefabBuilder
    {
        private const string ResourcesDir = "Assets/Resources/Kitchen";

        private static readonly (string PrefabName, string SourcePath)[] Wraps =
        {
            ("Kitchen_Table", "Assets/ThirdParty/KayKit-RestaurantBits/kitchentable_A_large.gltf"),
            ("Kitchen_Chair", "Assets/ThirdParty/KayKit-RestaurantBits/chair_A.gltf"),
            ("Kitchen_Fridge", "Assets/ThirdParty/KayKit-RestaurantBits/fridge_A.gltf"),
            ("Kitchen_CounterStraight", "Assets/ThirdParty/KayKit-RestaurantBits/kitchencounter_straight_A.gltf"),
            ("Kitchen_CounterSink", "Assets/ThirdParty/KayKit-RestaurantBits/kitchencounter_sink.gltf"),
            ("Kitchen_Wall", "Assets/ThirdParty/KayKit-RestaurantBits/wall.gltf"),
            ("Kitchen_WallWindow", "Assets/ThirdParty/KayKit-RestaurantBits/wall_window_closed.gltf"),
            ("Kitchen_WallDoorway", "Assets/ThirdParty/KayKit-RestaurantBits/wall_doorway.gltf"),
            ("Kitchen_Door", "Assets/ThirdParty/KayKit-RestaurantBits/door_A.gltf"),
            ("Kitchen_Rug", "Assets/ThirdParty/Kenney-FurnitureKit/rugRectangle.fbx"),
        };

        public static void Build()
        {
            System.IO.Directory.CreateDirectory(ResourcesDir);
            AssetDatabase.ImportAsset(ResourcesDir, ImportAssetOptions.ForceSynchronousImport);

            foreach (var (prefabName, sourcePath) in Wraps)
            {
                var targetPath = $"{ResourcesDir}/{prefabName}.prefab";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (source == null)
                {
                    Debug.LogError($"[KitchenAssetPrefabBuilder] Could not load source at {sourcePath} - skipping {prefabName}.");
                    continue;
                }

                var root = new GameObject(prefabName);
                var nested = Object.Instantiate(source, root.transform, false);
                nested.name = "Model";
                StripColliders(nested);

                if (prefabName == "Kitchen_Rug")
                {
                    ApplyVertexColorMaterial(nested);
                }

                PrefabUtility.SaveAsPrefabAsset(root, targetPath, out var success);
                Object.DestroyImmediate(root);

                Debug.Log(success
                    ? $"[KitchenAssetPrefabBuilder] Wrote {targetPath}"
                    : $"[KitchenAssetPrefabBuilder] FAILED to save {targetPath}");
            }

            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }

        // Kenney Furniture Kit's rugRectangle has no texture atlas - only vertex colour
        // (see docs/ASSETS.md). Creates a reusable material asset (once, idempotent) using
        // the new hand-written PocketHeist/VertexColorURP shader so the rug shows its real
        // baked colours instead of flat white/grey.
        private static void ApplyVertexColorMaterial(GameObject rugModel)
        {
            const string materialPath = ResourcesDir + "/RugVertexColor.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("PocketHeist/VertexColorURP");
                if (shader == null)
                {
                    Debug.LogError("[KitchenAssetPrefabBuilder] Shader 'PocketHeist/VertexColorURP' not found - is Assets/Shaders/VertexColorURP.shader present and compiling?");
                    return;
                }

                material = new Material(shader) { name = "RugVertexColor" };
                AssetDatabase.CreateAsset(material, materialPath);
            }

            foreach (var renderer in rugModel.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void StripColliders(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                Object.DestroyImmediate(collider);
            }
        }
    }
}
