#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Core;
using HauntedHouse.Enemies;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 39번 기능 테스트가 공통으로 사용하는 실행·결과 기록 도구다.
/// 각 Sector 테스트는 이 컨텍스트를 통해서만 결과를 기록해 테스트 간 출력 형식을 통일한다.
/// </summary>
public sealed class HospitalSectorTestContext
{
    private readonly List<string> failures = new();
    private readonly List<string> manualChecks = new();

    /// <summary>실행 중인 세션을 반환한다.</summary>
    public GameSession Session { get; }

    /// <summary>현재 활성 체크포인트 서비스를 반환한다.</summary>
    public CheckpointService Checkpoints { get; }

    /// <summary>현재 플레이어 방 추적기를 반환한다.</summary>
    public PlayerRoomTracker RoomTracker { get; }

    /// <summary>현재 씬의 플레이어 인벤토리를 반환한다.</summary>
    public PlayerInventory Inventory { get; }

    /// <summary>누적된 통과 항목 수를 반환한다.</summary>
    public int PassCount { get; private set; }

    /// <summary>누적된 수동 확인 항목 수를 반환한다.</summary>
    public int ManualCount => manualChecks.Count;

    /// <summary>게임 런타임 참조를 수집해 새 테스트 컨텍스트를 만든다.</summary>
    public HospitalSectorTestContext()
    {
        Session = UnityEngine.Object.FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);
        Checkpoints = UnityEngine.Object.FindFirstObjectByType<CheckpointService>(FindObjectsInactive.Include);
        RoomTracker = UnityEngine.Object.FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        Inventory = UnityEngine.Object.FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);
    }

    /// <summary>GameSession이 플레이 입력을 받을 수 있을 때까지 기다린다.</summary>
    public IEnumerator WaitForGameplayReady(float timeoutSeconds = 20f)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (Session != null && Session.Phase == GameSessionPhase.Playing)
            {
                Pass("GameSession이 Playing 상태로 초기화되었습니다.");
                yield break;
            }

            yield return null;
        }

        Fail("GameSession이 제한 시간 안에 Playing 상태가 되지 않았습니다.");
    }

    /// <summary>조건을 검사하고 성공 또는 실패 결과를 기록한다.</summary>
    public void Check(bool condition, string passMessage, string failureMessage)
    {
        if (condition)
        {
            Pass(passMessage);
            return;
        }

        Fail(failureMessage);
    }

    /// <summary>자동 판정이 어려운 시각·조작 항목을 수동 확인 대상으로 기록한다.</summary>
    public void Manual(int number, string description)
    {
        string message = $"{number}. {description}";
        manualChecks.Add(message);
        GameLog.Log($"[39 기능 테스트][MANUAL] {message}");
    }

    /// <summary>RoomId로 비활성 Room까지 포함해 Room을 찾는다.</summary>
    public Room FindRoom(string roomId)
    {
        return UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && string.Equals(room.RoomId, roomId, StringComparison.Ordinal));
    }

    /// <summary>씬 전체에서 정확한 이름의 Target을 찾는다.</summary>
    public Target FindTarget(string targetName)
    {
        return UnityEngine.Object.FindObjectsByType<Target>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(target => target != null && string.Equals(target.name, targetName, StringComparison.Ordinal));
    }

    /// <summary>씬 전체에서 정확한 CheckpointId의 체크포인트를 찾는다.</summary>
    public CheckpointDefinition FindCheckpoint(string checkpointId)
    {
        return UnityEngine.Object.FindObjectsByType<CheckpointDefinition>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(checkpoint => checkpoint != null &&
                string.Equals(checkpoint.CheckpointId, checkpointId, StringComparison.Ordinal));
    }

    /// <summary>Room의 자식 계층에서 이름이 같은 Target을 찾는다.</summary>
    public Target FindTarget(Room room, string targetName)
    {
        return room == null
            ? null
            : room.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => target != null &&
                    string.Equals(target.name, targetName, StringComparison.Ordinal));
    }

    /// <summary>이름이 같은 컴포넌트를 비활성 오브젝트까지 포함해 찾는다.</summary>
    public T FindNamed<T>(string objectName) where T : Component
    {
        return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(component => component != null &&
                string.Equals(component.name, objectName, StringComparison.Ordinal));
    }

    /// <summary>참조가 가리키는 Room의 ID가 기대값과 같은지 검사한다.</summary>
    public void CheckRoom(string label, Room room, string expectedRoomId, int number)
    {
        Check(room != null && string.Equals(room.RoomId, expectedRoomId, StringComparison.Ordinal),
            $"{number}. {label}이 {expectedRoomId}입니다.",
            $"{number}. {label}이 예상과 다릅니다: {room?.RoomId ?? "null"}");
    }

    /// <summary>체크포인트 정의의 직렬화 참조와 StoryStage를 검사한다.</summary>
    public void CheckCheckpoint(string checkpointId, StoryStage expectedStage, string expectedRoomId, int number)
    {
        CheckpointDefinition checkpoint = FindCheckpoint(checkpointId);
        Check(checkpoint != null,
            $"{number}. {checkpointId} CheckpointDefinition이 존재합니다.",
            $"{number}. {checkpointId} CheckpointDefinition을 찾지 못했습니다.");
        if (checkpoint == null)
        {
            return;
        }

        bool isValid = checkpoint.TryValidateConfiguration(out string error);
        Check(isValid,
            $"{number}. {checkpointId} 구성이 유효합니다.",
            $"{number}. {checkpointId} 구성이 유효하지 않습니다: {error}");
        Check(checkpoint.StoryStage == expectedStage,
            $"{number}. {checkpointId} StoryStage가 {expectedStage}입니다.",
            $"{number}. {checkpointId} StoryStage가 {checkpoint.StoryStage}입니다.");
        CheckRoom($"{checkpointId} RespawnRoom", checkpoint.RespawnRoom, expectedRoomId, number);
    }

    /// <summary>테스트 실패를 누적하고 오류 로그를 남긴다.</summary>
    public void Fail(string message)
    {
        failures.Add(message);
        GameLog.LogError($"[39 기능 테스트][FAIL] {message}");
    }

    /// <summary>테스트 통과를 누적하고 개발 로그를 남긴다.</summary>
    public void Pass(string message)
    {
        PassCount++;
        GameLog.Log($"[39 기능 테스트][PASS] {message}");
    }

    /// <summary>실행한 Sector의 결과를 요약하고 실패 시 오류로 남긴다.</summary>
    public void Report(HospitalSectorId sector)
    {
        string prefix = $"Sector{(int)sector}";
        if (failures.Count == 0)
        {
            GameLog.Log(
                $"[39 기능 테스트][{prefix}] 완료: PASS {PassCount}, MANUAL {ManualCount}, FAIL 0");
            return;
        }

        GameLog.LogError(
            $"[39 기능 테스트][{prefix}] 완료: PASS {PassCount}, MANUAL {ManualCount}, FAIL {failures.Count}\n- " +
            string.Join("\n- ", failures));
    }
}

