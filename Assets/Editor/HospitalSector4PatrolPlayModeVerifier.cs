#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Enemies;
using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sector4 살인마 Patrol의 정상 진입, Save4·Save5 복원, 9개 지점 순환을 저장 없이 검증하는 Editor 진입점이다.
/// Play Mode에서만 임시 상태를 변경하고 종료 시 씬 직렬화 데이터에는 쓰지 않는다.
/// </summary>
[InitializeOnLoad]
public static class HospitalSector4PatrolPlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalSector4PatrolVerificationPending";

    /// <summary>스크립트 리로드 뒤에도 Play Mode 진입 콜백을 유지한다.</summary>
    static HospitalSector4PatrolPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox에서 Sector4 Patrol Play Mode 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector4 Killer Patrol Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Sector4 Patrol 검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning(
                $"Sector4 Patrol 검증은 '{ScenePath}' 씬에서 실행해야 합니다. " +
                $"현재 씬: {SceneManager.GetActiveScene().path}");
            return;
        }

        // Play Mode 종료 시 사라지는 SessionState만 사용해 공용 씬 변경을 보존한다.
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Play Mode 진입 직후 임시 검증 Runner를 한 번 생성한다.</summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        new GameObject("HospitalSector4PatrolVerificationRunner")
            .AddComponent<HospitalSector4PatrolVerificationRunner>();
    }
}

