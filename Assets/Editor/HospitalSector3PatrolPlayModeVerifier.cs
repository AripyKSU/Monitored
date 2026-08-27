#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Enemies;
using HauntedHouse.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sector3 살인마 Patrol의 Save3 복원·실제 방 순환을 저장 없이 검증하는 Editor 진입점이다.
/// Play Mode에서만 임시 상태를 변경하고 종료 시 씬과 체크포인트 에셋에는 쓰지 않는다.
/// </summary>
[InitializeOnLoad]
public static class HospitalSector3PatrolPlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalSector3PatrolVerificationPending";

    /// <summary>스크립트 리로드 뒤에도 예약된 Play Mode 검증 콜백을 유지한다.</summary>
    static HospitalSector3PatrolPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox를 저장하지 않은 채 Sector3 Patrol 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector3 Killer Patrol Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Sector3 Patrol 검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning(
                $"Sector3 Patrol 검증은 '{ScenePath}' 씬에서 실행해야 합니다. " +
                $"현재 씬: {SceneManager.GetActiveScene().path}");
            return;
        }

        // Play Mode 종료 시 사라지는 SessionState만 사용해 다른 에이전트의 씬 변경을 보존한다.
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Play Mode 진입 직후 임시 검증 Runner를 생성한다.</summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        new GameObject("HospitalSector3PatrolVerificationRunner")
            .AddComponent<HospitalSector3PatrolVerificationRunner>();
    }
}

