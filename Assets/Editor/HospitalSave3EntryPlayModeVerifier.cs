#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 18번 Save3 진입 규칙과 체크포인트 복원을 실제 Play Mode에서 검증하는 에디터 진입점이다.
/// 씬을 저장하지 않고 임시 런타임 상태만 변경하여 다른 작업자의 씬 변경사항을 보존한다.
/// </summary>
[InitializeOnLoad]
public static class HospitalSave3EntryPlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalSave3EntryVerificationPending";

    /// <summary>스크립트 리로드 뒤에도 예약된 Play Mode 검증 콜백을 유지한다.</summary>
    static HospitalSave3EntryPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox 씬을 저장하지 않은 채 Play Mode 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital 18 Save3 Entry Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Save3 진입 검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning($"Save3 진입 검증은 CoreSandbox 씬에서만 실행할 수 있습니다. 현재 씬: {SceneManager.GetActiveScene().path}");
            return;
        }

        // 검증 데이터는 Play Mode 종료 시 폐기되며 열린 씬이나 에셋을 저장하지 않는다.
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Play Mode 진입이 완료되면 임시 검증 Runner를 생성한다.</summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        new GameObject("HospitalSave3EntryVerificationRunner")
            .AddComponent<HospitalSave3EntryVerificationRunner>();
    }
}

