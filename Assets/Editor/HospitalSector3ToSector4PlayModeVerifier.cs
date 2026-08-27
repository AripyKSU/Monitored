#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Core;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sector3 Save3 복원부터 BasementCorridor 최초 진입과 Save4 복원까지를 Play Mode에서 검증한다.
/// 검증 중 변경하는 진행 상태는 런타임 메모리에만 존재하며 Play Mode 종료와 함께 사라진다.
/// </summary>
[InitializeOnLoad]
public static class HospitalSector3ToSector4PlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalSector3ToSector4VerificationPending";

    /// <summary>스크립트 리로드 뒤에도 예약된 Play Mode 검증 콜백을 유지한다.</summary>
    static HospitalSector3ToSector4PlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox를 저장하지 않은 채 24번 경계 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector3 To Sector4 Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Sector3 → Sector4 검증은 다른 Play Mode 작업이 끝난 뒤 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning($"Sector3 → Sector4 검증은 '{ScenePath}' 씬에서 실행해야 합니다.");
            return;
        }

        // SessionState는 씬과 에셋에 기록되지 않아 다른 작업자의 변경을 덮어쓰지 않는다.
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
        new GameObject("HospitalSector3ToSector4VerificationRunner")
            .AddComponent<HospitalSector3ToSector4VerificationRunner>();
    }
}

