using System;
using System.IO;
using System.Linq;
using HauntedHouse.Commands;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HauntedHouse.Editor
{
    /// <summary>
    /// Player 모델과 새 애니메이션 클립을 연결하는 Animator Controller와 씬 설정을 생성한다.
    /// 메뉴 또는 Unity 배치 실행에서 반복 호출해도 기존 설정을 덮어쓰지 않도록 작성했다.
    /// </summary>
    public static class PlayerAnimationSetup
    {
        private const string ControllerPath = "Assets/Arts/Animations/Player.controller";
        private const string ModelPath = "Assets/Arts/Models/Girl/low_poly_girl .fbx";
        private const string IdleAnimationPath = "Assets/Arts/Animations/Nervously Look Around.fbx";
        private const string WalkingAnimationPath = "Assets/Arts/Animations/Walking.fbx";
        private const string IsMovingParameter = "IsMoving";

        /// <summary>
        /// Unity 에디터 메뉴에서 Player 애니메이션 설정을 생성하고 현재 씬에 적용한다.
        /// </summary>
        [MenuItem("Haunted House/Player/Setup Animations")]
        public static void SetupFromMenu()
        {
            Setup(SceneManager.GetActiveScene());
        }

        /// <summary>
        /// Unity 배치 모드에서 CoreSandbox 씬을 열고 Player 애니메이션 설정을 적용한다.
        /// </summary>
        public static void SetupCoreSandbox()
        {
            const string scenePath = "Assets/_3DStealthGame/Scenes/CoreSandbox.unity";
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Setup(scene);
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// 지정된 씬의 Player 모델에 Animator와 PlayerAnimationController를 연결한다.
        /// </summary>
        private static void Setup(Scene scene)
        {
            // 씬이 저장되지 않은 상태에서 잘못된 대상을 수정하지 않도록 활성 씬과 경로를 먼저 확인한다.
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                throw new InvalidOperationException("유효한 씬이 열려 있지 않아 Player 애니메이션을 설정할 수 없습니다.");
            }

            AnimatorController controller = CreateController();
            GameObject player = FindPlayer(scene);
            if (player == null)
            {
                throw new InvalidOperationException($"씬 '{scene.path}'에서 이름이 Player인 GameObject를 찾지 못했습니다.");
            }

            // 모델 FBX에 포함된 Animator를 우선 사용해 모델의 뼈 계층과 Avatar를 함께 유지한다.
            Transform model = FindGirlModel(player.transform);
            if (model == null)
            {
                throw new InvalidOperationException($"Player '{player.name}' 아래에서 low_poly_girl 모델을 찾지 못했습니다.");
            }

            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = player.GetComponent<Animator>();
            }

            if (animator == null)
            {
                animator = player.AddComponent<Animator>();
            }

            // 모델과 애니메이션 FBX가 Humanoid Rig이므로 모델 FBX에 포함된 Avatar를 반드시 연결한다.
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
            if (avatar == null)
            {
                throw new InvalidOperationException($"Humanoid Avatar를 찾지 못했습니다: {ModelPath}");
            }

            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            // 이동 구현과 표현 구현을 분리하되 Player 루트에서 함께 생명주기를 관리하도록 연결한다.
            if (player.GetComponent<PlayerAnimationController>() == null)
            {
                player.AddComponent<PlayerAnimationController>();
            }

            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            WriteSetupMessage(scene, player, model, controller);
        }

        /// <summary>
        /// Idle과 Walking 클립을 사용하는 bool 기반 Animator Controller를 생성한다.
        /// </summary>
        private static AnimatorController CreateController()
        {
            EnsureAnimationLoops(IdleAnimationPath);
            EnsureAnimationLoops(WalkingAnimationPath);
            AssetDatabase.Refresh();

            AnimationClip idleClip = FindClip(IdleAnimationPath, "Idle", "Nervously", "Look Around");
            AnimationClip walkingClip = FindClip(WalkingAnimationPath, "Walk", "Walking");
            if (idleClip == null || walkingClip == null)
            {
                throw new InvalidOperationException("Idle 또는 Walking FBX에서 AnimationClip을 찾지 못했습니다.");
            }

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(IsMovingParameter, AnimatorControllerParameterType.Bool);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState idleState = stateMachine.AddState("Idle", new Vector3(280f, 100f, 0f));
            AnimatorState walkingState = stateMachine.AddState("Walking", new Vector3(280f, 220f, 0f));
            idleState.motion = idleClip;
            walkingState.motion = walkingClip;
            stateMachine.defaultState = idleState;

            // 이동을 시작하면 짧게 블렌딩하고, Agent가 멈추면 Idle로 자연스럽게 복귀한다.
            AnimatorStateTransition startWalking = idleState.AddTransition(walkingState);
            startWalking.hasExitTime = false;
            startWalking.duration = 0.15f;
            startWalking.AddCondition(AnimatorConditionMode.If, 0f, IsMovingParameter);

            AnimatorStateTransition stopWalking = walkingState.AddTransition(idleState);
            stopWalking.hasExitTime = false;
            stopWalking.duration = 0.2f;
            stopWalking.AddCondition(AnimatorConditionMode.IfNot, 0f, IsMovingParameter);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// FBX의 기본 클립을 반복 재생하도록 가져오기 설정을 보정한다.
        /// </summary>
        private static void EnsureAnimationLoops(string assetPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                throw new FileNotFoundException($"애니메이션 FBX를 찾지 못했습니다: {assetPath}");
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            bool changed = false;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                // Idle·Walking 클립의 수직 루트 이동을 포즈에 굽혀 모델이 재생 시작 시 바닥 아래로 내려가지 않게 한다.
                // 실제 플레이어 위치는 NavMeshAgent가 관리하므로 애니메이션은 본의 자세만 바꿔야 한다.
                if (!clip.loopTime)
                {
                    clip.loopTime = true;
                    changed = true;
                }

                if (!clip.lockRootHeightY || clip.keepOriginalPositionY || !clip.heightFromFeet)
                {
                    clip.lockRootHeightY = true;
                    clip.keepOriginalPositionY = false;
                    clip.heightFromFeet = true;
                    changed = true;
                }
            }

            if (changed)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// FBX 내부에서 이름이 가장 잘 맞는 AnimationClip을 선택한다.
        /// </summary>
        private static AnimationClip FindClip(string assetPath, params string[] nameHints)
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return clips
                .OrderByDescending(clip => nameHints.Count(hint => clip.name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0))
                .FirstOrDefault();
        }

        /// <summary>
        /// 씬 루트에서 Player GameObject를 찾는다.
        /// </summary>
        private static GameObject FindPlayer(Scene scene)
        {
            return scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Player");
        }

        /// <summary>
        /// Player 하위에서 공백을 포함한 모델 이름 차이를 허용해 low_poly_girl을 찾는다.
        /// </summary>
        private static Transform FindGirlModel(Transform playerTransform)
        {
            return playerTransform.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name.Trim().Equals("low_poly_girl", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 배치 실행 결과를 Unity 로그에 남긴다.
        /// </summary>
        private static void WriteSetupMessage(Scene scene, GameObject player, Transform model, AnimatorController controller)
        {
            GameLog.Log(
                $"Player animation setup completed. Scene='{scene.path}', Player='{player.name}', " +
                $"Model='{model.name}', Controller='{AssetDatabase.GetAssetPath(controller)}'");
        }
    }
}