/// <summary>
/// Save3 설정, 선행 조건, 최초 진입 일회성 및 Restart 복원 결과를 순서대로 검사하는 임시 Runner다.
/// </summary>
public sealed class HospitalSave3EntryVerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 10f;
    private const float RestoreTimeout = 10f;

    private readonly List<string> failures = new();
    private int passCount;

    private GameSession gameSession;
    private CheckpointService checkpointService;
    private CheckpointResolver checkpointResolver;
    private PlayerRoomTracker playerRoomTracker;
    private Sector2HideEncounterController hideEncounter;
    private CheckpointDefinition save3;
    private Room centerHallwayA;
    private Room centerHallwayB;
    private Doorway gate;

    /// <summary>씬 초기화를 기다린 뒤 검증을 수행하고 이 검증이 시작한 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        yield return WaitForGameplayReady();

        if (gameSession != null && gameSession.Phase == GameSessionPhase.Playing)
        {
            CollectReferences();
            VerifyConfiguration();

            if (failures.Count == 0)
            {
                yield return VerifyFirstEntryAndRestore();
            }
        }

        ReportResult();
        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>GameSession의 최초 체크포인트 복원이 끝날 때까지 기다린다.</summary>
    private IEnumerator WaitForGameplayReady()
    {
        float deadline = Time.realtimeSinceStartup + StartupTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            if (gameSession != null && gameSession.Phase == GameSessionPhase.Playing)
            {
                Pass("GameSession이 Playing 상태로 초기화되었습니다.");
                yield break;
            }

            yield return null;
        }

        Fail("GameSession이 제한 시간 안에 Playing 상태가 되지 않았습니다.");
    }

    /// <summary>Save3 진입과 복원 검사에 필요한 씬 참조를 수집한다.</summary>
    private void CollectReferences()
    {
        checkpointService = FindFirstObjectByType<CheckpointService>();
        checkpointResolver = FindFirstObjectByType<CheckpointResolver>();
        playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
        hideEncounter = FindFirstObjectByType<Sector2HideEncounterController>(FindObjectsInactive.Include);

        CheckpointDefinition[] checkpoints = FindObjectsByType<CheckpointDefinition>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        save3 = checkpoints.FirstOrDefault(candidate => candidate.CheckpointId == "Save3");

        Room[] rooms = FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        centerHallwayA = rooms.FirstOrDefault(candidate => candidate.RoomId == "CenterHallway_A");
        centerHallwayB = rooms.FirstOrDefault(candidate => candidate.RoomId == "CenterHallway_B");

        gate = FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate =>
                candidate != null &&
                ((candidate.RoomA == centerHallwayA && candidate.RoomB == centerHallwayB) ||
                 (candidate.RoomA == centerHallwayB && candidate.RoomB == centerHallwayA)));
    }

    /// <summary>Save3 정의와 Resolver 규칙이 18번 명세대로 직렬화됐는지 검사한다.</summary>
    private void VerifyConfiguration()
    {
        Check(gameSession != null, "GameSession 참조가 존재합니다.", "GameSession이 없습니다.");
        Check(checkpointService != null, "CheckpointService 참조가 존재합니다.", "CheckpointService가 없습니다.");
        Check(checkpointResolver != null, "CheckpointResolver 참조가 존재합니다.", "CheckpointResolver가 없습니다.");
        Check(playerRoomTracker != null, "PlayerRoomTracker 참조가 존재합니다.", "PlayerRoomTracker가 없습니다.");
        Check(centerHallwayB != null, "CenterHallway_B 논리 방이 존재합니다.", "CenterHallway_B 논리 방이 없습니다.");
        Check(save3 != null, "Save3 CheckpointDefinition이 존재합니다.", "Save3 CheckpointDefinition이 없습니다.");

        if (save3 != null)
        {
            Check(save3.TryValidateConfiguration(out string error),
                "Save3 CheckpointDefinition 구성이 유효합니다.", error);
            Check(save3.RespawnRoom == centerHallwayB,
                "Save3 RespawnRoom이 CenterHallway_B입니다.", "Save3 RespawnRoom이 CenterHallway_B가 아닙니다.");
            Check(save3.StoryStage == StoryStage.Sector3,
                "Save3 StoryStage가 Sector3입니다.", "Save3 StoryStage가 Sector3가 아닙니다.");
            Check(save3.ProgressFlags.Contains(StoryProgressFlags.Sector2HideEncounterCompleted),
                "Save3가 Sector2 숨기 이벤트 완료 flag를 복원합니다.", "Save3에 Sector2HideEncounterCompleted가 없습니다.");
            Check(save3.ProgressFlags.Contains(StoryProgressFlags.CenterHallSecondEntered),
                "Save3가 두 번째 중앙 복도 진입 flag를 복원합니다.", "Save3에 CenterHallSecondEntered가 없습니다.");
        }

        VerifyResolverRule();
    }

    /// <summary>CenterHallway_B 진입을 Save3로 연결하는 Resolver 규칙의 필드를 검사한다.</summary>
    private void VerifyResolverRule()
    {
        if (checkpointResolver == null)
        {
            return;
        }

        SerializedProperty rules = new SerializedObject(checkpointResolver).FindProperty("rules");
        bool found = false;
        for (int index = 0; index < rules.arraySize; index++)
        {
            SerializedProperty rule = rules.GetArrayElementAtIndex(index);
            if (rule.FindPropertyRelative("eventType").enumValueIndex != (int)StoryEventType.RoomEntered ||
                rule.FindPropertyRelative("subjectId").stringValue != "CenterHallway_B")
            {
                continue;
            }

            found = true;
            Check(rule.FindPropertyRelative("requiredFlagId").stringValue == StoryProgressFlags.Sector2HideEncounterCompleted,
                "Save3 규칙이 Sector2 숨기 이벤트 완료를 요구합니다.", "Save3 규칙의 선행 flag가 올바르지 않습니다.");
            Check(rule.FindPropertyRelative("flagId").stringValue == StoryProgressFlags.CenterHallSecondEntered,
                "Save3 규칙이 CenterHallSecondEntered를 기록합니다.", "Save3 규칙의 결과 flag가 올바르지 않습니다.");
            Check(rule.FindPropertyRelative("checkpoint").objectReferenceValue == save3,
                "Save3 규칙이 Save3 정의를 활성화합니다.", "Save3 규칙의 체크포인트 참조가 올바르지 않습니다.");
            break;
        }

        Check(found, "CenterHallway_B 진입 Resolver 규칙이 존재합니다.", "CenterHallway_B 진입 Resolver 규칙이 없습니다.");
    }

    /// <summary>선행 flag 설정 후 최초·재진입과 Restart 복원 결과를 검사한다.</summary>
    private IEnumerator VerifyFirstEntryAndRestore()
    {
        int checkpointChangedCount = 0;
        checkpointService.CheckpointChanged += HandleCheckpointChanged;
        try
        {
            // 17번 완료 직후의 상태를 구성하고 물리 이동 대신 동일한 RoomEntered 사건을 직접 발행한다.
            gameSession.SetStoryFlag(StoryProgressFlags.Sector2HideEncounterCompleted);
            gameSession.ClearStoryFlag(StoryProgressFlags.CenterHallSecondEntered);
            gameSession.PublishRoomEntered(centerHallwayB);

            Check(gameSession.HasStoryFlag(StoryProgressFlags.CenterHallSecondEntered),
                "최초 진입이 CenterHallSecondEntered를 기록했습니다.", "최초 진입 flag가 기록되지 않았습니다.");
            Check(checkpointService.ActiveCheckpoint == save3,
                "최초 진입이 Save3를 활성화했습니다.", "최초 진입 후 활성 체크포인트가 Save3가 아닙니다.");
            Check(gameSession.CurrentStoryStage == StoryStage.Sector3,
                "최초 진입이 StoryStage를 Sector3로 전환했습니다.", "최초 진입 후 StoryStage가 Sector3가 아닙니다.");
            Check(checkpointChangedCount == 1,
                "최초 진입에서 체크포인트가 한 번만 변경됐습니다.", $"최초 진입 변경 횟수가 {checkpointChangedCount}회입니다.");

            // 동일 사건을 다시 발행해 완료 flag가 규칙 재실행을 차단하는지 확인한다.
            gameSession.PublishRoomEntered(centerHallwayB);
            Check(checkpointChangedCount == 1,
                "CenterHallway_B 재진입이 Save3를 다시 활성화하지 않았습니다.",
                $"재진입 후 체크포인트 변경 횟수가 {checkpointChangedCount}회입니다.");

            Check(checkpointService.RestoreActiveCheckpoint(),
                "Save3 Restart 복원을 시작했습니다.", "Save3 Restart 복원을 시작하지 못했습니다.");
            yield return WaitForRestore();

            Check(playerRoomTracker.CurrentRoom == centerHallwayB,
                "Restart 후 논리 방이 CenterHallway_B입니다.", "Restart 후 논리 방이 CenterHallway_B가 아닙니다.");
            Check(gameSession.CurrentStoryStage == StoryStage.Sector3,
                "Restart 후 StoryStage가 Sector3입니다.", "Restart 후 StoryStage가 Sector3가 아닙니다.");
            Check(gameSession.HasStoryFlag(StoryProgressFlags.CenterHallSecondEntered),
                "Restart 후 CenterHallSecondEntered가 유지됩니다.", "Restart 후 CenterHallSecondEntered가 누락됐습니다.");
            Check(gameSession.HasStoryFlag(StoryProgressFlags.Sector2HideEncounterCompleted),
                "Restart 후 Sector2 숨기 이벤트 완료 상태가 유지됩니다.", "Restart 후 숨기 이벤트 완료 flag가 누락됐습니다.");
            Check(gate != null && !gate.IsLocked,
                "Restart 후 CenterHallway 철창을 통과할 수 있습니다.", "Restart 후 철창이 없거나 잠겨 있습니다.");

            if (hideEncounter != null)
            {
                Check(!hideEncounter.ShouldStartEncounter && hideEncounter.StateName == "Completed",
                    "Restart 후 Sector2 scripted killer 이벤트가 완료 상태로 유지됩니다.",
                    $"Restart 후 숨기 이벤트 상태가 올바르지 않습니다: {hideEncounter.StateName}");
            }
            else
            {
                Fail("Sector2HideEncounterController가 없어 재실행 금지 상태를 확인할 수 없습니다.");
            }
        }
        finally
        {
            checkpointService.CheckpointChanged -= HandleCheckpointChanged;
        }

        void HandleCheckpointChanged(CheckpointDefinition checkpoint)
        {
            checkpointChangedCount++;
        }
    }

    /// <summary>Restart 복원이 끝나 GameSession이 다시 Playing이 될 때까지 기다린다.</summary>
    private IEnumerator WaitForRestore()
    {
        float deadline = Time.realtimeSinceStartup + RestoreTimeout;
        bool restoringObserved = false;
        while (Time.realtimeSinceStartup < deadline)
        {
            restoringObserved |= gameSession.Phase == GameSessionPhase.Restoring;
            if (restoringObserved && gameSession.Phase == GameSessionPhase.Playing)
            {
                Pass("Save3 Restart 복원이 제한 시간 안에 완료됐습니다.");
                yield break;
            }

            yield return null;
        }

        Fail("Save3 Restart 복원이 제한 시간 안에 완료되지 않았습니다.");
    }

    /// <summary>조건에 따라 성공 또는 실패 결과를 기록한다.</summary>
    private void Check(bool condition, string success, string failure)
    {
        if (condition) Pass(success);
        else Fail(failure);
    }

    /// <summary>성공 항목 수를 증가시키고 개발 로그를 남긴다.</summary>
    private void Pass(string message)
    {
        passCount++;
        GameLog.Log($"[Save3 검증 PASS] {message}", this);
    }

    /// <summary>실패 원인을 수집하고 오류 로그를 남긴다.</summary>
    private void Fail(string message)
    {
        failures.Add(message);
        GameLog.LogError($"[Save3 검증 FAIL] {message}", this);
    }

    /// <summary>전체 검증 결과를 한 줄 요약으로 출력한다.</summary>
    private void ReportResult()
    {
        if (failures.Count == 0)
        {
            GameLog.Log($"[Save3 검증 완료] PASS {passCount}, FAIL 0", this);
            return;
        }

        GameLog.LogError(
            $"[Save3 검증 완료] PASS {passCount}, FAIL {failures.Count}\n- {string.Join("\n- ", failures)}",
            this);
    }
}
#endif
