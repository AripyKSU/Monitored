#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.Interaction;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 계획서 37·38번에 정의된 Save1~Save5 체크포인트 복원 결과를 Play Mode에서 검증한다.
/// 검증 중에는 씬이나 에셋을 저장하지 않고 런타임 상태만 오염·복원하여 다른 작업자의 변경을 보존한다.
/// </summary>
[InitializeOnLoad]
public static class HospitalCheckpointRestorePlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalCheckpointRestoreVerificationPending";

    /// <summary>스크립트 리로드 뒤에도 Play Mode 진입 예약 콜백을 유지한다.</summary>
    static HospitalCheckpointRestorePlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox 씬에서 체크포인트 복원 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Checkpoint Restore Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("체크포인트 복원 검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning(
                $"체크포인트 복원 검증은 '{ScenePath}' 씬에서 실행해야 합니다. " +
                $"현재 씬: {SceneManager.GetActiveScene().path}");
            return;
        }

        // SessionState만 사용하므로 Play Mode가 끝나면 검증용 예약 값도 사라진다.
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Play Mode 진입 직후 임시 런타임 Runner를 한 번 생성한다.</summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        new GameObject("HospitalCheckpointRestoreVerificationRunner")
            .AddComponent<HospitalCheckpointRestoreVerificationRunner>();
    }
}