/// <summary>Sector4 Patrol의 진행 전환과 실제 지점별 대기·순환을 검사하는 임시 런타임 Runner다.</summary>
public sealed class HospitalSector4PatrolVerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 20f;
    private const float RestoreTimeout = 20f;
    private const float PointArrivalTimeout = 90f;
    private const float PointPositionTolerance = 2.0f;
    private const float WaitTolerance = 0.9f;

    private static readonly string[] RouteRoomIds =
    {
        "S4_MorgueRoom2",
        "S4_MorgueRoom1",
        "S4_BasementCorridor",
        "S4_Workshop",
        "S4_BasementCorridor",
        "S4_ControlRoom",
        "S4_BasementCorridor",
        "S4_Storage",
        "S4_BasementCorridor"
    };

    private readonly List<string> failures = new();
    private int passCount;
    private bool chaseWasEnabled;
    private EnemyChaseSystem enemyChaseSystem;
    private GameSession gameSession;
    private CheckpointService checkpointService;
    private CheckpointDefinition save4;
    private CheckpointDefinition save5;
    private EnemyController enemy;
    private PatrolRoute route;

    /// <summary>세션 초기화 후 정상 진입, 체크포인트 복원과 전체 경로를 검사하고 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        yield return WaitForGameplayReady();
        CollectReferences();
        VerifyReferences();

        if (failures.Count == 0)
        {
            DisableChaseSystem();
            if (failures.Count == 0)
            {
                yield return VerifyNormalSector4Entry();
            }

            if (failures.Count == 0)
            {
                yield return VerifySave4RestoreAndRoute();
            }

            if (failures.Count == 0)
            {
                yield return VerifySave5Restore();
            }
        }

        RestoreChaseSystem();
        ReportResult();
        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>초기 체크포인트 복원이 끝나고 GameSession이 Playing이 될 때까지 기다린다.</summary>
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

    /// <summary>검증에 필요한 공용 시스템과 Sector4 전용 참조를 수집한다.</summary>
    private void CollectReferences()
    {
        gameSession ??= FindFirstObjectByType<GameSession>();
        checkpointService = FindFirstObjectByType<CheckpointService>();
        enemyChaseSystem = FindFirstObjectByType<EnemyChaseSystem>();
        save4 = FindCheckpoint("Save4");
        save5 = FindCheckpoint("Save5");
        enemy = FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.name == "Enemy") ??
                FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
        route = FindObjectsByType<PatrolRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.name == "Sector4_KillerPatrolRoute");
    }

    /// <summary>경로, 체크포인트, Enemy 참조와 계획된 Room 순서를 확인한다.</summary>
    private void VerifyReferences()
    {
        Check(gameSession != null, "GameSession 참조가 존재합니다.", "GameSession이 없습니다.");
        Check(checkpointService != null, "CheckpointService 참조가 존재합니다.", "CheckpointService가 없습니다.");
        Check(save4 != null, "Save4 CheckpointDefinition이 존재합니다.", "Save4 CheckpointDefinition이 없습니다.");
        Check(save5 != null, "Save5 CheckpointDefinition이 존재합니다.", "Save5 CheckpointDefinition이 없습니다.");
        Check(enemy != null, "Sector4 대상 EnemyController가 존재합니다.", "EnemyController가 없습니다.");
        Check(route != null, "Sector4 Killer Patrol Route가 존재합니다.", "Sector4 Killer Patrol Route가 없습니다.");

        if (route == null)
        {
            return;
        }

        Check(route.Count == RouteRoomIds.Length,
            "Sector4 PatrolRoute가 9개 지점을 가집니다.",
            $"Sector4 PatrolRoute 지점 수가 9개가 아닙니다: {route.Count}");
        if (route.Count != RouteRoomIds.Length)
        {
            return;
        }

        for (int index = 0; index < route.Count; index++)
        {
            PatrolPoint point = route.GetPoint(index);
            Check(point != null && point.Room != null && point.Room.RoomId == RouteRoomIds[index],
                $"PatrolPoint {index}가 '{RouteRoomIds[index]}'에 연결됐습니다.",
                $"PatrolPoint {index}의 Room 순서가 올바르지 않습니다.");
            Check(point != null && Mathf.Abs(point.WaitDuration - 6f) <= 0.01f,
                $"PatrolPoint {index}의 대기 시간이 6초입니다.",
                $"PatrolPoint {index}의 대기 시간이 6초가 아닙니다.");
        }

        VerifyCheckpointEnemyState(save4, "Save4");
        VerifyCheckpointEnemyState(save5, "Save5");
    }

    /// <summary>Save4 복원과 동일한 BasementEntered·Sector4 전환에서 Patrol이 활성화되는지 검사한다.</summary>
    private IEnumerator VerifyNormalSector4Entry()
    {
        // 실제 계단 경계가 이미 BasementEntered를 기록한 상황을 임시로 재현한다.
        gameSession.SetStoryFlag(StoryProgressFlags.BasementKeyAcquired);
        gameSession.SetStoryFlag(StoryProgressFlags.BasementEntered);
        gameSession.SetStoryStage(StoryStage.Sector4);
        yield return null;

        Check(enemy.Mode == EnemyMode.Patrol,
            "정상 Sector4 진입에서 Enemy Mode가 Patrol로 전환됐습니다.",
            $"정상 Sector4 진입 후 Enemy Mode가 Patrol이 아닙니다: {enemy.Mode}");
        Check(enemy.CurrentRoute == route,
            "정상 Sector4 진입에서 Sector4 PatrolRoute가 적용됐습니다.",
            "정상 Sector4 진입 후 Sector4 PatrolRoute가 적용되지 않았습니다.");
        Check(enemy.CurrentRoom != null && enemy.CurrentRoom.RoomId == "S4_MorgueRoom2",
            "정상 Sector4 진입에서 Enemy가 MorgueRoom2에서 시작합니다.",
            $"정상 Sector4 진입 후 Enemy 시작 Room이 올바르지 않습니다: {enemy.CurrentRoom?.RoomId ?? "null"}");

        // 다음 체크포인트 검증은 저장된 상태 자체를 관찰하므로 정상 진입 이동을 잠시 중단한다.
        enemy.SetActive(false);
    }

    /// <summary>Save4를 복원한 뒤 첫 지점과 9개 PatrolPoint의 도착·6초 대기를 검사한다.</summary>
    private IEnumerator VerifySave4RestoreAndRoute()
    {
        if (!checkpointService.ActivateCheckpoint(save4))
        {
            Fail("Save4 CheckpointDefinition 활성화에 실패했습니다.");
            yield break;
        }

        Check(checkpointService.RestoreActiveCheckpoint(),
            "Save4 Restart 복원을 시작했습니다.",
            "Save4 Restart 복원을 시작하지 못했습니다.");
        yield return WaitForRestore();
        if (failures.Count > 0)
        {
            yield break;
        }

        Check(gameSession.CurrentStoryStage == StoryStage.Sector4,
            "Save4 복원 후 StoryStage가 Sector4입니다.",
            $"Save4 복원 후 StoryStage가 Sector4가 아닙니다: {gameSession.CurrentStoryStage}");
        Check(enemy.CurrentRoute == route,
            "Save4 복원 후 Sector4 PatrolRoute가 적용됐습니다.",
            "Save4 복원 후 다른 PatrolRoute가 적용됐습니다.");
        Check(enemy.Mode == EnemyMode.Patrol,
            "Save4 복원 후 Enemy Mode가 Patrol입니다.",
            $"Save4 복원 후 Enemy Mode가 Patrol이 아닙니다: {enemy.Mode}");
        Check(enemy.CurrentRoom != null && enemy.CurrentRoom.RoomId == RouteRoomIds[0],
            "Save4 복원 후 Enemy가 MorgueRoom2에서 시작합니다.",
            $"Save4 복원 후 Enemy 시작 Room이 올바르지 않습니다: {enemy.CurrentRoom?.RoomId ?? "null"}");

        for (int index = 0; index < route.Count; index++)
        {
            yield return WaitForPointAndMeasureWait(route.GetPoint(index), index);
            if (failures.Count > 0)
            {
                yield break;
            }
        }

        Check(enemy.Mode == EnemyMode.Patrol,
            "Sector4 Patrol Route 한 바퀴 후에도 Enemy가 Patrol 상태입니다.",
            $"Sector4 Patrol Route 한 바퀴 후 Enemy Mode가 Patrol이 아닙니다: {enemy.Mode}");
    }

    /// <summary>Save5 복원 시에도 Sector4 적 Route가 유지되고 FinalChase가 자동 시작되지 않는지 검사한다.</summary>
    private IEnumerator VerifySave5Restore()
    {
        if (!checkpointService.ActivateCheckpoint(save5))
        {
            Fail("Save5 CheckpointDefinition 활성화에 실패했습니다.");
            yield break;
        }

        Check(checkpointService.RestoreActiveCheckpoint(),
            "Save5 Restart 복원을 시작했습니다.",
            "Save5 Restart 복원을 시작하지 못했습니다.");
        yield return WaitForRestore();
        if (failures.Count > 0)
        {
            yield break;
        }

        Check(gameSession.CurrentStoryStage == StoryStage.Sector5Ready,
            "Save5 복원 후 StoryStage가 Sector5Ready입니다.",
            $"Save5 복원 후 StoryStage가 Sector5Ready가 아닙니다: {gameSession.CurrentStoryStage}");
        Check(enemy.CurrentRoute == route,
            "Save5 복원 후 Sector4 PatrolRoute가 유지됩니다.",
            "Save5 복원 후 Sector4 PatrolRoute가 유지되지 않습니다.");
        Check(enemy.Mode == EnemyMode.Patrol,
            "Save5 복원 후 Enemy Mode가 Patrol입니다.",
            $"Save5 복원 후 Enemy Mode가 Patrol이 아닙니다: {enemy.Mode}");
        Check(enemy.CurrentRoom != null && enemy.CurrentRoom.RoomId == RouteRoomIds[0],
            "Save5 복원 후 Enemy가 MorgueRoom2에서 시작합니다.",
            $"Save5 복원 후 Enemy 시작 Room이 올바르지 않습니다: {enemy.CurrentRoom?.RoomId ?? "null"}");
        Check(!gameSession.HasStoryFlag(StoryProgressFlags.FinalChaseStarted),
            "Save5 복원 후 FinalChaseStarted가 자동으로 켜지지 않습니다.",
            "Save5 복원 후 FinalChaseStarted가 자동으로 켜졌습니다.");
    }

    /// <summary>복원 코루틴이 Restoring을 거쳐 Playing으로 돌아올 때까지 기다린다.</summary>
    private IEnumerator WaitForRestore()
    {
        float deadline = Time.realtimeSinceStartup + RestoreTimeout;
        bool restoringObserved = false;
        while (Time.realtimeSinceStartup < deadline)
        {
            restoringObserved |= gameSession.Phase == GameSessionPhase.Restoring;
            if (restoringObserved && gameSession.Phase == GameSessionPhase.Playing)
            {
                Pass("체크포인트 Restart 복원이 제한 시간 안에 완료됐습니다.");
                yield break;
            }

            yield return null;
        }

        Fail("체크포인트 Restart 복원이 제한 시간 안에 완료되지 않았습니다.");
    }

    /// <summary>
    /// 특정 PatrolPoint에 도착한 뒤 State가 Waiting인 약 6초 동안 유지되는지 측정한다.
    /// 순찰 타이머가 Time.deltaTime을 사용하므로, 에디터 프레임 지연에 흔들리지 않도록
    /// 실제 경과 시간이 아닌 게임 시간(Time.time)으로 대기 시간을 비교한다.
    /// </summary>
    private IEnumerator WaitForPointAndMeasureWait(PatrolPoint expectedPoint, int index)
    {
        if (expectedPoint == null)
        {
            Fail($"PatrolPoint {index} 참조가 없어 도착 검사를 할 수 없습니다.");
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + PointArrivalTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            bool arrived = enemy.State == EnemyState.Waiting &&
                           Vector3.Distance(enemy.transform.position, expectedPoint.Position) <= PointPositionTolerance;
            if (arrived)
            {
                // EnemyController.UpdateWaiting이 Time.deltaTime으로 waitRemaining을 차감하므로
                // 동일한 게임 시간 축을 사용해 대기 시간 측정값과 실제 동작을 일치시킨다.
                float waitStarted = Time.time;
                while (enemy.State == EnemyState.Waiting && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                float elapsed = Time.time - waitStarted;
                Check(Mathf.Abs(elapsed - expectedPoint.WaitDuration) <= WaitTolerance,
                    $"PatrolPoint {index} '{expectedPoint.name}'에서 {elapsed:0.00}초 대기했습니다.",
                    $"PatrolPoint {index} '{expectedPoint.name}' 대기 시간이 {elapsed:0.00}초로 6초와 다릅니다.");
                Pass($"Enemy가 PatrolPoint {index} '{expectedPoint.name}'에 도착했습니다.");
                yield break;
            }

            yield return null;
        }

        Fail(
            $"Enemy가 {PointArrivalTimeout:0}초 안에 PatrolPoint {index} '{expectedPoint.name}'에 도착하지 못했습니다. " +
            $"현재 Room: {enemy.CurrentRoom?.RoomId ?? "null"}, State: {enemy.State}, 위치: {enemy.transform.position}");
    }

    /// <summary>EnemyChaseSystem이 순찰 검증을 방해하지 않도록 일시 중지한다.</summary>
    private void DisableChaseSystem()
    {
        if (enemyChaseSystem == null)
        {
            Fail("순찰 격리를 위해 EnemyChaseSystem을 찾지 못했습니다.");
            return;
        }

        chaseWasEnabled = enemyChaseSystem.enabled;
        enemyChaseSystem.StopAllChases();
        enemyChaseSystem.enabled = false;
        Pass("순찰 검증 동안 EnemyChaseSystem을 임시 비활성화했습니다.");
    }

    /// <summary>검증 시작 전 EnemyChaseSystem의 활성 상태를 복원한다.</summary>
    private void RestoreChaseSystem()
    {
        if (enemyChaseSystem != null)
        {
            enemyChaseSystem.enabled = chaseWasEnabled;
        }
    }

    /// <summary>체크포인트 EnemyState가 대상 Enemy와 Sector4 Route를 참조하는지 확인한다.</summary>
    private void VerifyCheckpointEnemyState(CheckpointDefinition checkpoint, string checkpointId)
    {
        if (checkpoint == null)
        {
            return;
        }

        Check(checkpoint.EnemyStates != null && checkpoint.EnemyStates.Length == 1,
            $"{checkpointId} EnemyState가 하나 연결됐습니다.",
            $"{checkpointId} EnemyState가 정확히 하나가 아닙니다.");
        if (checkpoint.EnemyStates == null || checkpoint.EnemyStates.Length != 1)
        {
            return;
        }

        CheckpointEnemyState state = checkpoint.EnemyStates[0];
        Check(state.Enemy == enemy, $"{checkpointId} EnemyState가 Sector4 Enemy를 참조합니다.",
            $"{checkpointId} EnemyState의 Enemy 참조가 다릅니다.");
        Check(state.Active, $"{checkpointId} EnemyState가 활성으로 저장됐습니다.",
            $"{checkpointId} EnemyState가 비활성으로 저장됐습니다.");
        Check(state.PatrolRoute == route, $"{checkpointId} EnemyState가 Sector4 Route를 참조합니다.",
            $"{checkpointId} EnemyState의 PatrolRoute 참조가 다릅니다.");
        Check(state.RouteStartIndex == 0, $"{checkpointId} Patrol 시작 인덱스가 0입니다.",
            $"{checkpointId} Patrol 시작 인덱스가 0이 아닙니다.");
    }

    /// <summary>CheckpointId로 체크포인트를 찾는다.</summary>
    private static CheckpointDefinition FindCheckpoint(string checkpointId)
    {
        return FindObjectsByType<CheckpointDefinition>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.CheckpointId == checkpointId);
    }

    /// <summary>조건을 기록하고 실패 항목을 누적한다.</summary>
    private void Check(bool condition, string success, string failure)
    {
        if (condition)
        {
            Pass(success);
        }
        else
        {
            Fail(failure);
        }
    }

    /// <summary>검증 성공 항목을 개발 로그로 남긴다.</summary>
    private void Pass(string message)
    {
        passCount++;
        GameLog.Log($"[Sector4 Patrol 검증 PASS] {message}", this);
    }

    /// <summary>검증 실패 항목을 오류 로그로 남긴다.</summary>
    private void Fail(string message)
    {
        failures.Add(message);
        GameLog.LogError($"[Sector4 Patrol 검증 FAIL] {message}", this);
    }

    /// <summary>전체 검증 결과를 요약 출력한다.</summary>
    private void ReportResult()
    {
        if (failures.Count == 0)
        {
            GameLog.Log($"[Sector4 Patrol 검증 완료] PASS {passCount}, FAIL 0", this);
            return;
        }

        GameLog.LogError(
            $"[Sector4 Patrol 검증 완료] PASS {passCount}, FAIL {failures.Count}\n- {string.Join("\n- ", failures)}",
            this);
    }
}
#endif