/// <summary>39번 기능 테스트에서 실행할 Sector 식별자다.</summary>
public enum HospitalSectorId
{
    Sector1 = 1,
    Sector2 = 2,
    Sector3 = 3,
    Sector4 = 4,
    Sector5 = 5
}

/// <summary>
/// Sector별 검증기를 Edit Mode에서 예약하고 Play Mode 진입 후 한 번 실행한다.
/// SessionState만 사용하므로 씬과 다른 에이전트의 편집 상태를 저장하거나 덮어쓰지 않는다.
/// </summary>
[InitializeOnLoad]
public static class HospitalSectorTestHarness
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalSector39FunctionalTestPending";
    private const string SectorKey = "HauntedHouse.HospitalSector39FunctionalTestSector";

    /// <summary>스크립트 리로드 뒤에도 Play Mode 진입 콜백을 유지한다.</summary>
    static HospitalSectorTestHarness()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>지정한 Sector 기능 테스트를 예약한다.</summary>
    public static void Schedule(HospitalSectorId sector)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("39번 Sector 기능 테스트는 다른 Play Mode 작업이 끝난 뒤 실행해야 합니다.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.path, ScenePath, StringComparison.Ordinal))
        {
            GameLog.LogWarning($"39번 Sector 기능 테스트는 '{ScenePath}' 씬에서 실행해야 합니다. 현재 씬: {activeScene.path}");
            return;
        }

        SessionState.SetInt(SectorKey, (int)sector);
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Play Mode 진입 직후 해당 Sector의 임시 Runner를 생성한다.</summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        HospitalSectorId sector = (HospitalSectorId)SessionState.GetInt(SectorKey, 0);
        if (sector < HospitalSectorId.Sector1 || sector > HospitalSectorId.Sector5)
        {
            GameLog.LogError("39번 기능 테스트 Sector 값이 올바르지 않습니다.");
            EditorApplication.ExitPlaymode();
            return;
        }

        new GameObject($"HospitalSector{(int)sector}FunctionalTestRunner")
            .AddComponent<HospitalSectorFunctionalTestRunner>()
            .Initialize(sector);
    }
}

