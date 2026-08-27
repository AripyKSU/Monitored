#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Sector2 MachineRoom의 설정과 핵심 런타임 분기를 자동으로 검사하는 Play Mode 검증 진입점이다.
/// 메뉴에서 한 번 실행하면 Play Mode에 진입한 뒤 임시 Runner가 검사를 수행하고 결과를 Console에 남긴다.
/// </summary>
[InitializeOnLoad]
public static class HospitalMachineRoomPlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalMachineRoomPlayModeVerificationPending";

    /// <summary>도메인 리로드 뒤에도 Play Mode 진입 콜백을 유지한다.</summary>
    static HospitalMachineRoomPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>CoreSandbox를 Play Mode로 시작하고 MachineRoom 검증 Runner를 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector2 MachineRoom Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("MachineRoom Play Mode 검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 시작해야 합니다.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path != ScenePath)
        {
            GameLog.LogWarning($"MachineRoom Play Mode 검증은 CoreSandbox 씬에서만 실행할 수 있습니다. 현재 씬: {activeScene.path}");
            return;
        }

        // 검증 자체는 Play Mode 임시 상태만 변경하며, 완료 후 Play Mode를 종료해 씬에 저장하지 않는다.
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>예약된 검증을 Play Mode 진입 직후 임시 Runner로 시작한다.</summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        GameObject runnerObject = new GameObject("HospitalMachineRoomPlayModeVerificationRunner");
        runnerObject.AddComponent<HospitalMachineRoomPlayModeVerificationRunner>();
    }
}

/// <summary>
/// Play Mode에서 MachineRoomTarget의 전원 분기·중복 입력·체크포인트와 ToolCase 연결을 검사하는 임시 컴포넌트다.
/// 모든 변경은 현재 Play 세션 안에서만 발생하며 검증 종료 시 Unity가 폐기한다.
/// </summary>
public sealed class HospitalMachineRoomPlayModeVerificationRunner : MonoBehaviour
{
    private const string MachineTargetName = "S2_MachineRoom_Tank_1";
    private const string ToolCaseTargetName = "S2_MachineRoom_ToolCase";
    private const string DefaultNoPowerMessage = "파워가 들어오지 않는다.";
    private const float StartupTimeout = 10f;
    private const float ActionTimeout = 6f;
    private const float PlayerCommandTimeout = 35f;

    private readonly List<string> failures = new();
    private readonly List<string> warnings = new();

    private int passCount;
    private bool actionFinished;

    private GameSession gameSession;
    private MachineRoomTarget machineAction;
    private Target machineTarget;
    private StealthHideTarget toolCaseAction;
    private Target toolCaseTarget;
    private GateController gateController;
    private ScreenShake screenShake;
    private MessagePanel messagePanel;
    private Player player;
    private PlayerRoomTracker playerRoomTracker;
    private TargetCommandRunner targetCommandRunner;

    /// <summary>씬 초기화가 끝난 다음 전체 검증 코루틴을 시작하고 완료 후 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        try
        {
            CollectReferences();
        }
        catch (Exception exception)
        {
            RecordFailure($"검증 Runner 예외: {exception.GetType().Name}: {exception.Message}");
        }

        yield return RunSafely(WaitForGameplayReady());

        if (gameSession != null && gameSession.Phase == GameSessionPhase.Playing)
        {
            try
            {
                VerifyConfiguration();
            }
            catch (Exception exception)
            {
                RecordFailure($"설정 검사 예외: {exception.GetType().Name}: {exception.Message}");
            }

            yield return RunSafely(VerifyNoPowerBranch());
            yield return RunSafely(VerifyPoweredBranch());
            yield return RunSafely(VerifyDuplicateInputThroughRunner());

            try
            {
                VerifyCheckpointStates();
            }
            catch (Exception exception)
            {
                RecordFailure($"체크포인트 검사 예외: {exception.GetType().Name}: {exception.Message}");
            }
        }

        ReportResult();

        // Console 결과를 확인할 수 있도록 한 프레임을 남긴 뒤 이 검증이 시작한 Play Mode만 종료한다.
        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>
    /// 하위 검증 코루틴의 MoveNext 예외를 잡아 결과를 남긴다.
    /// try/catch가 yield를 포함하지 않도록 한 단계씩 실행해 예외 뒤에도 Play Mode를 안전하게 종료한다.
    /// </summary>
    private IEnumerator RunSafely(IEnumerator routine)
    {
        while (true)
        {
            object yieldedValue;
            bool hasNext;
            try
            {
                hasNext = routine != null && routine.MoveNext();
                yieldedValue = hasNext ? routine.Current : null;
            }
            catch (Exception exception)
            {
                RecordFailure($"검증 코루틴 예외: {exception.GetType().Name}: {exception.Message}");
                yield break;
            }

            if (!hasNext)
            {
                yield break;
            }

            yield return yieldedValue;
        }
    }