/// <summary>Save3 복원 뒤 살인마가 네 방을 순서대로 순찰하는지 검사하는 임시 런타임 Runner다.</summary>
public sealed class HospitalSector3PatrolVerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 15f;
    private const float RestoreTimeout = 15f;
    private const float RoomArrivalTimeout = 45f;

    private readonly List<string> failures = new();
    private int passCount;

    private GameSession gameSession;
    private CheckpointService checkpointService;
    private CheckpointDefinition save3;
    private EnemyController enemy;
    private PatrolRoute route;
    private Room[] expectedRooms;

    /// <summary>세션 초기화 후 구성·Save3 복원·전체 순환을 검사하고 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        yield return WaitForGameplayReady();
        CollectReferences();
        VerifyReferences();

        if (failures.Count == 0)
        {
            yield return VerifySave3RestoreAndRoute();
        }

        ReportResult();
        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>GameSession이 최초 체크포인트 복원을 끝내고 Playing이 될 때까지 기다린다.</summary>
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

    /// <summary>검증에 필요한 Scene 참조를 한 번만 수집한다.</summary>
    private void CollectReferences()
    {
        gameSession ??= FindFirstObjectByType<GameSession>();
        checkpointService = FindFirstObjectByType<CheckpointService>();
        save3 = FindObjectsByType<CheckpointDefinition>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.CheckpointId == "Save3");
        enemy = FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.name == "Enemy") ??
                FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
        route = FindObjectsByType<PatrolRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.name == "Sector3_KillerPatrolRoute");

        expectedRooms = new[]
        {
            FindRoom("S3_DiningRoom"),
            FindRoom("S3_Kitchen"),
            FindRoom("S3_HallwayB"),
            FindRoom("CenterHallway_B")
        };
    }

    /// <summary>Save3·Enemy·Route의 기본 참조와 계획 문서의 순서를 확인한다.</summary>
    private void VerifyReferences()
    {
        Check(gameSession != null, "GameSession 참조가 존재합니다.", "GameSession이 없습니다.");
        Check(checkpointService != null, "CheckpointService 참조가 존재합니다.", "CheckpointService가 없습니다.");
        Check(save3 != null, "Save3 CheckpointDefinition이 존재합니다.", "Save3 CheckpointDefinition이 없습니다.");
        Check(enemy != null, "Sector3 대상 EnemyController가 존재합니다.", "EnemyController가 없습니다.");
        Check(route != null, "Sector3 Killer Patrol Route가 존재합니다.", "Sector3 Killer Patrol Route가 없습니다.");

        for (int index = 0; index < expectedRooms.Length; index++)
        {
            Check(expectedRooms[index] != null,
                $"순찰 Room {index}번 참조가 존재합니다.",
                $"순찰 Room {index}번 참조가 없습니다.");
        }

        if (route != null)
        {
            Check(route.Count == expectedRooms.Length,
                "PatrolRoute가 네 개의 순찰 지점을 가집니다.",
                $"PatrolRoute 지점 수가 {expectedRooms.Length}개가 아닙니다: {route.Count}");

            if (route.Count == expectedRooms.Length)
            {
                for (int index = 0; index < route.Count; index++)
                {
                    PatrolPoint point = route.GetPoint(index);
                    Check(point != null && point.Room == expectedRooms[index],
                        $"PatrolPoint {index}가 계획된 Room에 연결됐습니다.",
                        $"PatrolPoint {index}의 Room 순서가 올바르지 않습니다.");
                }
            }
        }

        if (save3 != null)
        {
            Check(save3.StoryStage == StoryStage.Sector3,
                "Save3 StoryStage가 Sector3입니다.",
                "Save3 StoryStage가 Sector3가 아닙니다.");
            Check(save3.EnemyStates != null && save3.EnemyStates.Length == 1,
                "Save3 EnemyState가 하나 연결됐습니다.",
                "Save3 EnemyState가 정확히 하나가 아닙니다.");

            if (save3.EnemyStates != null && save3.EnemyStates.Length == 1)
            {
                Check(save3.EnemyStates[0].Enemy == enemy,
                    "Save3 EnemyState가 Sector3 Enemy를 참조합니다.",
                    "Save3 EnemyState의 Enemy 참조가 다릅니다.");
                Check(save3.EnemyStates[0].Active,
                    "Save3 EnemyState가 활성으로 저장됐습니다.",
                    "Save3 EnemyState가 비활성으로 저장됐습니다.");
                Check(save3.EnemyStates[0].PatrolRoute == route,
                    "Save3 EnemyState가 Sector3 Route를 참조합니다.",
                    "Save3 EnemyState의 PatrolRoute 참조가 다릅니다.");
                Check(save3.EnemyStates[0].RouteStartIndex == 0,
                    "Save3 Patrol 시작 인덱스가 0입니다.",
                    "Save3 Patrol 시작 인덱스가 0이 아닙니다.");
            }
        }
    }

    /// <summary>Save3를 복원한 뒤 Enemy가 첫 지점에서 시작해 전체 Room Route를 한 바퀴 도는지 검사한다.</summary>
    private IEnumerator VerifySave3RestoreAndRoute()
    {
        // 체크포인트를 복원하기 전에 실제 CenterHallway_B 최초 진입과 동일한
        // Story Stage 전환을 임시로 발생시켜 정상 플레이 활성화 어댑터를 검사한다.
        gameSession.SetStoryFlag(StoryProgressFlags.Sector2HideEncounterCompleted);
        gameSession.SetStoryFlag(StoryProgressFlags.CenterHallSecondEntered);
        gameSession.SetStoryStage(StoryStage.Sector3);
        Check(enemy.Mode == EnemyMode.Patrol,
            "정상 Sector3 진입에서 Enemy Mode가 Patrol로 전환됐습니다.",
            $"정상 Sector3 진입 후 Enemy Mode가 Patrol이 아닙니다: {enemy.Mode}");
        Check(enemy.CurrentRoom == expectedRooms[0],
            "정상 Sector3 진입에서 Enemy가 DiningRoom에서 시작합니다.",
            $"정상 Sector3 진입 후 Enemy 시작 Room이 올바르지 않습니다: {enemy.CurrentRoom?.RoomId ?? "null"}");

        // 이후 Save3 Restart 검증이 저장된 상태만 검사하도록 임시 정상 진입 상태를 정리한다.
        enemy.SetActive(false);

        if (!checkpointService.ActivateCheckpoint(save3))
        {
            Fail("Save3 CheckpointDefinition 활성화에 실패했습니다.");
            yield break;
        }

        Check(checkpointService.RestoreActiveCheckpoint(),
            "Save3 Restart 복원을 시작했습니다.",
            "Save3 Restart 복원을 시작하지 못했습니다.");
        yield return WaitForRestore();

        if (failures.Count > 0)
        {
            yield break;
        }

        Check(gameSession.CurrentStoryStage == StoryStage.Sector3,
            "Save3 복원 후 StoryStage가 Sector3입니다.",
            "Save3 복원 후 StoryStage가 Sector3가 아닙니다.");
        Check(enemy.CurrentRoute == route,
            "Save3 복원 후 Sector3 Patrol Route가 적용됐습니다.",
            "Save3 복원 후 다른 Patrol Route가 적용됐습니다.");
        Check(enemy.Mode == EnemyMode.Patrol,
            "Save3 복원 후 Enemy Mode가 Patrol입니다.",
            $"Save3 복원 후 Enemy Mode가 Patrol이 아닙니다: {enemy.Mode}");
        Check(enemy.CurrentRoom == expectedRooms[0],
            "Save3 복원 후 Enemy가 DiningRoom에서 시작합니다.",
            $"Save3 복원 후 Enemy 시작 Room이 올바르지 않습니다: {enemy.CurrentRoom?.RoomId ?? "null"}");

        for (int index = 1; index < expectedRooms.Length; index++)
        {
            yield return WaitForRoom(expectedRooms[index]);
        }

        yield return WaitForRoom(expectedRooms[0]);
        Check(enemy.Mode == EnemyMode.Patrol,
            "전체 Route 한 바퀴 후에도 Enemy가 Patrol 상태입니다.",
            $"전체 Route 한 바퀴 후 Enemy Mode가 Patrol이 아닙니다: {enemy.Mode}");
    }

    /// <summary>Save3 복원 코루틴이 Playing으로 돌아올 때까지 기다린다.</summary>
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

    /// <summary>지정한 논리 Room에 Enemy가 도착할 때까지 기다린다.</summary>
    private IEnumerator WaitForRoom(Room targetRoom)
    {
        if (targetRoom == null)
        {
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + RoomArrivalTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (enemy.CurrentRoom == targetRoom)
            {
                Pass($"Enemy가 '{targetRoom.RoomId}'에 도착했습니다.");
                yield break;
            }

            yield return null;
        }

        Fail(
            $"Enemy가 {RoomArrivalTimeout:0}초 안에 '{targetRoom.RoomId}'에 도착하지 못했습니다. " +
            $"현재 Room: {enemy.CurrentRoom?.RoomId ?? "null"}");
    }

    /// <summary>RoomId로 씬의 논리 Room을 찾는다.</summary>
    private static Room FindRoom(string roomId)
    {
        return FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.RoomId == roomId);
    }

    /// <summary>조건을 기록하고 실패 시 오류를 누적한다.</summary>
    private void Check(bool condition, string success, string failure)
    {
        if (condition) Pass(success);
        else Fail(failure);
    }

    /// <summary>검증 성공 항목을 개발 로그로 남긴다.</summary>
    private void Pass(string message)
    {
        passCount++;
        GameLog.Log($"[Sector3 Patrol 검증 PASS] {message}", this);
    }

    /// <summary>검증 실패 항목을 오류 로그로 남긴다.</summary>
    private void Fail(string message)
    {
        failures.Add(message);
        GameLog.LogError($"[Sector3 Patrol 검증 FAIL] {message}", this);
    }

    /// <summary>전체 검증 결과를 한 줄 요약으로 출력한다.</summary>
    private void ReportResult()
    {
        if (failures.Count == 0)
        {
            GameLog.Log($"[Sector3 Patrol 검증 완료] PASS {passCount}, FAIL 0", this);
            return;
        }

        GameLog.LogError(
            $"[Sector3 Patrol 검증 완료] PASS {passCount}, FAIL {failures.Count}\n- {string.Join("\n- ", failures)}",
            this);
    }
}
#endif
