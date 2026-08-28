using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 포획 사망 연출용 Mixamo 클립과 Animator Controller 상태/전이를 구성한다.
/// 같은 구성을 반복 적용해도 중복 파라미터나 전이를 만들지 않는다.
/// </summary>
[InitializeOnLoad]
public static class CaptureAnimationSetup
{
    private const string PlayerControllerPath = "Assets/Arts/Animations/Player.controller";
    private const string EnemyControllerPath = "Assets/Arts/Animations/Enemies/Killer_Enemy.controller";
    private const string PlayerClipPath = "Assets/Arts/Animations/Falling Back Death.fbx";
    private const string EnemyClipPath = "Assets/Arts/Animations/Zombie Attack.fbx";
    private const string PlayerTrigger = "CaptureDeath";
    private const string EnemyTrigger = "CaptureAttack";
    private const string PlayerStateName = "CaptureDeath";
    private const string EnemyStateName = "CaptureAttack";
    private const string SessionKey = "HauntedHouse.CaptureAnimationSetup.v4";

    static CaptureAnimationSetup()
    {
        EditorApplication.delayCall += RunAutomaticSetup;
    }

    private static void RunAutomaticSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        Setup();
    }

    [MenuItem("Haunted House/Setup Capture Animations")]
    public static void Setup()
    {
        SessionState.SetBool(SessionKey, true);
        try
        {
            ConfigureHumanoidRetargeting(PlayerClipPath, true);
            ConfigureHumanoidRetargeting(EnemyClipPath, false);

            AnimationClip playerClip = LoadAnimationClip(PlayerClipPath);
            AnimationClip enemyClip = LoadAnimationClip(EnemyClipPath);
            AnimatorController playerController = LoadController(PlayerControllerPath);
            AnimatorController enemyController = LoadController(EnemyControllerPath);

            ConfigurePlayerController(playerController, playerClip);
            AnimatorState enemyIdle = ConfigureEnemyController(enemyController, enemyClip);

            EditorUtility.SetDirty(playerController);
            EditorUtility.SetDirty(enemyController);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[CaptureAnimationSetup] 완료. Player={PlayerStateName} (마지막 프레임 유지), " +
                $"Enemy={EnemyStateName} -> {enemyIdle.name}.");
        }
        catch (Exception exception)
        {
            SessionState.SetBool(SessionKey, false);
            Debug.LogException(exception);
        }
    }

    private static void ConfigurePlayerController(AnimatorController controller, AnimationClip clip)
    {
        EnsureTrigger(controller, PlayerTrigger);
        AnimatorStateMachine stateMachine = RequireRootStateMachine(controller);
        AnimatorState captureState = FindDirectState(stateMachine, PlayerStateName) ??
                                     stateMachine.AddState(PlayerStateName);
        captureState.motion = clip;
        captureState.speed = 1f;

        AnimatorStateTransition[] outgoing = captureState.transitions;
        foreach (AnimatorStateTransition transition in outgoing)
        {
            captureState.RemoveTransition(transition);
        }

        ReplaceAnyStateTransition(stateMachine, captureState, PlayerTrigger, 0.05f);
    }

    private static AnimatorState ConfigureEnemyController(AnimatorController controller, AnimationClip clip)
    {
        EnsureTrigger(controller, EnemyTrigger);
        AnimatorStateMachine stateMachine = RequireRootStateMachine(controller);
        AnimatorState captureState = FindDirectState(stateMachine, EnemyStateName) ??
                                     stateMachine.AddState(EnemyStateName);
        captureState.motion = clip;
        captureState.speed = 1f;

        AnimatorState idleState = FindDirectStateContaining(stateMachine, "Idle") ?? stateMachine.defaultState;
        if (idleState == null || idleState == captureState)
        {
            throw new InvalidOperationException("Killer_Enemy.controller에서 복귀할 Idle/default 상태를 찾지 못했습니다.");
        }

        AnimatorStateTransition[] outgoing = captureState.transitions;
        foreach (AnimatorStateTransition transition in outgoing)
        {
            captureState.RemoveTransition(transition);
        }

        AnimatorStateTransition idleTransition = captureState.AddTransition(idleState);
        idleTransition.hasExitTime = true;
        idleTransition.exitTime = 1f;
        idleTransition.hasFixedDuration = true;
        idleTransition.duration = 0.1f;
        idleTransition.interruptionSource = TransitionInterruptionSource.None;

        ReplaceAnyStateTransition(stateMachine, captureState, EnemyTrigger, 0.05f);
        return idleState;
    }

    private static void ReplaceAnyStateTransition(
        AnimatorStateMachine stateMachine,
        AnimatorState destination,
        string triggerName,
        float duration)
    {
        AnimatorStateTransition[] transitions = stateMachine.anyStateTransitions;
        foreach (AnimatorStateTransition transition in transitions)
        {
            if (transition.destinationState == destination)
            {
                stateMachine.RemoveAnyStateTransition(transition);
            }
        }

        AnimatorStateTransition newTransition = stateMachine.AddAnyStateTransition(destination);
        newTransition.hasExitTime = false;
        newTransition.hasFixedDuration = true;
        newTransition.duration = duration;
        newTransition.canTransitionToSelf = false;
        newTransition.interruptionSource = TransitionInterruptionSource.None;
        newTransition.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
    }

    private static void EnsureTrigger(AnimatorController controller, string triggerName)
    {
        foreach (AnimatorControllerParameter parameter in controller.parameters)
        {
            if (parameter.name == triggerName)
            {
                if (parameter.type != AnimatorControllerParameterType.Trigger)
                {
                    throw new InvalidOperationException(
                        $"Animator 파라미터 '{triggerName}'가 Trigger가 아닙니다: {controller.name}");
                }

                return;
            }
        }

        controller.AddParameter(triggerName, AnimatorControllerParameterType.Trigger);
    }

    private static AnimatorStateMachine RequireRootStateMachine(AnimatorController controller)
    {
        if (controller.layers == null || controller.layers.Length == 0)
        {
            throw new InvalidOperationException($"Animator Controller '{controller.name}'에 레이어가 없습니다.");
        }

        return controller.layers[0].stateMachine;
    }

    private static AnimatorState FindDirectState(AnimatorStateMachine stateMachine, string stateName)
    {
        foreach (ChildAnimatorState child in stateMachine.states)
        {
            if (child.state != null && string.Equals(child.state.name, stateName, StringComparison.Ordinal))
            {
                return child.state;
            }
        }

        return null;
    }

    private static AnimatorState FindDirectStateContaining(AnimatorStateMachine stateMachine, string token)
    {
        foreach (ChildAnimatorState child in stateMachine.states)
        {
            if (child.state != null &&
                child.state.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return child.state;
            }
        }

        return null;
    }

    private static AnimatorController LoadController(string assetPath)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        if (controller == null)
        {
            throw new InvalidOperationException($"Animator Controller를 찾지 못했습니다: {assetPath}");
        }

        return controller;
    }

    private static AnimationClip LoadAnimationClip(string assetPath)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            {
                return clip;
            }
        }

        throw new InvalidOperationException($"AnimationClip을 찾지 못했습니다: {assetPath}");
    }

    private static void ConfigureHumanoidRetargeting(string animationPath, bool useFeetBasedY)
    {
        if (AssetImporter.GetAtPath(animationPath) is not ModelImporter importer)
        {
            throw new InvalidOperationException($"ModelImporter를 찾지 못했습니다: {animationPath}");
        }

        // Mixamo 애니메이션 FBX는 자체 스켈레톤에서 Humanoid Avatar를 생성해야 한다.
        // 대상 캐릭터 Avatar를 Copy From Other Avatar로 강제하면 서로 다른 본 계층에서 바인딩이 깨진다.
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar = null;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        foreach (ModelImporterClipAnimation clip in clips)
        {
            clip.loopTime = false;
            clip.loopPose = false;

            // NavMesh가 캐릭터 루트 위치를 소유하므로 Mixamo Root Transform 이동은 포즈에 bake한다.
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = !useFeetBasedY;
            clip.heightFromFeet = useFeetBasedY;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalPositionXZ = true;
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        Debug.Log($"[CaptureAnimationSetup] Humanoid self-avatar configured: {animationPath}.");
    }
}