    /// <summary>현재 씬에서 MachineRoom 검증에 필요한 런타임 참조를 수집한다.</summary>
    private void CollectReferences()
    {
        gameSession = FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);
        machineAction = FindObjectsByType<MachineRoomTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.name == MachineTargetName);
        machineTarget = machineAction != null ? machineAction.GetComponent<Target>() : null;
        toolCaseAction = FindObjectsByType<StealthHideTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.name == ToolCaseTargetName);
        toolCaseTarget = toolCaseAction != null ? toolCaseAction.GetComponent<Target>() : null;
        gateController = FindObjectsByType<GateController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.name == "LogicalGate_CenterHallway_A_B");
        screenShake = FindFirstObjectByType<ScreenShake>(FindObjectsInactive.Include);
        messagePanel = FindFirstObjectByType<MessagePanel>(FindObjectsInactive.Include);
        player = FindFirstObjectByType<Player>(FindObjectsInactive.Include);
        playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        targetCommandRunner = FindFirstObjectByType<TargetCommandRunner>(FindObjectsInactive.Include);
    }

    /// <summary>GameSession이 초기 체크포인트 복원을 끝내고 실제 플레이 상태가 될 때까지 기다린다.</summary>
    private IEnumerator WaitForGameplayReady()
    {
        float elapsed = 0f;
        while (gameSession != null && gameSession.Phase != GameSessionPhase.Playing && elapsed < StartupTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (gameSession == null)
        {
            RecordFailure("GameSession을 찾지 못했습니다.");
            yield break;
        }

        if (gameSession.Phase != GameSessionPhase.Playing)
        {
            RecordFailure($"초기 체크포인트 복원이 제한 시간 안에 끝나지 않았습니다. 현재 Phase={gameSession.Phase}");
            yield break;
        }

        RecordPass("GameSession이 Playing 상태로 초기화되었습니다.");
    }

    /// <summary>Target·연출·ToolCase·17번 Controller 연결이 Play Mode에서도 유효한지 검사한다.</summary>
    private void VerifyConfiguration()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        Check(activeScene.path == "Assets/Scenes/CoreSandbox.unity", $"활성 씬이 CoreSandbox가 아닙니다: {activeScene.path}");
        Check(machineAction != null, $"{MachineTargetName} MachineRoomTarget을 찾지 못했습니다.");
        Check(machineTarget != null, $"{MachineTargetName}의 Target을 찾지 못했습니다.");
        Check(toolCaseAction != null, $"{ToolCaseTargetName} StealthHideTarget을 찾지 못했습니다.");
        Check(toolCaseTarget != null, $"{ToolCaseTargetName}의 Target을 찾지 못했습니다.");
        Check(gateController != null, "LogicalGate_CenterHallway_A_B GateController를 찾지 못했습니다.");
        Check(screenShake != null, "Main Camera의 ScreenShake를 찾지 못했습니다.");
        Check(messagePanel != null, "MessagePanel을 찾지 못했습니다.");

        if (machineTarget != null)
        {
            Check(machineTarget.GetComponent<MachineRoomTarget>() == machineAction,
                $"{MachineTargetName}의 Target Action이 MachineRoomTarget이 아닙니다.");
            Check(machineTarget.OwnerRoom != null && machineTarget.OwnerRoom.RoomId == "S2_MachineRoom",
                $"{MachineTargetName}의 OwnerRoom이 S2_MachineRoom이 아닙니다: {machineTarget.OwnerRoom?.RoomId}");
            Check(machineTarget.GetComponent<TargetPresenter>() != null, $"{MachineTargetName}에 TargetPresenter가 없습니다.");
            Check(machineTarget.GetComponent<BoxCollider>()?.isTrigger == true, $"{MachineTargetName}에 Trigger BoxCollider가 없습니다.");
            Check(machineTarget.TryValidateConfiguration(out string targetError), targetError);
        }

        if (machineAction != null)
        {
            Check(machineAction.CompletionId == MachineTargetName,
                $"MachineRoomTarget CompletionId가 올바르지 않습니다: {machineAction.CompletionId}");
            Check(machineAction.TryValidateConfiguration(out string machineError), machineError);

            AudioClip gateSound = GetObjectReference<AudioClip>(machineAction, "gateActivationSound");
            if (gateSound == null)
            {
                RecordWarning("gateActivationSound가 비어 있습니다. 로직은 실행되지만 실제 철창 SFX는 재생되지 않습니다.");
            }
            else
            {
                RecordPass($"철창 SFX 참조가 연결되었습니다: {gateSound.name}");
            }
        }

        if (gateController != null)
        {
            Check(gateController.TryValidateConfiguration(out string gateError), gateError);
        }

        if (screenShake != null)
        {
            Check(screenShake.IsConfigured, "ScreenShake의 Cinemachine 참조가 구성되지 않았습니다.");
        }

        if (messagePanel != null)
        {
            Check(GetObjectReference<GameObject>(messagePanel, "panelRoot") != null,
                "MessagePanel의 panelRoot 참조가 비어 있습니다.");
            Check(GetObjectReference<Text>(messagePanel, "messageLabel") != null,
                "MessagePanel의 messageLabel 참조가 비어 있습니다.");
            Check(GetObjectReference<Button>(messagePanel, "confirmButton") != null,
                "MessagePanel의 confirmButton 참조가 비어 있습니다.");
        }

        if (toolCaseAction != null)
        {
            Check(toolCaseTarget.GetComponent<StealthHideTarget>() == toolCaseAction,
                $"{ToolCaseTargetName}의 Target Action이 StealthHideTarget이 아닙니다.");
            Check(toolCaseTarget.OwnerRoom != null && toolCaseTarget.OwnerRoom.RoomId == "S2_MachineRoom",
                $"{ToolCaseTargetName}의 OwnerRoom이 S2_MachineRoom이 아닙니다: {toolCaseTarget.OwnerRoom?.RoomId}");
            StealthHideSystem hideSystem = GetObjectReference<StealthHideSystem>(toolCaseAction, "stealthHideSystem");
            Check(hideSystem != null, $"{ToolCaseTargetName}의 StealthHideSystem 참조가 비어 있습니다.");
        }

        Sector2HideEncounterController encounter = FindFirstObjectByType<Sector2HideEncounterController>(FindObjectsInactive.Include);
        if (encounter == null)
        {
            RecordWarning("Sector2HideEncounterController가 없어 17번 이벤트 연결은 검사하지 않았습니다.");
        }
        else
        {
            Target encounterToolCase = GetObjectReference<Target>(encounter, "toolCaseTarget");
            Check(encounterToolCase == toolCaseTarget, "17번 Controller의 toolCaseTarget 참조가 MachineRoom ToolCase와 다릅니다.");
        }
    }

    /// <summary>GeneratorPowered가 없을 때 정확한 메시지만 표시되고 완료되지 않는지 검사한다.</summary>
    private IEnumerator VerifyNoPowerBranch()
    {
        if (gameSession == null || machineAction == null || machineTarget == null || messagePanel == null)
        {
            RecordFailure("전원 부족 분기를 검사할 필수 참조가 없습니다.");
            yield break;
        }

        PrepareMachineState(false, false);
        string expectedMessage = GetSerializedString(machineAction, "noPowerMessage", DefaultNoPowerMessage);
        Text messageLabel = GetObjectReference<Text>(messagePanel, "messageLabel");
        GameObject panelRoot = GetObjectReference<GameObject>(messagePanel, "panelRoot");
        TargetActionContext context = new TargetActionContext(machineTarget, player);

        actionFinished = false;
        StartCoroutine(RunMachineAction(context));

        bool messageDisplayed = false;
        float elapsed = 0f;
        while (!messageDisplayed && elapsed < ActionTimeout)
        {
            messageDisplayed = panelRoot != null && panelRoot.activeSelf && messageLabel != null && messageLabel.text == expectedMessage;
            if (!messageDisplayed)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        Check(messageDisplayed, $"전원 부족 메시지가 표시되지 않았습니다. 기대값: {expectedMessage}");
        Check(!gameSession.HasStoryFlag(StoryProgressFlags.GateActivated), "전원이 없는데 GateActivated가 기록되었습니다.");
        Check(!context.IsCompleted, "전원이 없는데 MachineRoom 상호작용이 완료 처리되었습니다.");
        Check(!machineAction.IsCompleted, "전원이 없는데 MachineRoomTarget이 완료 처리되었습니다.");

        // MessagePanel.Show는 확인 입력까지 대기하므로 자동 확인해 검증 코루틴이 영구 대기하지 않게 한다.
        if (messageDisplayed)
        {
            messagePanel.Dismiss();
        }

        yield return WaitForActionFinished();
        Check(actionFinished, "전원 부족 분기 코루틴이 제한 시간 안에 종료되지 않았습니다.");
    }

    /// <summary>GeneratorPowered가 있으면 연출과 Gate·완료 상태가 실행되는지 검사한다.</summary>
    private IEnumerator VerifyPoweredBranch()
    {
        if (gameSession == null || machineAction == null || machineTarget == null || gateController == null)
        {
            RecordFailure("전원 성공 분기를 검사할 필수 참조가 없습니다.");
            yield break;
        }

        PrepareMachineState(true, false);
        TargetActionContext context = new TargetActionContext(machineTarget, player);
        actionFinished = false;
        StartCoroutine(RunMachineAction(context));
        yield return WaitForActionFinished();

        Check(actionFinished, "전원 성공 분기 코루틴이 제한 시간 안에 종료되지 않았습니다.");
        Check(context.IsCompleted, "전원 성공 후 TargetActionContext가 완료되지 않았습니다.");
        Check(gameSession.HasStoryFlag(StoryProgressFlags.GeneratorPowered), "GeneratorPowered가 유지되지 않았습니다.");
        Check(gameSession.HasStoryFlag(StoryProgressFlags.GateActivated), "전원 성공 후 GateActivated가 기록되지 않았습니다.");
        Check(gateController.IsActivated, "GateController가 GateActivated 상태로 갱신되지 않았습니다.");
        Check(machineAction.IsCompleted, "전원 성공 후 MachineRoomTarget이 완료되지 않았습니다.");
        Check(!machineTarget.IsSelectable, "완료된 MachineRoomTarget이 계속 선택 가능 상태입니다.");
        Check(screenShake != null && screenShake.IsConfigured, "ScreenShake가 실행 가능한 구성 상태가 아닙니다.");
    }

    /// <summary>TargetCommandRunner의 연속 요청 중 두 번째 요청이 거부되는지 검사한다.</summary>
    private IEnumerator VerifyDuplicateInputThroughRunner()
    {
        if (gameSession == null || machineAction == null || machineTarget == null || targetCommandRunner == null ||
            player == null || playerRoomTracker == null || machineTarget.OwnerRoom == null)
        {
            RecordFailure("중복 입력 분기를 검사할 필수 참조가 없습니다.");
            yield break;
        }

        PrepareMachineState(true, false);
        playerRoomTracker.RestoreRoom(machineTarget.OwnerRoom);
        if (!player.WarpTo(machineTarget.ApproachPoint))
        {
            RecordFailure("중복 입력 검사를 위해 Player를 MachineRoom 접근점에 배치하지 못했습니다.");
            yield break;
        }

        // Warp 직후 NavMeshAgent와 논리 방이 안정화될 시간을 한 프레임 제공한다.
        yield return null;
        bool firstAccepted = targetCommandRunner.TryExecute(machineTarget);
        bool secondAccepted = targetCommandRunner.TryExecute(machineTarget);
        Check(firstAccepted, "첫 번째 MachineRoom Target 요청이 접수되지 않았습니다.");
        Check(!secondAccepted, "실행 중 두 번째 MachineRoom Target 요청이 거부되지 않았습니다.");

        float elapsed = 0f;
        while (targetCommandRunner.IsBusy && elapsed < PlayerCommandTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Check(!targetCommandRunner.IsBusy, "중복 입력 검사 후 TargetCommandRunner가 계속 Busy 상태입니다.");
        Check(gameSession.HasStoryFlag(StoryProgressFlags.GateActivated), "Runner를 통한 정상 요청 후 GateActivated가 기록되지 않았습니다.");
        Check(machineAction.IsCompleted, "Runner를 통한 정상 요청 후 MachineRoomTarget이 완료되지 않았습니다.");
    }

    /// <summary>Save1~Save5의 MachineRoom 완료 복원 값이 계획과 일치하는지 검사한다.</summary>
    private void VerifyCheckpointStates()
    {
        CheckpointDefinition[] checkpoints = FindObjectsByType<CheckpointDefinition>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (string checkpointId in new[] { "Save1", "Save2", "Save3", "Save4", "Save5" })
        {
            CheckpointDefinition checkpoint = checkpoints.FirstOrDefault(candidate => candidate.CheckpointId == checkpointId);
            CheckpointCompletedState state = checkpoint?.CompletedStates?
                .FirstOrDefault(candidate => candidate != null && candidate.CompletionId == MachineTargetName);
            bool expected = checkpointId is "Save3" or "Save4" or "Save5";
            Check(state != null && state.Completed == expected,
                $"{checkpointId} MachineRoom 완료 상태가 올바르지 않습니다. 기대값={expected}, 실제값={(state != null ? state.Completed.ToString() : "<missing>")}");
        }
    }

    /// <summary>런타임 분기 검사를 시작하기 전에 MachineRoom 관련 임시 진행 상태를 정리한다.</summary>
    private void PrepareMachineState(bool powered, bool activated)
    {
        if (gameSession == null)
        {
            return;
        }

        gameSession.ClearStoryFlag(StoryProgressFlags.FuseInstalled);
        gameSession.ClearStoryFlag(StoryProgressFlags.GeneratorPowered);
        gameSession.ClearStoryFlag(StoryProgressFlags.GateActivated);

        if (powered)
        {
            gameSession.SetStoryFlag(StoryProgressFlags.FuseInstalled);
            gameSession.SetStoryFlag(StoryProgressFlags.GeneratorPowered);
        }

        if (activated)
        {
            gameSession.SetStoryFlag(StoryProgressFlags.GateActivated);
        }

        machineAction?.RestoreCompletedState(false);
        gateController?.RefreshState();
    }

    /// <summary>MachineRoomTarget 행동을 실행하고 완료 여부를 별도 플래그에 기록한다.</summary>
    private IEnumerator RunMachineAction(TargetActionContext context)
    {
        yield return machineAction.Execute(context);
        actionFinished = true;
    }

    /// <summary>현재 행동 코루틴이 끝나거나 제한 시간에 도달할 때까지 기다린다.</summary>
    private IEnumerator WaitForActionFinished()
    {
        float elapsed = 0f;
        while (!actionFinished && elapsed < ActionTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>모든 검사 결과를 PASS·WARNING·FAIL 요약으로 Console에 출력한다.</summary>
    private void ReportResult()
    {
        if (failures.Count == 0)
        {
            GameLog.Log($"Hospital Sector2 MachineRoom Play Mode 검증 성공: PASS {passCount}건, WARNING {warnings.Count}건.", this);
        }
        else
        {
            GameLog.LogError(
                $"Hospital Sector2 MachineRoom Play Mode 검증 실패: PASS {passCount}건, WARNING {warnings.Count}건, FAIL {failures.Count}건\n- " +
                string.Join("\n- ", failures),
                this);
        }
    }

    /// <summary>조건이 참이면 PASS를 기록하고 거짓이면 FAIL을 기록한다.</summary>
    private void Check(bool condition, string message)
    {
        if (condition)
        {
            RecordPass(message);
        }
        else
        {
            RecordFailure(message);
        }
    }

    /// <summary>성공 검사를 Console에 기록한다.</summary>
    private void RecordPass(string message)
    {
        passCount++;
        GameLog.Log($"[MachineRoom 검증][PASS] {message}", this);
    }

    /// <summary>실패 검사를 목록과 Console에 기록한다.</summary>
    private void RecordFailure(string message)
    {
        failures.Add(message);
        GameLog.LogError($"[MachineRoom 검증][FAIL] {message}", this);
    }

    /// <summary>사람이 조치해야 하는 경고를 목록과 Console에 기록한다.</summary>
    private void RecordWarning(string message)
    {
        warnings.Add(message);
        GameLog.LogWarning($"[MachineRoom 검증][WARNING] {message}", this);
    }

    /// <summary>SerializedObject를 읽기 전용으로 사용해 private Unity 참조를 가져온다.</summary>
    private static T GetObjectReference<T>(UnityEngine.Object target, string propertyName) where T : UnityEngine.Object
    {
        if (target == null)
        {
            return null;
        }

        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        return property != null ? property.objectReferenceValue as T : null;
    }

    /// <summary>SerializedObject를 읽기 전용으로 사용해 private 문자열을 가져온다.</summary>
    private static string GetSerializedString(UnityEngine.Object target, string propertyName, string fallback)
    {
        if (target == null)
        {
            return fallback;
        }

        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        return property != null && !string.IsNullOrEmpty(property.stringValue)
            ? property.stringValue
            : fallback;
    }
}
#endif