/// <summary>
/// Save1~Save5를 일부러 잘못된 상태에서 복원하고 계획서의 복원 기준과 실제 오브젝트 상태를 비교하는 Runner다.
/// </summary>
public sealed class HospitalCheckpointRestoreVerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 20f;
    private const float RestoreTimeout = 20f;
    private const float PositionTolerance = 0.35f;
    private const float RotationTolerance = 5f;

    private static readonly string[] SaveIds = { "Save1", "Save2", "Save3", "Save4", "Save5" };

    private static readonly string[] AllStoryFlags =
    {
        StoryProgressFlags.IntroDialogueCompleted,
        StoryProgressFlags.StartHallDoorScareSeen,
        StoryProgressFlags.FlashlightAcquired,
        StoryProgressFlags.NursingRoomFlashlightActivated,
        StoryProgressFlags.WardHallKeyAcquired,
        StoryProgressFlags.CenterHallFirstEntered,
        StoryProgressFlags.FuseInstalled,
        StoryProgressFlags.GeneratorPowered,
        StoryProgressFlags.GateActivated,
        StoryProgressFlags.Sector2HideEncounterCompleted,
        StoryProgressFlags.CenterHallSecondEntered,
        StoryProgressFlags.ExitDoorHintShown,
        StoryProgressFlags.FreezerHintRead,
        StoryProgressFlags.BasementKeyAcquired,
        StoryProgressFlags.BasementEntered,
        StoryProgressFlags.KillerDiary3Read,
        StoryProgressFlags.MasterKeyAcquired,
        StoryProgressFlags.FinalChaseStarted,
        StoryProgressFlags.EntranceReached,
        StoryProgressFlags.EndingStarted,
        GateController.GateDiscoveryDialogueShownFlag
    };

    private readonly List<string> failures = new();
    private int passCount;

    private GameSession gameSession;
    private CheckpointService checkpointService;
    private Player player;
    private PlayerRoomTracker playerRoomTracker;
    private PlayerInventory playerInventory;
    private GateController gateController;
    private GeneratorPowerController generator;
    private MachineRoomTarget machineRoomTarget;
    private Sector2HideEncounterController hideEncounter;
    private Sector5FinalChaseController finalChaseController;
    private EnemyChaseSystem enemyChaseSystem;
    private bool chaseSystemWasEnabled;
    private EnemyController[] enemies = Array.Empty<EnemyController>();
    private CheckpointDefinition[] checkpoints = Array.Empty<CheckpointDefinition>();
    private readonly Dictionary<string, CheckpointDefinition> checkpointsById = new();

    /// <summary>초기 복원 완료 후 정적 구성과 Save1~Save5 복원을 검사하고 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        yield return WaitForGameplayReady();
        CollectReferences();
        VerifyCommonConfiguration();

        if (failures.Count == 0)
        {
            DisableChaseSystemForRestoreChecks();
            foreach (string saveId in SaveIds)
            {
                yield return VerifyCheckpoint(saveId);
                if (failures.Count > 0)
                {
                    // 한 Save의 실패가 다음 Save의 복원 자체를 막지는 않도록 계속 검사한다.
                    GameLog.LogWarning($"{saveId} 검사 중 실패가 있어 다음 체크포인트 검사를 계속합니다.", this);
                }
            }

            RestoreChaseSystem();
        }

        ReportResult();
        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>초기 Save1 복원이 끝나 GameSession이 Playing이 될 때까지 기다린다.</summary>
    private IEnumerator WaitForGameplayReady()
    {
        float deadline = Time.realtimeSinceStartup + StartupTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            if (gameSession != null && gameSession.Phase == GameSessionPhase.Playing)
            {
                Pass("초기 체크포인트 복원 후 GameSession이 Playing 상태가 됐습니다.");
                yield break;
            }

            yield return null;
        }

        Fail("초기 체크포인트 복원이 제한 시간 안에 Playing 상태로 끝나지 않았습니다.");
    }

    /// <summary>공용 시스템, 체크포인트, 38번 상태 재계산 대상 참조를 수집한다.</summary>
    private void CollectReferences()
    {
        gameSession ??= FindFirstObjectByType<GameSession>();
        checkpointService = FindFirstObjectByType<CheckpointService>();
        player = FindFirstObjectByType<Player>();
        playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
        playerInventory = FindFirstObjectByType<PlayerInventory>();
        gateController = FindFirstObjectByType<GateController>(FindObjectsInactive.Include);
        generator = FindFirstObjectByType<GeneratorPowerController>(FindObjectsInactive.Include);
        machineRoomTarget = FindFirstObjectByType<MachineRoomTarget>(FindObjectsInactive.Include);
        hideEncounter = FindFirstObjectByType<Sector2HideEncounterController>(FindObjectsInactive.Include);
        finalChaseController = FindFirstObjectByType<Sector5FinalChaseController>(FindObjectsInactive.Include);
        enemyChaseSystem = FindFirstObjectByType<EnemyChaseSystem>(FindObjectsInactive.Include);
        enemies = FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        checkpoints = FindObjectsByType<CheckpointDefinition>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        checkpointsById.Clear();
        foreach (CheckpointDefinition checkpoint in checkpoints)
        {
            if (checkpoint != null && !string.IsNullOrWhiteSpace(checkpoint.CheckpointId))
            {
                checkpointsById[checkpoint.CheckpointId] = checkpoint;
            }
        }
    }

    /// <summary>공용 참조와 모든 CheckpointDefinition의 직렬화 구성을 사전에 검사한다.</summary>
    private void VerifyCommonConfiguration()
    {
        Check(gameSession != null, "GameSession 참조가 존재합니다.", "GameSession이 없습니다.");
        Check(checkpointService != null, "CheckpointService 참조가 존재합니다.", "CheckpointService가 없습니다.");
        Check(player != null, "Player 참조가 존재합니다.", "Player가 없습니다.");
        Check(playerRoomTracker != null, "PlayerRoomTracker 참조가 존재합니다.", "PlayerRoomTracker가 없습니다.");
        Check(playerInventory != null, "PlayerInventory 참조가 존재합니다.", "PlayerInventory가 없습니다.");

        foreach (string saveId in SaveIds)
        {
            Check(checkpointsById.TryGetValue(saveId, out CheckpointDefinition checkpoint),
                $"{saveId} CheckpointDefinition이 존재합니다.",
                $"{saveId} CheckpointDefinition이 없습니다.");
            if (checkpoint == null)
            {
                continue;
            }

            Check(checkpoint.TryValidateConfiguration(out string error),
                $"{saveId} CheckpointDefinition 구성이 유효합니다.",
                $"{saveId} CheckpointDefinition 구성 오류: {error}");

            VerifyCompletedStateDefinitions(checkpoint, saveId);
        }

        Check(gateController != null, "GateController 참조가 존재합니다.", "GateController가 없습니다.");
        Check(generator != null, "GeneratorPowerController 참조가 존재합니다.", "GeneratorPowerController가 없습니다.");
        Check(machineRoomTarget != null, "MachineRoomTarget 참조가 존재합니다.", "MachineRoomTarget이 없습니다.");
        Check(hideEncounter != null, "Sector2HideEncounterController 참조가 존재합니다.",
            "Sector2HideEncounterController가 없습니다.");

        if (finalChaseController != null)
        {
            Check(finalChaseController.TryValidateConfiguration(out string error),
                "Save5 최종 추격 Controller 구성이 유효합니다.",
                $"Save5 최종 추격 Controller 구성 오류: {error}");
        }
        else
        {
            Fail("Save5 최종 추격 Controller가 없어 계단 진입 조건을 검사할 수 없습니다.");
        }
    }

    /// <summary>체크포인트의 완료 상태 참가자 참조와 중복 ID를 검사한다.</summary>
    private void VerifyCompletedStateDefinitions(CheckpointDefinition checkpoint, string saveId)
    {
        HashSet<string> completionIds = new();
        if (checkpoint.CompletedStates == null)
        {
            Fail($"{saveId}의 completedStates 배열이 null입니다.");
            return;
        }

        foreach (CheckpointCompletedState state in checkpoint.CompletedStates)
        {
            Check(state != null && state.Participant != null,
                $"{saveId} completedState 참가자 참조가 유효합니다.",
                $"{saveId} completedState에 ICompletedState 참가자가 없습니다.");
            if (state?.Participant == null)
            {
                continue;
            }

            Check(completionIds.Add(state.CompletionId),
                $"{saveId} 완료 ID '{state.CompletionId}'가 유일합니다.",
                $"{saveId} 완료 ID '{state.CompletionId}'가 중복됩니다.");
        }
    }

    /// <summary>하나의 Save를 오염시킨 뒤 복원하고 37·38번 결과를 검사한다.</summary>
    private IEnumerator VerifyCheckpoint(string saveId)
    {
        if (!checkpointsById.TryGetValue(saveId, out CheckpointDefinition checkpoint) || checkpoint == null)
        {
            yield break;
        }

        ContaminateRuntimeState();
        Check(checkpointService.ActivateCheckpoint(checkpoint),
            $"{saveId}를 활성 체크포인트로 지정했습니다.",
            $"{saveId} 활성화에 실패했습니다.");
        Check(checkpointService.RestoreActiveCheckpoint(),
            $"{saveId} Restart 복원을 시작했습니다.",
            $"{saveId} Restart 복원을 시작하지 못했습니다.");
        yield return WaitForRestore(saveId);
        yield return null;

        VerifyCheckpointResult(checkpoint, saveId);
        VerifyStoryDrivenObjectStates(saveId);
        if (saveId == "Save5")
        {
            VerifySave5FinalChasePrecondition();
        }
    }

    /// <summary>복원 로직이 실제로 상태를 교체하는지 확인할 수 있도록 의도적으로 반대 상태를 만든다.</summary>
    private void ContaminateRuntimeState()
    {
        if (gameSession != null)
        {
            foreach (string flag in AllStoryFlags)
            {
                gameSession.SetStoryFlag(flag);
            }

            gameSession.SetStoryStage(StoryStage.Ending);
        }

        if (playerInventory != null)
        {
            // 모든 Save에 등장하는 아이템을 섞어 넣어 Save별 목록 교체를 확인한다.
            ItemDefinition[] contaminatedItems = checkpoints
                .Where(checkpoint => checkpoint != null && checkpoint.InventoryItems != null)
                .SelectMany(checkpoint => checkpoint.InventoryItems)
                .Where(item => item != null)
                .GroupBy(item => item.ItemId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            playerInventory.RestoreItems(contaminatedItems);
        }

        foreach (Doorway doorway in FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            doorway?.SetLocked(!doorway.IsLocked);
            doorway?.ForceClosed();
        }

        foreach (MonoBehaviour component in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (component is ICompletedState participant)
            {
                participant.RestoreCompletedState(!participant.IsCompleted);
            }
        }

        foreach (EnemyController enemy in enemies)
        {
            enemy?.SetActive(false);
        }

        if (playerRoomTracker != null)
        {
            Room otherRoom = FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(room => room != null && room != playerRoomTracker.CurrentRoom);
            if (otherRoom != null)
            {
                playerRoomTracker.RestoreRoom(otherRoom);
            }
        }
    }

    /// <summary>복원 코루틴이 Restoring을 거쳐 Playing으로 끝날 때까지 기다린다.</summary>
    private IEnumerator WaitForRestore(string saveId)
    {
        if (gameSession == null)
        {
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + RestoreTimeout;
        bool restoringObserved = false;
        while (Time.realtimeSinceStartup < deadline)
        {
            restoringObserved |= gameSession.Phase == GameSessionPhase.Restoring;
            if (restoringObserved && gameSession.Phase == GameSessionPhase.Playing)
            {
                Pass($"{saveId} 복원이 제한 시간 안에 완료됐습니다.");
                yield break;
            }

            yield return null;
        }

        Fail($"{saveId} 복원이 제한 시간 안에 완료되지 않았습니다.");
    }

    /// <summary>37번 표의 Save별 Stage·방·위치·flag·아이템·문·적 상태를 검사한다.</summary>
    private void VerifyCheckpointResult(CheckpointDefinition checkpoint, string saveId)
    {
        CheckpointExpectation expectation = CheckpointExpectation.For(saveId);
        Check(checkpointService.ActiveCheckpoint == checkpoint,
            $"{saveId}가 활성 체크포인트로 유지됩니다.",
            $"복원 후 활성 체크포인트가 {saveId}가 아닙니다.");

        Check(gameSession.CurrentStoryStage == expectation.Stage,
            $"{saveId} 복원 후 StoryStage가 {expectation.Stage}입니다.",
            $"{saveId} 복원 후 StoryStage가 {gameSession.CurrentStoryStage}입니다.");
        Check(playerRoomTracker.CurrentRoom == checkpoint.RespawnRoom &&
              checkpoint.RespawnRoom != null && checkpoint.RespawnRoom.RoomId == expectation.RoomId,
            $"{saveId} 복원 후 논리 방이 {expectation.RoomId}입니다.",
            $"{saveId} 복원 후 논리 방이 예상과 다릅니다: {playerRoomTracker.CurrentRoom?.RoomId ?? "null"}");

        if (player != null && checkpoint.RespawnPoint != null)
        {
            Check(Vector3.Distance(player.transform.position, checkpoint.RespawnPoint.position) <= PositionTolerance,
                $"{saveId} 플레이어 위치가 RespawnPoint와 일치합니다.",
                $"{saveId} 플레이어 위치가 RespawnPoint에서 벗어났습니다.");
            Check(Quaternion.Angle(player.transform.rotation, checkpoint.RespawnPoint.rotation) <= RotationTolerance,
                $"{saveId} 플레이어 회전이 RespawnPoint와 일치합니다.",
                $"{saveId} 플레이어 회전이 RespawnPoint와 다릅니다.");
        }

        foreach (string flag in AllStoryFlags)
        {
            bool expected = checkpoint.ProgressFlags != null && checkpoint.ProgressFlags.Contains(flag);
            Check(gameSession.HasStoryFlag(flag) == expected,
                $"{saveId} flag {flag} 복원 상태가 올바릅니다.",
                $"{saveId} flag {flag}가 예상과 다릅니다.");
        }

        VerifyInventory(checkpoint, saveId);
        VerifyDoorwayStates(checkpoint, saveId);
        VerifyEnemyStates(checkpoint, saveId, expectation);
    }

    /// <summary>체크포인트가 지정한 ItemId 집합과 현재 인벤토리를 비교한다.</summary>
    private void VerifyInventory(CheckpointDefinition checkpoint, string saveId)
    {
        HashSet<string> expected = new(
            (checkpoint.InventoryItems ?? Array.Empty<ItemDefinition>())
                .Where(item => item != null)
                .Select(item => item.ItemId),
            StringComparer.Ordinal);
        HashSet<string> actual = new(
            (playerInventory?.Items ?? Array.Empty<ItemDefinition>())
                .Where(item => item != null)
                .Select(item => item.ItemId),
            StringComparer.Ordinal);

        Check(expected.SetEquals(actual),
            $"{saveId} 인벤토리가 CheckpointDefinition과 일치합니다.",
            $"{saveId} 인벤토리가 예상과 다릅니다. 예상=[{string.Join(", ", expected)}], 실제=[{string.Join(", ", actual)}]");
    }

    /// <summary>체크포인트에 기록된 모든 Doorway 잠금 상태가 실제 상태에 적용됐는지 검사한다.</summary>
    private void VerifyDoorwayStates(CheckpointDefinition checkpoint, string saveId)
    {
        if (checkpoint.DoorwayStates == null)
        {
            return;
        }

        foreach (CheckpointDoorwayState state in checkpoint.DoorwayStates)
        {
            if (state?.Doorway == null)
            {
                continue;
            }

            Check(state.Doorway.IsLocked == state.Locked && !state.Doorway.IsOpen,
                $"{saveId} Doorway '{state.Doorway.name}' 잠금·닫힘 상태가 복원됐습니다.",
                $"{saveId} Doorway '{state.Doorway.name}' 상태가 잠금={state.Locked}, 열림=false와 다릅니다.");
        }
    }

    /// <summary>Save별 Enemy 비활성·순찰 경로·첫 방 복원 결과를 검사한다.</summary>
    private void VerifyEnemyStates(CheckpointDefinition checkpoint, string saveId, CheckpointExpectation expectation)
    {
        bool shouldHaveActiveEnemy = expectation.ActiveEnemy;
        if (!shouldHaveActiveEnemy)
        {
            Check(enemies.All(enemy => enemy == null || enemy.Mode == EnemyMode.Inactive),
                $"{saveId}에서 Enemy가 모두 비활성입니다.",
                $"{saveId} 복원 후 비활성이어야 할 Enemy가 활성입니다.");
            return;
        }

        CheckpointEnemyState state = checkpoint.EnemyStates?.FirstOrDefault(item => item != null && item.Active);
        Check(state != null && state.Enemy != null,
            $"{saveId} 활성 EnemyState가 존재합니다.",
            $"{saveId} 활성 EnemyState가 없습니다.");
        if (state == null || state.Enemy == null)
        {
            return;
        }

        Check(state.Enemy.Mode == EnemyMode.Patrol,
            $"{saveId} Enemy가 Patrol 상태로 복원됐습니다.",
            $"{saveId} Enemy Mode가 Patrol이 아닙니다: {state.Enemy.Mode}");
        Check(state.Enemy.CurrentRoute == state.PatrolRoute,
            $"{saveId} Enemy PatrolRoute가 CheckpointDefinition과 일치합니다.",
            $"{saveId} Enemy PatrolRoute가 CheckpointDefinition과 다릅니다.");

        PatrolPoint firstPoint = state.PatrolRoute != null && state.PatrolRoute.Count > 0
            ? state.PatrolRoute.GetPoint(state.RouteStartIndex)
            : null;
        Check(firstPoint != null && state.Enemy.CurrentRoom == firstPoint.Room &&
              firstPoint.Room.RoomId == expectation.EnemyStartRoomId,
            $"{saveId} Enemy가 {expectation.EnemyStartRoomId}에서 순찰을 시작합니다.",
            $"{saveId} Enemy 시작 방이 예상과 다릅니다: {state.Enemy.CurrentRoom?.RoomId ?? "null"}");
    }

    /// <summary>38번의 flag 기반 Controller·Target·ICompletedState 재계산 결과를 검사한다.</summary>
    private void VerifyStoryDrivenObjectStates(string saveId)
    {
        bool gateExpected = saveId is "Save3" or "Save4" or "Save5";
        bool diaryExpected = saveId == "Save5";
        bool masterKeyExpected = saveId == "Save5";
        bool freezerExpected = saveId is "Save4" or "Save5";

        if (gateController != null)
        {
            Check(gateController.IsActivated == gateExpected,
                $"{saveId} GateController가 GateActivated flag에 맞게 계산됐습니다.",
                $"{saveId} GateController 상태가 예상과 다릅니다.");
        }

        if (generator != null)
        {
            Check(generator.IsPowered == gateExpected,
                $"{saveId} GeneratorPowerController가 GeneratorPowered flag에 맞게 계산됐습니다.",
                $"{saveId} GeneratorPowerController 상태가 예상과 다릅니다.");
            Check(generator.GetComponent<Target>() == null || generator.GetComponent<Target>().IsSelectable == !gateExpected,
                $"{saveId} 발전기 Target 선택 상태가 완료 여부에 맞습니다.",
                $"{saveId} 발전기 Target 선택 상태가 완료 여부와 다릅니다.");
        }

        if (machineRoomTarget != null)
        {
            Check(machineRoomTarget.IsCompleted == gateExpected,
                $"{saveId} MachineRoom 완료 상태가 복원됐습니다.",
                $"{saveId} MachineRoom 완료 상태가 예상과 다릅니다.");
        }

        if (hideEncounter != null)
        {
            // Gate가 아직 닫힌 Save1·2에서는 전원 조건이 충족되지 않으므로
            // ShouldStartEncounter가 false여야 하며, Save3 이후에는 Completed 상태여야 한다.
            Check(!hideEncounter.ShouldStartEncounter,
                $"{saveId} Sector2 숨기 이벤트 재실행 조건이 올바릅니다.",
                $"{saveId} Sector2 숨기 이벤트 재실행 조건이 예상과 다릅니다.");
            if (gateExpected)
            {
                Check(hideEncounter.StateName == "Completed",
                    $"{saveId} Sector2 숨기 이벤트 상태가 Completed입니다.",
                    $"{saveId} Sector2 숨기 이벤트 상태가 Completed가 아닙니다: {hideEncounter.StateName}");
            }
        }

        VerifyNamedCompletedTarget("Story_FreezerHintBoard", freezerExpected, saveId, "FreezerHintRead");
        VerifyNamedCompletedTarget("Workshop_RestraintBed", diaryExpected, saveId, "KillerDiary3Read");
        VerifyNamedCompletedTarget("MorgueBoxB", masterKeyExpected, saveId, "MasterKeyAcquired");

        VerifyCheckpointCompletedStates(saveId);
    }

    /// <summary>이름으로 찾아낸 완료 상태 Target의 IsCompleted와 선택 상태를 검사한다.</summary>
    private void VerifyNamedCompletedTarget(string targetName, bool expected, string saveId, string flagName)
    {
        Target target = FindTarget(targetName);
        Check(target != null, $"{saveId} '{targetName}' Target이 존재합니다.",
            $"{saveId} '{targetName}' Target을 찾지 못했습니다.");
        if (target == null)
        {
            return;
        }

        ICompletedState participant = target.GetComponents<MonoBehaviour>()
            .OfType<ICompletedState>()
            .FirstOrDefault();
        Check(participant != null, $"{saveId} '{targetName}'이 ICompletedState를 구현합니다.",
            $"{saveId} '{targetName}'에 ICompletedState 구현체가 없습니다.");
        if (participant == null)
        {
            return;
        }

        // StoryFlagTarget·ItemUseTarget 등이 같은 Target의 선택 상태를 추가로 제한할 수
        // 있으므로, 이 검증에서는 완료 계약의 복원 결과를 기준으로 판정한다.
        Check(participant.IsCompleted == expected,
            $"{saveId} '{targetName}'이 {flagName} 상태를 반영했습니다.",
            $"{saveId} '{targetName}' 완료 상태가 {flagName}와 다릅니다.");
    }

    /// <summary>각 CheckpointDefinition의 completedStates 값이 복원된 참가자 상태와 일치하는지 검사한다.</summary>
    private void VerifyCheckpointCompletedStates(string saveId)
    {
        if (!checkpointsById.TryGetValue(saveId, out CheckpointDefinition checkpoint) || checkpoint?.CompletedStates == null)
        {
            return;
        }

        foreach (CheckpointCompletedState state in checkpoint.CompletedStates)
        {
            if (state?.Participant == null)
            {
                continue;
            }

            Check(state.Participant.IsCompleted == state.Completed,
                $"{saveId} 완료 상태 '{state.CompletionId}'가 정의값 {state.Completed}로 복원됐습니다.",
                $"{saveId} 완료 상태 '{state.CompletionId}'가 정의값과 다릅니다.");
        }
    }

    /// <summary>Save5 복원 후 최종 추격이 자동 시작되지 않고 계단 도착 Controller가 준비됐는지 검사한다.</summary>
    private void VerifySave5FinalChasePrecondition()
    {
        Check(!gameSession.HasStoryFlag(StoryProgressFlags.FinalChaseStarted),
            "Save5 복원 직후 FinalChaseStarted가 꺼져 있습니다.",
            "Save5 복원 직후 FinalChaseStarted가 자동으로 켜졌습니다.");
        Check(gameSession.CurrentStoryStage == StoryStage.Sector5Ready,
            "Save5 복원 직후 StoryStage가 Sector5Ready입니다.",
            "Save5 복원 직후 StoryStage가 Sector5Ready가 아닙니다.");

        if (finalChaseController == null)
        {
            return;
        }

        Check(!finalChaseController.IsRunning,
            "Save5 복원 직후 최종 추격 시작 Coroutine이 실행 중이 아닙니다.",
            "Save5 복원 직후 최종 추격 시작 Coroutine이 실행 중입니다.");
    }

    /// <summary>복원 검사가 적의 자동 추격 타이밍에 흔들리지 않도록 ChaseSystem을 잠시 격리한다.</summary>
    private void DisableChaseSystemForRestoreChecks()
    {
        if (enemyChaseSystem == null)
        {
            Fail("체크포인트 복원 중 EnemyChaseSystem을 격리할 수 없습니다.");
            return;
        }

        chaseSystemWasEnabled = enemyChaseSystem.enabled;
        enemyChaseSystem.StopAllChases();
        enemyChaseSystem.enabled = false;
        Pass("체크포인트 복원 검증 동안 EnemyChaseSystem을 임시 비활성화했습니다.");
    }

    /// <summary>체크포인트 복원 검사가 끝난 뒤 ChaseSystem의 원래 활성 상태를 돌려놓는다.</summary>
    private void RestoreChaseSystem()
    {
        if (enemyChaseSystem != null)
        {
            enemyChaseSystem.enabled = chaseSystemWasEnabled;
        }
    }

    /// <summary>현재 씬에서 정확한 이름의 Target을 찾는다.</summary>
    private static Target FindTarget(string targetName)
    {
        return FindObjectsByType<Target>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(target => target != null && target.name == targetName);
    }

    /// <summary>조건 결과를 통과 수에 반영하고 통과 로그를 남긴다.</summary>
    private void Check(bool condition, string passMessage, string failureMessage)
    {
        if (condition)
        {
            Pass(passMessage);
            return;
        }

        Fail(failureMessage);
    }

    /// <summary>검증 통과 항목을 기록한다.</summary>
    private void Pass(string message)
    {
        passCount++;
        GameLog.Log($"[Checkpoint 검증 PASS] {message}", this);
    }

    /// <summary>검증 실패 항목을 누적하고 오류 로그를 남긴다.</summary>
    private void Fail(string message)
    {
        failures.Add(message);
        GameLog.LogError($"[Checkpoint 검증 FAIL] {message}", this);
    }

    /// <summary>전체 검증 결과를 한 번에 요약한다.</summary>
    private void ReportResult()
    {
        if (failures.Count == 0)
        {
            GameLog.Log($"[Checkpoint 복원 검증 완료] PASS {passCount}, FAIL 0", this);
            return;
        }

        GameLog.LogError(
            $"[Checkpoint 복원 검증 완료] PASS {passCount}, FAIL {failures.Count}\n- " +
            string.Join("\n- ", failures), this);
    }

    /// <summary>계획서의 Save별 복원 기준을 코드로 표현한 내부 기대값이다.</summary>
    private sealed class CheckpointExpectation
    {
        public StoryStage Stage;
        public string RoomId;
        public bool ActiveEnemy;
        public string EnemyStartRoomId;

        /// <summary>Save ID에 해당하는 계획서 기준 기대값을 반환한다.</summary>
        public static CheckpointExpectation For(string saveId)
        {
            return saveId switch
            {
                "Save1" => new CheckpointExpectation
                {
                    Stage = StoryStage.Sector1,
                    RoomId = "S1_StartBedroom",
                    ActiveEnemy = false,
                    EnemyStartRoomId = string.Empty
                },
                "Save2" => new CheckpointExpectation
                {
                    Stage = StoryStage.Sector2,
                    RoomId = "CenterHallway_A",
                    ActiveEnemy = false,
                    EnemyStartRoomId = string.Empty
                },
                "Save3" => new CheckpointExpectation
                {
                    Stage = StoryStage.Sector3,
                    RoomId = "CenterHallway_B",
                    ActiveEnemy = true,
                    EnemyStartRoomId = "S3_DiningRoom"
                },
                "Save4" => new CheckpointExpectation
                {
                    Stage = StoryStage.Sector4,
                    RoomId = "S4_BasementCorridor",
                    ActiveEnemy = true,
                    EnemyStartRoomId = "S4_MorgueRoom2"
                },
                "Save5" => new CheckpointExpectation
                {
                    Stage = StoryStage.Sector5Ready,
                    RoomId = "S4_MorgueRoom2",
                    ActiveEnemy = true,
                    EnemyStartRoomId = "S4_MorgueRoom2"
                },
                _ => throw new ArgumentOutOfRangeException(nameof(saveId), saveId, "알 수 없는 체크포인트 ID입니다.")
            };
        }
    }
}
#endif