/// <summary>Sector 기능 테스트를 실제 Play Mode에서 순차 실행하는 임시 Runner다.</summary>
public sealed class HospitalSectorFunctionalTestRunner : MonoBehaviour
{
    private HospitalSectorId sector;

    /// <summary>실행할 Sector를 지정한다.</summary>
    public void Initialize(HospitalSectorId value)
    {
        sector = value;
    }

    /// <summary>게임 초기화가 끝난 뒤 Sector 테스트를 실행하고 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        HospitalSectorTestContext context = new();
        yield return context.WaitForGameplayReady();

        if (context.Session == null || context.Session.Phase != GameSessionPhase.Playing)
        {
            context.Report(sector);
            yield return null;
            EditorApplication.ExitPlaymode();
            Destroy(gameObject);
            yield break;
        }

        IEnumerator sectorRoutine = null;
        try
        {
            switch (sector)
            {
                case HospitalSectorId.Sector1:
                    sectorRoutine = HospitalSector1FunctionalTest.Run(context);
                    break;
                case HospitalSectorId.Sector2:
                    sectorRoutine = HospitalSector2FunctionalTest.Run(context);
                    break;
                case HospitalSectorId.Sector3:
                    sectorRoutine = HospitalSector3FunctionalTest.Run(context);
                    break;
                case HospitalSectorId.Sector4:
                    sectorRoutine = HospitalSector4FunctionalTest.Run(context);
                    break;
                case HospitalSectorId.Sector5:
                    sectorRoutine = HospitalSector5FunctionalTest.Run(context);
                    break;
            }
        }
        catch (Exception exception)
        {
            // 씬 구성 누락이나 예기치 않은 API 예외가 발생해도 Play Mode가 고아 상태로 남지 않도록
            // 현재 Sector 결과에 실패를 기록한 뒤 공통 종료 흐름으로 이동한다.
            context.Fail($"Sector{(int)sector} 테스트 실행 중 예외가 발생했습니다: {exception.Message}");
        }

        // Iterator 내부의 예외도 공통 종료를 보장하도록 MoveNext 단위에서 별도로 감싼다.
        while (sectorRoutine != null)
        {
            object current;
            bool hasNext;
            try
            {
                hasNext = sectorRoutine.MoveNext();
                current = sectorRoutine.Current;
            }
            catch (Exception exception)
            {
                context.Fail($"Sector{(int)sector} 테스트 단계 실행 중 예외가 발생했습니다: {exception.Message}");
                break;
            }

            if (!hasNext)
            {
                break;
            }

            yield return current;
        }

        context.Report(sector);
        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }
}
#endif