/// <summary>Save3 복원과 BasementCorridor 진입에 따른 Save4 활성화를 검사하는 임시 Runner다.</summary>
public sealed class HospitalSector3ToSector4VerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 15f;
    private const float RestoreTimeout = 15f;

    private readonly List<string> failures = new();
    private int passCount;

    private GameSession gameSession;
    private CheckpointService checkpointService;
    private PlayerInventory inventory;
    private PlayerRoomTracker playerRoomTracker;
    private CheckpointDefinition save3;
    private CheckpointDefinition save4;
    private Doorway stairDoorway;
    private Room serviceRoom;
    private Room stairwellRoom;
    private Room basementRoom;
    private ItemDefinition basementKey;

    /// <summary>참조 수집, Save3 복원, 진입 사건, Save4 재복원을 순서대로 검사한다.</summary>
    private IEnumerator Start()
    {
        yield return WaitForGameplayReady();
        CollectReferences();
        VerifyStaticConfiguration();

        if (failures.Count == 0)
        {
            yield return VerifySave3Restore();
        }

        if (failures.Count == 0)
        {
            yield return VerifyBasementEntryAndSave4();
        }

        ReportResult();
        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>초기 체크포인트 복원이 끝나 GameSession이 Playing이 될 때까지 기다린다.</summary>
    private IEnumerator WaitForGameplayReady()
    {
        float deadline = Time.realtimeSinceStartup + StartupTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            if (gameSession != null && gameSession.Phase == GameSessionPhase.Playing)
            {
                Pass("초기 체크포인트 복원 후 GameSession이 Playing 상태입니다.");
                yield break;
            }

            yield return null;
        }

        Fail("GameSession이 제한 시간 안에 Playing 상태가 되지 않았습니다.");
    }

    /// <summary>검증에 필요한 공용 시스템, Room, Checkpoint, 계단 문을 수집한다.</summary>
    private void CollectReferences()
    {
        gameSession ??= FindFirstObjectByType<GameSession>();
        checkpointService = FindFirstObjectByType<CheckpointService>();
        inventory = FindFirstObjectByType<PlayerInventory>();
        playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
        save3 = FindCheckpoint("Save3");
        save4 = FindCheckpoint("Save4");
        serviceRoom = FindRoom("S3_HallwayB");
        stairwellRoom = FindRoom("S4_Stairwell");
        basementRoom = FindRoom("S4_BasementCorridor");
        basementKey = Resources.FindObjectsOfTypeAll<ItemDefinition>()
            .FirstOrDefault(item => item != null && item.ItemId == "basement_key");
        stairDoorway = FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(doorway => doorway != null && doorway.name == "SlideDoor_ServiceHallwayToStair" &&
                ((doorway.RoomA == serviceRoom && doorway.RoomB == stairwellRoom) ||
                 (doorway.RoomA == stairwellRoom && doorway.RoomB == serviceRoom)));
    }

    /// <summary>Editor Setup의 정적 구성을 다시 읽어 24번 필수 배선을 검사한다.</summary>
    private void VerifyStaticConfiguration()
    {
        Check(gameSession != null, "GameSession 참조가 존재합니다.", "GameSession이 없습니다.");
        Check(checkpointService != null, "CheckpointService 참조가 존재합니다.", "CheckpointService가 없습니다.");
        Check(inventory != null, "PlayerInventory 참조가 존재합니다.", "PlayerInventory가 없습니다.");
        Check(playerRoomTracker != null, "PlayerRoomTracker 참조가 존재합니다.", "PlayerRoomTracker가 없습니다.");
        Check(save3 != null, "Save3 CheckpointDefinition이 존재합니다.", "Save3가 없습니다.");
        Check(save4 != null, "Save4 CheckpointDefinition이 존재합니다.", "Save4가 없습니다.");
        Check(serviceRoom != null, "S3_HallwayB Room이 존재합니다.", "S3_HallwayB Room이 없습니다.");
        Check(stairwellRoom != null, "S4_Stairwell Room이 존재합니다.", "S4_Stairwell Room이 없습니다.");
        Check(basementRoom != null, "S4_BasementCorridor Room이 존재합니다.", "S4_BasementCorridor Room이 없습니다.");
        Check(stairDoorway != null, "S3_HallwayB ↔ S4_Stairwell 계단 문이 존재합니다.", "계단 Doorway가 없습니다.");
        Check(basementKey != null, "BasementKey 에셋이 존재합니다.", "BasementKey 에셋이 없습니다.");

        if (stairDoorway != null)
        {
            Check(stairDoorway.IsLocked, "검증 시작 시 계단 문이 잠겨 있습니다.", "검증 시작 시 계단 문이 열려 있습니다.");
        }

        bool setupValid = HospitalSector3ToSector4Setup.ValidateConfiguration(out string setupReport);
        Check(setupValid, setupReport, setupReport);
    }

    /// <summary>Save3를 복원해 Sector3, CenterHallway_B, 계단 잠금 기준을 검사한다.</summary>
    private IEnumerator VerifySave3Restore()
    {
        if (checkpointService == null || save3 == null)
        {
            yield break;
        }

        Check(checkpointService.ActivateCheckpoint(save3), "Save3 활성화 요청이 성공했습니다.", "Save3 활성화 요청이 실패했습니다.");
        Check(checkpointService.RestoreActiveCheckpoint(), "Save3 복원 요청이 시작되었습니다.", "Save3 복원 요청이 시작되지 않았습니다.");
        yield return WaitForGameplayReadyAfterRestore();

        Check(gameSession.CurrentStoryStage == StoryStage.Sector3,
            "Save3 복원 후 StoryStage가 Sector3입니다.", "Save3 복원 후 StoryStage가 Sector3가 아닙니다.");
        Check(playerRoomTracker.CurrentRoom?.RoomId == "CenterHallway_B",
            "Save3 복원 후 플레이어 논리 Room이 CenterHallway_B입니다.",
            $"Save3 복원 후 플레이어 논리 Room이 예상과 다릅니다: {playerRoomTracker.CurrentRoom?.RoomId ?? "null"}");
        Check(!gameSession.HasStoryFlag(StoryProgressFlags.BasementKeyAcquired),
            "Save3 복원 시 BasementKeyAcquired가 아직 없습니다.",
            "Save3 복원 시 BasementKeyAcquired가 이미 기록되어 있습니다.");
        Check(stairDoorway == null || stairDoorway.IsLocked,
            "Save3 복원 후 계단 문이 잠겨 있습니다.", "Save3 복원 후 계단 문이 잠금 해제됐습니다.");
    }

    /// <summary>열쇠 획득 상태에서 BasementCorridor 진입 사건이 Save4를 만들고 복원하는지 검사한다.</summary>
    private IEnumerator VerifyBasementEntryAndSave4()
    {
        if (gameSession == null || checkpointService == null || save4 == null || basementRoom == null)
        {
            yield break;
        }

        // 실제 냉장고 상호작용 대신 검증 대상 사건 직전의 정상 조건인 열쇠 보유만 구성한다.
        bool keyAvailable = inventory != null && basementKey != null &&
                            (inventory.Contains(basementKey) || inventory.TryAdd(basementKey));
        Check(keyAvailable,
            "검증용 BasementKey를 인벤토리에 추가했습니다.",
            "검증용 BasementKey를 인벤토리에 추가하지 못했습니다.");
        bool keyFlagSet = gameSession.HasStoryFlag(StoryProgressFlags.BasementKeyAcquired) ||
                          gameSession.SetStoryFlag(StoryProgressFlags.BasementKeyAcquired);
        Check(keyFlagSet,
            "검증용 BasementKeyAcquired flag를 추가했습니다.",
            "검증용 BasementKeyAcquired flag 추가에 실패했습니다.");
        // 실제 DoorTarget 이동 연출은 공유 플레이어 입력을 건드리지 않도록 생략하되,
        // PlayerRoomTracker의 인접 방 전환을 순서대로 호출해 동일한 RoomEntered 사건을 발생시킨다.
        Check(playerRoomTracker.EnterRoom(serviceRoom),
            "S3_HallwayB로 논리 방 전환이 가능합니다.", "S3_HallwayB로 논리 방 전환에 실패했습니다.");
        Check(playerRoomTracker.EnterRoom(stairwellRoom),
            "S4_Stairwell로 논리 방 전환이 가능합니다.", "S4_Stairwell로 논리 방 전환에 실패했습니다.");
        Check(playerRoomTracker.EnterRoom(basementRoom),
            "S4_BasementCorridor로 논리 방 전환이 가능합니다.",
            "S4_BasementCorridor로 논리 방 전환에 실패했습니다.");
        yield return null;

        Check(gameSession.HasStoryFlag(StoryProgressFlags.BasementEntered),
            "BasementCorridor 진입 사건으로 BasementEntered가 기록됐습니다.",
            "BasementCorridor 진입 사건 후 BasementEntered가 기록되지 않았습니다.");
        Check(checkpointService.ActiveCheckpoint == save4,
            "BasementCorridor 최초 진입으로 Save4가 활성화됐습니다.",
            $"BasementCorridor 최초 진입 후 활성 체크포인트가 Save4가 아닙니다: {checkpointService.ActiveCheckpoint?.CheckpointId ?? "null"}");
        Check(gameSession.CurrentStoryStage == StoryStage.Sector4,
            "Save4 활성화 후 StoryStage가 Sector4입니다.",
            "Save4 활성화 후 StoryStage가 Sector4가 아닙니다.");

        Check(checkpointService.RestoreActiveCheckpoint(), "Save4 복원 요청이 시작되었습니다.", "Save4 복원 요청이 시작되지 않았습니다.");
        yield return WaitForGameplayReadyAfterRestore();

        Check(playerRoomTracker.CurrentRoom == basementRoom,
            "Save4 복원 후 플레이어 논리 Room이 BasementCorridor입니다.",
            $"Save4 복원 후 플레이어 논리 Room이 예상과 다릅니다: {playerRoomTracker.CurrentRoom?.RoomId ?? "null"}");
        Check(gameSession.CurrentStoryStage == StoryStage.Sector4,
            "Save4 복원 후 StoryStage가 Sector4입니다.", "Save4 복원 후 StoryStage가 Sector4가 아닙니다.");
        Check(gameSession.HasStoryFlag(StoryProgressFlags.BasementEntered),
            "Save4 복원 후 BasementEntered가 유지됩니다.", "Save4 복원 후 BasementEntered가 사라졌습니다.");
        Check(inventory != null && basementKey != null && inventory.Contains(basementKey),
            "Save4 복원 후 BasementKey가 인벤토리에 유지됩니다.",
            "Save4 복원 후 BasementKey가 인벤토리에 없습니다.");
        Check(stairDoorway == null || !stairDoorway.IsLocked,
            "Save4 복원 후 계단 문이 열림 상태입니다.", "Save4 복원 후 계단 문이 잠겨 있습니다.");
    }

    /// <summary>Restart에 해당하는 복원이 끝날 때까지 기다린다.</summary>
    private IEnumerator WaitForGameplayReadyAfterRestore()
    {
        float deadline = Time.realtimeSinceStartup + RestoreTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (gameSession != null && gameSession.Phase == GameSessionPhase.Playing)
            {
                yield break;
            }

            yield return null;
        }

        Fail("체크포인트 복원이 제한 시간 안에 끝나지 않았습니다.");
    }

    /// <summary>검증 결과를 GameLog로 남기고 Play Mode를 종료한다.</summary>
    private void ReportResult()
    {
        if (failures.Count == 0)
        {
            GameLog.Log($"Sector3 → Sector4 Play Mode 검증 통과: {passCount}개 항목.", this);
            return;
        }

        GameLog.LogError(
            $"Sector3 → Sector4 Play Mode 검증 실패 ({failures.Count}개):\n- {string.Join("\n- ", failures)}",
            this);
    }

    /// <summary>검증 통과 항목을 기록한다.</summary>
    private void Pass(string message)
    {
        passCount++;
        GameLog.Log($"[Sector3 → Sector4 검증 통과] {message}", this);
    }

    /// <summary>검증 실패 항목을 기록한다.</summary>
    private void Fail(string message)
    {
        failures.Add(message);
        GameLog.LogError($"[Sector3 → Sector4 검증 실패] {message}", this);
    }

    /// <summary>조건에 따라 통과 또는 실패를 기록한다.</summary>
    private void Check(bool condition, string passMessage, string failMessage)
    {
        if (condition)
        {
            Pass(passMessage);
        }
        else
        {
            Fail(failMessage);
        }
    }

    /// <summary>방 ID로 현재 씬의 Room을 찾는다.</summary>
    private static Room FindRoom(string roomId)
    {
        return FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && room.RoomId == roomId);
    }

    /// <summary>체크포인트 ID로 현재 씬의 CheckpointDefinition을 찾는다.</summary>
    private static CheckpointDefinition FindCheckpoint(string checkpointId)
    {
        return FindObjectsByType<CheckpointDefinition>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(checkpoint => checkpoint != null && checkpoint.CheckpointId == checkpointId);
    }
}
#endif
