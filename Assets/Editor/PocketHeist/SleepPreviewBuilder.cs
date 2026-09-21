using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PocketHeist.EditorTools
{
    // Milestone 1 sleep-clip preview: builds a scene with the placeholder giant
    // (Quaternius "Superhero" mesh - the Regular/Teen proportions the brief
    // originally assumed are paid-tier only, see docs/ASSETS.md) seated at four
    // identical kitchen-table greyboxes, each playing a different sleep-pose
    // candidate, so the pick can be made by eye in the Editor. Not part of the
    // shipped game - deletable once a clip is chosen and the real level work
    // (Milestone 2+) begins.
    public static class SleepPreviewBuilder
    {
        private const string ModelDir = "Assets/ThirdParty/Quaternius-BaseCharacters-Placeholder";
        private const string AnimDir = "Assets/ThirdParty/Quaternius-Animations-Placeholder";
        private const string MixamoDir = "Assets/ThirdParty/Mixamo-SleepClips-Placeholder";
        private const string PrefabDir = ModelDir + "/Prefabs";
        private const string ScenePath = "Assets/Scenes/SleepClipPreview.unity";

        // World-scale numbers from docs/GAME_DESIGN.md section 2 (x25 factor).
        private const float TableTopHeight = 18.75f;
        private const float TableWidth = 30f;
        private const float TableDepth = 20f;
        private const float ChairSeatHeight = 11.25f;
        private const float GiantScale = 25f;
        private const float StationSpacing = 80f;

        public static void Build()
        {
            SetHumanoid($"{ModelDir}/Superhero_Male_FullBody.fbx");
            SetHumanoid($"{MixamoDir}/Laying_Sleeping.fbx");
            SetHumanoid($"{MixamoDir}/Sleeping_Idle1.fbx");
            SetHumanoid($"{MixamoDir}/Sleeping_Idle2.fbx");
            SetHumanoid($"{AnimDir}/UAL1_Standard.fbx");

            Directory.CreateDirectory(PrefabDir);
            BuildMaterial();
            var characterModelPrefab = BuildCharacterModelPrefab();
            var giantPrefab = BuildWrapperPrefab(characterModelPrefab, "GiantModel_Placeholder", GiantScale);
            BuildWrapperPrefab(characterModelPrefab, "ThiefModel_Placeholder", 1f);

            var layingSleeping = FirstClip($"{MixamoDir}/Laying_Sleeping.fbx");
            var sleepingIdle1 = FirstClip($"{MixamoDir}/Sleeping_Idle1.fbx");
            var sleepingIdle2 = FirstClip($"{MixamoDir}/Sleeping_Idle2.fbx");
            var sittingIdleLoop = NamedClip($"{AnimDir}/UAL1_Standard.fbx", "Armature|Sitting_Idle_Loop");
            var pickUpTable = NamedClip($"{AnimDir}/UAL1_Standard.fbx", "Armature|PickUp_Table");

            if (layingSleeping == null || sleepingIdle1 == null || sleepingIdle2 == null ||
                sittingIdleLoop == null || pickUpTable == null)
            {
                Debug.LogError("One or more required animation clips could not be found - aborting.");
                EditorApplication.Exit(1);
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildLightingAndCamera();

            BuildStation(0, giantPrefab, "1: Laying Sleeping (Mixamo)",
                SimpleController("Preview_LayingSleeping", layingSleeping));
            BuildStation(1, giantPrefab, "2: Sleeping Idle 1 (Mixamo)",
                SimpleController("Preview_SleepingIdle1", sleepingIdle1));
            BuildStation(2, giantPrefab, "3: Sleeping Idle 2 (Mixamo)",
                SimpleController("Preview_SleepingIdle2", sleepingIdle2));
            BuildStation(3, giantPrefab, "4: Custom - Sitting + forward-lean overlay (experimental)",
                MaskedController(sittingIdleLoop, pickUpTable));

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"Preview scene saved: {ScenePath}");
            EditorApplication.Exit(0);
        }

        private static void SetHumanoid(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                Debug.LogError($"No ModelImporter at {path}");
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            Debug.Log(avatar != null && avatar.isValid && avatar.isHuman
                ? $"Humanoid avatar OK: {path}"
                : $"WARNING: Humanoid avatar missing/invalid for {path} - retargeting may fail.");
        }

        private static void BuildMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = "SuperheroMale_URP" };
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/T_Superhero_Male_Dark.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/T_Superhero_Male_Normal.png"));
            if (mat.HasProperty("_BumpMap")) mat.EnableKeyword("_NORMALMAP");
            AssetDatabase.CreateAsset(mat, $"{ModelDir}/SuperheroMale_URP.mat");
        }

        // The raw imported model, wrapped as its own prefab so a future re-skin
        // (the real Quaternius SOURCE Regular/Teen meshes) means creating a new
        // prefab like this one and repointing GiantModel_Placeholder/
        // ThiefModel_Placeholder's nested instance at it - not touching whatever
        // ends up referencing those wrapper prefabs.
        private static GameObject BuildCharacterModelPrefab()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/Superhero_Male_FullBody.fbx");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            instance.name = "SuperheroMale_CharacterModel";

            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{ModelDir}/SuperheroMale_URP.mat");
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = mat;
            }

            var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
            var avatar = AssetDatabase.LoadAllAssetsAtPath($"{ModelDir}/Superhero_Male_FullBody.fbx")
                .OfType<Avatar>().FirstOrDefault();
            animator.avatar = avatar;

            var prefabPath = $"{PrefabDir}/SuperheroMale_CharacterModel.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        // Wrapper prefab: scale/role live here, the actual model is a nested
        // prefab instance - swapping meshes later is repointing this nested
        // instance to a different model prefab, not rebuilding this wrapper.
        private static GameObject BuildWrapperPrefab(GameObject modelPrefab, string wrapperName, float scale)
        {
            var root = new GameObject(wrapperName);
            root.transform.localScale = Vector3.one * scale;
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, root.transform);
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;

            var prefabPath = $"{PrefabDir}/{wrapperName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static AnimationClip FirstClip(string fbxPath) =>
            AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        private static AnimationClip NamedClip(string fbxPath, string clipName) =>
            AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == clipName);

        private static RuntimeAnimatorController SimpleController(string assetName, AnimationClip clip)
        {
            var path = $"{PrefabDir}/{assetName}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var state = controller.layers[0].stateMachine.AddState(clip.name);
            state.motion = clip;
            return controller;
        }

        // Base layer: full-body Sitting_Idle_Loop (sets the seated leg/hip pose).
        // Upper-body layer (avatar-masked to spine/chest/head/arms, Override
        // blend, full weight): PickUp_Table, chosen for its forward torso lean -
        // an approximation of "head on arms," not a purpose-built pose. Labeled
        // "experimental" in the scene for exactly that reason.
        private static RuntimeAnimatorController MaskedController(AnimationClip baseClip, AnimationClip leanClip)
        {
            var path = $"{PrefabDir}/Preview_CustomLean.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var baseState = controller.layers[0].stateMachine.AddState(baseClip.name);
            baseState.motion = baseClip;

            var mask = new AvatarMask();
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);
            AssetDatabase.AddObjectToAsset(mask, path);

            controller.AddLayer("UpperBodyLean");
            var upperLayer = controller.layers[1];
            upperLayer.avatarMask = mask;
            upperLayer.defaultWeight = 1f;
            upperLayer.blendingMode = AnimatorLayerBlendingMode.Override;
            var leanState = upperLayer.stateMachine.AddState(leanClip.name);
            leanState.motion = leanClip;
            controller.layers = new[] { controller.layers[0], upperLayer };

            return controller;
        }

        private static void BuildStation(int index, GameObject giantPrefab, string label, RuntimeAnimatorController controller)
        {
            var originX = index * StationSpacing;
            var group = new GameObject($"Station_{index}");
            group.transform.position = new Vector3(originX, 0f, 0f);

            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "TableGreybox";
            table.transform.SetParent(group.transform);
            table.transform.localPosition = new Vector3(0f, TableTopHeight, TableDepth * 0.5f + 5f);
            table.transform.localScale = new Vector3(TableWidth, 1.5f, TableDepth);

            var chair = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chair.name = "ChairGreybox";
            chair.transform.SetParent(group.transform);
            chair.transform.localPosition = new Vector3(0f, ChairSeatHeight, 0f);
            chair.transform.localScale = new Vector3(4f, 1f, 4f);

            var giant = (GameObject)PrefabUtility.InstantiatePrefab(giantPrefab, group.transform);
            giant.name = "Giant";
            giant.transform.localPosition = new Vector3(0f, ChairSeatHeight, 0f);
            giant.transform.localRotation = Quaternion.identity;
            var animator = giant.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = controller;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(group.transform);
            labelGo.transform.localPosition = new Vector3(0f, GiantScale * 2.2f, 0f);
            var textMesh = labelGo.AddComponent<TextMesh>();
            textMesh.text = label;
            textMesh.characterSize = 4f;
            textMesh.fontSize = 48;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.color = Color.yellow;
        }

        private static void BuildLightingAndCamera()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGo = new GameObject("Overview Camera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(1.5f * StationSpacing, 90f, -140f);
            camera.transform.LookAt(new Vector3(1.5f * StationSpacing, 30f, 0f));
            camera.farClipPlane = 2000f;
            camera.nearClipPlane = 0.3f;
        }
    }
}
