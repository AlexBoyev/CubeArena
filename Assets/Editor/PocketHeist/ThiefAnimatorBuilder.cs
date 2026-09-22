using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PocketHeist.EditorTools
{
    // Milestone 2 "thief prefab with animations": builds the shared locomotion
    // AnimatorController from the Quaternius Universal Animation Library
    // (UAL1_Standard.fbx). One layer, states only, no transition graph -
    // PlayerController drives state entirely via Animator.CrossFade(stateName, ...)
    // each frame a change is detected (see UpdateLocomotionAnimation), the same
    // "states only" pattern SleepPreviewBuilder.SimpleController already used for a
    // single-clip station; this just has more states and none are wired to
    // auto-transition into each other.
    //
    // UAL1_Standard.fbx has 43 clips, not 86 as docs/ASSETS.md's import note
    // estimated - confirmed via a one-off clip listing. No dedicated climb or
    // crawl/prone clip exists in it; PlayerController reuses Walk_Loop for climbing
    // and the Crouch_* clips for crawling too - see docs/DECISIONS.md for why.
    //
    // Idempotent (safe to rerun) - see SleepPreviewBuilder.DeleteIfExists for why
    // overwrite-at-same-path is avoided in favour of delete-then-recreate here (a
    // controller, unlike a ScriptableObject settings asset, has no tunable values a
    // rerun could clobber, so delete+recreate is safe and simpler than edit-in-place).
    public static class ThiefAnimatorBuilder
    {
        private const string AnimDir = "Assets/ThirdParty/Quaternius-Animations-Placeholder";
        private const string OutputPath = "Assets/Resources/ThiefLocomotion.controller";

        // Doubles as both the Animator state name and the clip's take name inside
        // UAL1_Standard.fbx (after stripping the "Armature|" prefix Unity's FBX
        // importer adds to every take).
        public static readonly string[] ClipNames =
        {
            "Idle_Loop", "Walk_Loop", "Sprint_Loop",
            "Crouch_Idle_Loop", "Crouch_Fwd_Loop",
            "Jump_Start", "Jump_Loop", "Jump_Land",
        };

        public static void Build()
        {
            System.IO.Directory.CreateDirectory("Assets/Resources");
            if (AssetDatabase.LoadAssetAtPath<Object>(OutputPath) != null)
            {
                AssetDatabase.DeleteAsset(OutputPath);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(OutputPath);
            var stateMachine = controller.layers[0].stateMachine;

            var clipsByName = AssetDatabase.LoadAllAssetsAtPath($"{AnimDir}/UAL1_Standard.fbx")
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToDictionary(c => c.name.Contains("|") ? c.name.Split('|')[1] : c.name);

            var first = true;
            foreach (var clipName in ClipNames)
            {
                if (!clipsByName.TryGetValue(clipName, out var clip))
                {
                    Debug.LogError($"Missing expected clip '{clipName}' in UAL1_Standard.fbx - aborting.");
                    EditorApplication.Exit(1);
                    return;
                }

                var state = stateMachine.AddState(clipName);
                state.motion = clip;
                if (first)
                {
                    stateMachine.defaultState = state;
                    first = false;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Built {OutputPath} with {ClipNames.Length} states.");
            EditorApplication.Exit(0);
        }
    }
}
