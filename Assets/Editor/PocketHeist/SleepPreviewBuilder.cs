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
        internal const string ScenePath = "Assets/Scenes/SleepClipPreview.unity";

        // World-scale numbers from docs/GAME_DESIGN.md section 2 (x25 factor).
        // Internal (not private): SleepPreviewScreenshotter positions its
        // per-station verification cameras using these same values.
        private const float TableTopHeight = 18.75f;
        private const float TableWidth = 30f;
        internal const float TableDepth = 20f;
        private const float ChairSeatHeight = 11.25f;
        internal const float GiantScale = 25f;
        internal const float StationSpacing = 80f;

        // AssetDatabase.CreateAsset (and AnimatorController.CreateAnimatorControllerAtPath,
        // which is built on it) silently fails - not overwrites - when something
        // already exists at that path. First rebuild after the initial Build()
        // run hit exactly this on the material: BuildMaterial() re-ran
        // CreateAsset against the already-existing .mat path, it silently did
        // nothing, BuildCharacterModelPrefab() then loaded back a stale/broken
        // reference, and every station rendered the giant in Unity's magenta
        // missing-material fallback. Every CreateAsset call below is preceded by
        // this so Build() is safely re-runnable.
        private static void DeleteIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        public static void Build()
        {
            SetHumanoid($"{ModelDir}/Superhero_Male_FullBody.fbx");
            SetHumanoid($"{MixamoDir}/Laying_Sleeping.fbx");
            SetHumanoid($"{MixamoDir}/Sleeping_Idle1.fbx");
            SetHumanoid($"{MixamoDir}/Sleeping_Idle2.fbx");
            SetHumanoid($"{AnimDir}/UAL1_Standard.fbx");

            Directory.CreateDirectory(PrefabDir);
            BuildMaterial();
            // AssetDatabase.LoadAssetAtPath can resolve a just-created asset
            // within the same in-memory session even before it's fully
            // persisted/imported on disk - which is exactly what
            // BuildCharacterModelPrefab does a few lines down to fetch this
            // material back and assign it to the model's renderers. That
            // in-memory-only reference is what's suspected of not surviving
          // the scene+domain reload Play mode triggers (still magenta after
            // two rounds of fixes targeting other theories) - forcing a full
            // disk commit here, before anything references the material,
            // rules that out.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var characterModelPrefab = BuildCharacterModelPrefab();
            var giantPrefab = BuildWrapperPrefab(characterModelPrefab, "GiantModel_Placeholder", GiantScale);
            BuildWrapperPrefab(characterModelPrefab, "ThiefModel_Placeholder", 1f);

            var layingSleeping = FirstClip($"{MixamoDir}/Laying_Sleeping.fbx");
            var sleepingIdle1 = FirstClip($"{MixamoDir}/Sleeping_Idle1.fbx");
            var sleepingIdle2 = FirstClip($"{MixamoDir}/Sleeping_Idle2.fbx");
            var sittingIdleLoop = NamedClip($"{AnimDir}/UAL1_Standard.fbx", "Armature|Sitting_Idle_Loop");

            if (layingSleeping == null || sleepingIdle1 == null || sleepingIdle2 == null || sittingIdleLoop == null)
            {
                Debug.LogError("One or more required animation clips could not be found - aborting.");
                EditorApplication.Exit(1);
                return;
            }

            var headOnArmsLean = BuildHeadOnArmsLeanClip();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildLightingAndCamera();

            BuildStation(0, giantPrefab, "1: Laying Sleeping (Mixamo)",
                SimpleController("Preview_LayingSleeping", layingSleeping));
            BuildStation(1, giantPrefab, "2: Sleeping Idle 1 (Mixamo)",
                SimpleController("Preview_SleepingIdle1", sleepingIdle1));
            BuildStation(2, giantPrefab, "3: Sleeping Idle 2 (Mixamo)",
                SimpleController("Preview_SleepingIdle2", sleepingIdle2));
            BuildStation(3, giantPrefab, "4: Custom - Sitting + head-on-arms lean (experimental)",
                MaskedController(sittingIdleLoop, headOnArmsLean));

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

        // Load-and-modify-in-place rather than delete-then-recreate at the same
        // path in one call (what every earlier attempt at this method did, via
        // DeleteIfExists). That still rendered magenta (Unity's missing-material
        // fallback) in Play mode every time despite the material testing valid
        // in Edit mode immediately after creation (shader/textures/reload all
        // confirmed via logging) - strong sign the delete+immediate-recreate at
        // one path, with no asset database sync in between, was corrupting the
        // GUID/import linkage in a way that only actually surfaces once Play
        // mode's scene+domain reload re-resolves everything from disk. Editing
        // the same material asset in place across reruns avoids that path
        // entirely.
        private static void BuildMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("SCREENSHOT_STEP: Shader.Find(\"Universal Render Pipeline/Lit\") returned null!");
            }

            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/T_Superhero_Male_Dark.png");
            var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/T_Superhero_Male_Normal.png");
            var path = $"{ModelDir}/SuperheroMale_URP.mat";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = "SuperheroMale_URP" };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetTexture("_BaseMap", baseTex);
            mat.SetTexture("_BumpMap", normalTex);
            if (mat.HasProperty("_BumpMap")) mat.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            var reloaded = AssetDatabase.LoadAssetAtPath<Material>(path);
            Debug.Log($"SCREENSHOT_STEP: material ready, shader={reloaded?.shader?.name ?? "NULL"} baseTex={(baseTex != null)} normalTex={(normalTex != null)}");
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
            // Root motion would translate the character according to each clip's
            // baked root movement - wrong for a giant who must stay put at his
            // table. All four wrapper prefabs (giant, thief, and anything built
            // from SuperheroMale_CharacterModel later) inherit this.
            animator.applyRootMotion = false;

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
            DeleteIfExists(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var state = controller.layers[0].stateMachine.AddState(clip.name);
            state.motion = clip;
            return controller;
        }

        // Base layer: full-body Sitting_Idle_Loop (sets the seated leg/hip pose).
        // Upper-body layer (avatar-masked to spine/chest/head/arms, Override
        // blend, full weight): a custom-authored muscle-curve clip (see
        // BuildHeadOnArmsLeanClip) rather than any stock library clip - none of
        // the 86 available clips actually has a "head down on folded arms" pose,
        // so this is authored directly in Humanoid muscle space instead.
        private static RuntimeAnimatorController MaskedController(AnimationClip baseClip, AnimationClip leanClip)
        {
            var path = $"{PrefabDir}/Preview_CustomLean.controller";
            DeleteIfExists(path);
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

        // Authors a Humanoid muscle-curve clip directly (AnimationClip.SetCurve
        // with type=typeof(Animator) and the exact HumanTrait.MuscleName string -
        // Unity's documented way to build a Humanoid pose in code, avatar-agnostic
        // by construction since muscle values are normalized). Forward lean
        // through spine/chest/upper-chest, head tipped down, both arms brought
        // forward/down and forearms bent so the hands come together in front —
        // an approximation of "head resting on folded arms on the table," plus a
        // slow (4s cycle) breathing oscillation on the chest so the pose isn't
        // dead-static.
        //
        // First attempt (spine/chest/upperChest at -0.55/-0.5/-0.45) folded the
        // torso almost completely to the floor instead of a seated lean - the
        // three spine-chain muscles stack additively, so that combination was
        // roughly 2-3x too strong. Confirmed via an actual Play-mode screenshot
        // (the whole reason this is built this way instead of guessed blind) and
        // cut down here accordingly; the forward-lean *direction* the signs
        // produce was correct on the first try, only the magnitude was wrong.
        private static AnimationClip BuildHeadOnArmsLeanClip()
        {
            var clip = new AnimationClip { name = "Custom_HeadOnArms" };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            const float duration = 4f;

            void Hold(string muscle, float value) =>
                clip.SetCurve("", typeof(Animator), muscle,
                    AnimationCurve.Linear(0f, value, duration, value));

            Hold("Spine Front-Back", -0.22f);
            Hold("Chest Front-Back", -0.18f);
            Hold("UpperChest Front-Back", -0.15f);
            Hold("Neck Nod Down-Up", -0.45f);
            Hold("Head Nod Down-Up", -0.55f);

            Hold("Left Shoulder Down-Up", -0.2f);
            Hold("Left Shoulder Front-Back", 0.35f);
            Hold("Left Arm Down-Up", -0.4f);
            Hold("Left Arm Front-Back", 0.5f);
            Hold("Left Forearm Stretch", -0.55f);

            Hold("Right Shoulder Down-Up", -0.2f);
            Hold("Right Shoulder Front-Back", 0.35f);
            Hold("Right Arm Down-Up", -0.4f);
            Hold("Right Arm Front-Back", 0.5f);
            Hold("Right Forearm Stretch", -0.6f);

            // Slow breathing: a small sine oscillation layered onto the chest's
            // held lean value, one full cycle across the clip's 4s loop so it
            // seams cleanly.
            const float breathAmplitude = 0.04f;
            var breathCurve = new AnimationCurve();
            const int samples = 12;
            for (var i = 0; i <= samples; i++)
            {
                var t = duration * i / samples;
                var value = -0.18f + breathAmplitude * Mathf.Sin(2f * Mathf.PI * t / duration);
                breathCurve.AddKey(t, value);
            }

            for (var i = 0; i < breathCurve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(breathCurve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(breathCurve, i, AnimationUtility.TangentMode.ClampedAuto);
            }

            clip.SetCurve("", typeof(Animator), "Chest Front-Back", breathCurve);

            var path = $"{PrefabDir}/Custom_HeadOnArms.anim";
            DeleteIfExists(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // Station layout, giant facing +Z: floor at Y=0 (giant's root sits here -
        // Humanoid rig convention is feet-at-root, so ground-rooting plus the
        // clip's own joint poses is what actually produces a seated look; placing
        // the root at seat height instead - the original bug - floats the whole
        // character in a standing pose above the chair). Chair just behind/under
        // the giant, table further along +Z within arm's reach.
        // Chair moved closer to the table (was -1.5, a 4.5-unit gap to the table's
        // near edge) - that gap needed an unrealistically extreme forward lean to
        // bridge, which is what folded the giant to the floor on the first
        // attempt. 1.5 units of legroom is enough for a moderate, seated-looking
        // lean to actually reach the table.
        internal const float ChairNearZ = 1.5f;
        internal const float TableNearZ = 3f;
        private const float FloorSize = 60f;
        internal const float LabelHeight = 62f;

        private static void BuildStation(int index, GameObject giantPrefab, string label, RuntimeAnimatorController controller)
        {
            var originX = index * StationSpacing;
            var group = new GameObject($"Station_{index}");
            group.transform.position = new Vector3(originX, 0f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "FloorGreybox";
            floor.transform.SetParent(group.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, TableNearZ + TableDepth * 0.5f);
            floor.transform.localScale = new Vector3(FloorSize, 1f, FloorSize);

            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "TableGreybox";
            table.transform.SetParent(group.transform, false);
            table.transform.localPosition = new Vector3(0f, TableTopHeight - 0.75f, TableNearZ + TableDepth * 0.5f);
            table.transform.localScale = new Vector3(TableWidth, 1.5f, TableDepth);

            var chairSeat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chairSeat.name = "ChairSeatGreybox";
            chairSeat.transform.SetParent(group.transform, false);
            chairSeat.transform.localPosition = new Vector3(0f, ChairSeatHeight - 0.5f, ChairNearZ);
            chairSeat.transform.localScale = new Vector3(4f, 1f, 4f);

            var chairBack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chairBack.name = "ChairBackGreybox";
            chairBack.transform.SetParent(group.transform, false);
            chairBack.transform.localPosition = new Vector3(0f, ChairSeatHeight + 3f, ChairNearZ - 2f);
            chairBack.transform.localScale = new Vector3(4f, 6f, 0.5f);

            var giant = (GameObject)PrefabUtility.InstantiatePrefab(giantPrefab, group.transform);
            giant.name = "Giant";
            // Ground-rooted (Y=0), positioned over the chair seat's footprint -
            // the seated clip's own hip/knee poses raise him onto the seat, not a
            // manual Y offset.
            giant.transform.localPosition = new Vector3(0f, 0f, ChairNearZ);
            giant.transform.localRotation = Quaternion.identity;
            var animator = giant.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            BuildLabel(group.transform, label);
            BuildStationCamera(index, group.transform);
        }

        // Disabled by default (would otherwise all render into the Game view at
        // once) - SleepPreviewScreenshotter finds each by name and calls
        // .Render() directly during the verification pass.
        private static void BuildStationCamera(int index, Transform parent)
        {
            // Three-quarter side angle rather than straight from behind - shows
            // the forward-lean silhouette and head-to-table relationship (the
            // actual thing being judged) instead of just a back view. Pulled back
            // further than the first attempt, which was close enough that the
            // label filled most of the frame.
            var cameraGo = new GameObject($"StationCamera_{index}");
            cameraGo.transform.SetParent(parent, false);
            cameraGo.transform.localPosition = new Vector3(42f, ChairSeatHeight + 19f, ChairNearZ - 9f);
            cameraGo.transform.LookAt(parent.position + new Vector3(0f, ChairSeatHeight + 5f, TableNearZ + 5f));
            var camera = cameraGo.AddComponent<Camera>();
            // Default 60 deg FOV was wide enough, combined with the far clip
            // plane, to see clean across the 80-unit gap into a neighbouring
            // station's label floating in the empty sky - the actual cause of
            // the "labels overlap" symptom, not a label sizing/position problem.
            // A narrower FOV plus a much shorter far clip plane makes that
            // physically impossible regardless of what's out there. (28 deg was
            // too tight, framing only the head/hands - 36 balances both.)
            camera.fieldOfView = 36f;
            camera.farClipPlane = 100f;
            camera.nearClipPlane = 0.3f;
            camera.enabled = false;
        }

        private static void BuildLabel(Transform parent, string label)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(parent, false);
            labelGo.transform.localPosition = new Vector3(0f, LabelHeight, TableNearZ);
            // Faces roughly toward the station camera's new side-on position
            // (local +X-ish) rather than straight down +Z.
            labelGo.transform.localRotation = Quaternion.Euler(0f, -55f, 0f);

            // A dark backing quad behind the text. First attempt's card (36x8)
            // filled most of the frame once the verification camera was close
            // enough to read it — shrunk here now that the card only needs to
            // work at the new, further-back camera distance.
            var backing = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backing.name = "LabelBacking";
            Object.DestroyImmediate(backing.GetComponent<Collider>());
            backing.transform.SetParent(labelGo.transform, false);
            backing.transform.localScale = new Vector3(22f, 5f, 1f);
            backing.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            var backingRenderer = backing.GetComponent<Renderer>();
            var backingMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0f, 0f, 0f, 0.85f) };
            backingRenderer.sharedMaterial = backingMat;

            var textMesh = labelGo.AddComponent<TextMesh>();
            textMesh.text = label;
            textMesh.characterSize = 4f;
            textMesh.fontSize = 96;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = Color.yellow;
            var textRenderer = labelGo.GetComponent<MeshRenderer>();
            textRenderer.sortingOrder = 1;
        }

        private static void BuildLightingAndCamera()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Overview, framing all 4 stations - per-station close-up cameras for
            // the actual verification screenshots are built separately by
            // SleepPreviewScreenshotter, which knows the exact station layout
            // constants (StationSpacing, ChairNearZ, LabelHeight) via this class.
            var cameraGo = new GameObject("Overview Camera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(1.5f * StationSpacing, 110f, -180f);
            camera.transform.LookAt(new Vector3(1.5f * StationSpacing, 40f, 10f));
            camera.farClipPlane = 3000f;
            camera.nearClipPlane = 0.3f;
        }
    }
}
