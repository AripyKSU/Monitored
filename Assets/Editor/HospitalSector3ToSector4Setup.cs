#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Core;
using HauntedHouse.Interaction;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sector3에서 Sector4로 넘어가는 계단 문과 Save4 경계를 기존 시스템에 연결하는 Editor 도구다.
/// 새 진행 매니저를 만들지 않고 DoorTarget, ItemUseTarget, CheckpointResolver의 직렬화 참조만 구성한다.
/// </summary>
public static class HospitalSector3ToSector4Setup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string StairDoorName = "SlideDoor_ServiceHallwayToStair";
    private const string ServiceRoomId = "S3_HallwayB";
    private const string StairwellRoomId = "S4_Stairwell";
    private const string BasementRoomId = "S4_BasementCorridor";
    private const string BasementKeyAssetPath = "Assets/Data/Items/BasementKey.asset";
    private const string LockedMessage = "잠겨있다.";
    private const string FailureMessage = "이 아이템은 지하계단 문을 여는 데 사용할 수 없습니다.";

    /// <summary>
    /// 지하 통로 열쇠를 사용하는 계단 문과 Save1~Save5의 문 복원 상태를 멱등적으로 구성한다.
    /// 현재 활성 씬이 CoreSandbox이고 Edit Mode일 때만 실행할 수 있다.
    /// </summary>
    [MenuItem("Haunted House/Setup/Hospital Sector3 To Sector4")]
    public static void Configure()
    {
        if (!EnsureEditMode() || !EnsureScene())
        {
            return;
        }

        Room serviceRoom = FindRoom(ServiceRoomId);
        Room stairwellRoom = FindRoom(StairwellRoomId);
        ItemDefinition basementKey = AssetDatabase.LoadAssetAtPath<ItemDefinition>(BasementKeyAssetPath);
        CheckpointResolver resolver = FindRequired<CheckpointResolver>("CheckpointResolver");
        CheckpointDefinition save4 = FindCheckpoint("Save4");
        GamePauseService pauseService = FindRequired<GamePauseService>("GamePauseService");
        ItemSelectionPanel selectionPanel = FindRequired<ItemSelectionPanel>("ItemSelectionPanel");
        MessagePanel messagePanel = FindRequired<MessagePanel>("MessagePanel");
        Doorway stairDoorway = FindStairDoorway(serviceRoom, stairwellRoom);

        if (serviceRoom == null || stairwellRoom == null || stairDoorway == null || save4 == null)
        {
            throw new InvalidOperationException(
                $"계단 문 구성을 위해 {ServiceRoomId}, {StairwellRoomId}, '{StairDoorName}', Save4 참조가 모두 필요합니다.");
        }

        if (basementKey == null)
        {
            throw new InvalidOperationException($"지하 통로 열쇠 에셋을 찾지 못했습니다: {BasementKeyAssetPath}");
        }

        // 기본 씬은 Save3에서 시작할 수 있으므로, 계단 문은 항상 잠긴 기준으로 저장한다.
        SetBool(stairDoorway, "locked", true);

        ConfigureDoorDirection(
            stairDoorway,
            serviceRoom,
            basementKey,
            pauseService,
            selectionPanel,
            messagePanel);
        ConfigureDoorDirection(
            stairDoorway,
            stairwellRoom,
            basementKey,
            pauseService,
            selectionPanel,
            messagePanel);

        ConfigureCheckpointDoorwayStates(stairDoorway);
        ConfigureBasementEntryResolverRule(resolver, save4);

        EditorUtility.SetDirty(stairDoorway);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        // 다른 작업자가 열어 둔 씬까지 함께 저장하지 않도록 현재 활성 씬만 저장한다.
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        if (!ValidateConfiguration(out string report))
        {
            throw new InvalidOperationException(report);
        }

        GameLog.Log(report, stairDoorway);
    }

    /// <summary>계단 문, 양방향 Target, BasementKey 및 Save4 경계를 저장 전 검증한다.</summary>
    [MenuItem("Haunted House/Validate/Hospital Sector3 To Sector4")]
    public static void Validate()
    {
        if (!EnsureEditMode() || !EnsureScene())
        {
            return;
        }

        if (ValidateConfiguration(out string report))
        {
            GameLog.Log(report);
        }
        else
        {
            GameLog.LogError(report);
        }
    }

    /// <summary>
    /// 현재 씬의 24번 구성 요소를 읽기 전용으로 검사한다.
    /// 계단 문은 반드시 S3_HallwayB와 S4_Stairwell을 직접 연결해야 한다.
    /// </summary>
    /// <param name="report">검사 결과 요약.</param>
    /// <returns>필수 설정이 모두 유효하면 true.</returns>
    public static bool ValidateConfiguration(out string report)
    {
        var errors = new List<string>();
        Room serviceRoom = FindRoom(ServiceRoomId, errors);
        Room stairwellRoom = FindRoom(StairwellRoomId, errors);
        Room basementRoom = FindRoom(BasementRoomId, errors);
        ItemDefinition basementKey = AssetDatabase.LoadAssetAtPath<ItemDefinition>(BasementKeyAssetPath);
        GameSession gameSession = FindFirst<GameSession>();
        GamePauseService pauseService = FindFirst<GamePauseService>();
        ItemSelectionPanel selectionPanel = FindFirst<ItemSelectionPanel>();
        MessagePanel messagePanel = FindFirst<MessagePanel>();
        Doorway doorway = FindStairDoorway(serviceRoom, stairwellRoom, errors);

        Check(errors, basementKey != null, $"BasementKey 에셋이 없습니다: {BasementKeyAssetPath}");
        Check(errors, gameSession != null, "GameSession을 찾지 못했습니다.");
        Check(errors, pauseService != null, "GamePauseService를 찾지 못했습니다.");
        Check(errors, selectionPanel != null, "ItemSelectionPanel을 찾지 못했습니다.");
        Check(errors, messagePanel != null, "MessagePanel을 찾지 못했습니다.");

        if (doorway != null)
        {
            Check(errors, doorway.TryValidateConfiguration(out string doorwayError), doorwayError);
            Check(errors, doorway.IsLocked, "계단 문은 Save3 기준 잠금 상태여야 합니다.");
            ValidateDoorDirection(
                doorway,
                serviceRoom,
                basementKey,
                pauseService,
                selectionPanel,
                messagePanel,
                errors);
            ValidateDoorDirection(
                doorway,
                stairwellRoom,
                basementKey,
                pauseService,
                selectionPanel,
                messagePanel,
                errors);
        }

        CheckpointDefinition[] checkpoints = FindCheckpoints(errors);
        ValidateCheckpointState(checkpoints, doorway, basementRoom, errors);

        Check(errors,
            HasResolverRuleForBasementEntry(),
            "S4_BasementCorridor 진입 시 Save4를 활성화하는 CheckpointResolver 규칙을 찾지 못했습니다.");

        if (errors.Count > 0)
        {
            report = "Sector3 → Sector4 구성 검증 실패:\n- " + string.Join("\n- ", errors);
            return false;
        }

        report = "Sector3 → Sector4 계단 문, 열쇠 배선, Save4 경계 구성이 유효합니다.";
        return true;
    }

    /// <summary>지정한 방향의 DoorTarget과 ItemUseTarget을 기존 Doorway에 연결한다.</summary>
    private static void ConfigureDoorDirection(
        Doorway doorway,
        Room ownerRoom,
        ItemDefinition basementKey,
        GamePauseService pauseService,
        ItemSelectionPanel selectionPanel,
        MessagePanel messagePanel)
    {
        DoorTarget doorTarget = FindDoorTarget(doorway, ownerRoom);
        if (doorTarget == null)
        {
            throw new InvalidOperationException($"{ownerRoom.RoomId} 방향 계단 DoorTarget을 찾지 못했습니다.");
        }

        Target target = doorTarget.GetComponent<Target>();
        if (target == null)
        {
            throw new InvalidOperationException($"{doorTarget.name}에 Target 컴포넌트가 없습니다.");
        }

        ItemUseTarget itemUseTarget = GetOrAdd<ItemUseTarget>(doorTarget.gameObject);

        SetObject(itemUseTarget, "requiredItem", basementKey);
        SetBool(itemUseTarget, "consumeItemOnSuccess", false);
        SetObject(itemUseTarget, "effectComponent", doorTarget);
        SetObject(itemUseTarget, "inventory", FindRequired<PlayerInventory>("PlayerInventory"));
        SetObject(itemUseTarget, "pauseService", pauseService);
        SetObject(itemUseTarget, "selectionPanel", selectionPanel);
        SetObject(itemUseTarget, "messagePanel", messagePanel);
        SetString(itemUseTarget, "successMessage", string.Empty);
        SetString(itemUseTarget, "failureMessage", FailureMessage);

        SetObject(doorTarget, "lockedItemUseTarget", itemUseTarget);
        SetObject(doorTarget, "lockedMessagePanel", messagePanel);
        SetString(doorTarget, "lockedMessage", LockedMessage);
        SetObject(target, "actionComponent", doorTarget);

        EditorUtility.SetDirty(doorTarget);
        EditorUtility.SetDirty(itemUseTarget);
        EditorUtility.SetDirty(target);
    }

    /// <summary>지정 방향 Target의 열쇠 선택과 DoorTarget 효과 연결을 검사한다.</summary>
    private static void ValidateDoorDirection(
        Doorway doorway,
        Room ownerRoom,
        ItemDefinition basementKey,
        GamePauseService pauseService,
        ItemSelectionPanel selectionPanel,
        MessagePanel messagePanel,
        List<string> errors)
    {
        DoorTarget doorTarget = FindDoorTarget(doorway, ownerRoom, errors);
        if (doorTarget == null)
        {
            return;
        }

        Target target = doorTarget.GetComponent<Target>();
        ItemUseTarget itemUseTarget = doorTarget.GetComponent<ItemUseTarget>();
        Check(errors, target != null, $"{ownerRoom.RoomId} 방향 계단 문에 Target이 없습니다.");
        Check(errors, target == null || target.Action is DoorTarget,
            $"{ownerRoom.RoomId} 방향 계단 문 Target의 행동이 DoorTarget이 아닙니다.");
        Check(errors, itemUseTarget != null,
            $"{ownerRoom.RoomId} 방향 계단 문에 ItemUseTarget이 없습니다.");

        if (itemUseTarget == null)
        {
            return;
        }

        SerializedObject serialized = new(itemUseTarget);
        Check(errors, serialized.FindProperty("requiredItem")?.objectReferenceValue == basementKey,
            $"{ownerRoom.RoomId} 방향 ItemUseTarget의 RequiredItem이 BasementKey가 아닙니다.");
        Check(errors, serialized.FindProperty("consumeItemOnSuccess")?.boolValue == false,
            $"{ownerRoom.RoomId} 방향 ItemUseTarget이 열쇠를 소비하도록 설정되어 있습니다.");
        Check(errors, serialized.FindProperty("effectComponent")?.objectReferenceValue == doorTarget,
            $"{ownerRoom.RoomId} 방향 ItemUseTarget의 EffectComponent가 DoorTarget이 아닙니다.");
        Check(errors, serialized.FindProperty("inventory")?.objectReferenceValue != null,
            $"{ownerRoom.RoomId} 방향 ItemUseTarget의 Inventory 참조가 없습니다.");
        Check(errors, serialized.FindProperty("pauseService")?.objectReferenceValue == pauseService,
            $"{ownerRoom.RoomId} 방향 ItemUseTarget의 PauseService 참조가 다릅니다.");
        Check(errors, serialized.FindProperty("selectionPanel")?.objectReferenceValue == selectionPanel,
            $"{ownerRoom.RoomId} 방향 ItemUseTarget의 SelectionPanel 참조가 다릅니다.");
        Check(errors, serialized.FindProperty("messagePanel")?.objectReferenceValue == messagePanel,
            $"{ownerRoom.RoomId} 방향 ItemUseTarget의 MessagePanel 참조가 다릅니다.");

        SerializedObject doorSerialized = new(doorTarget);
        Check(errors, doorSerialized.FindProperty("lockedItemUseTarget")?.objectReferenceValue == itemUseTarget,
            $"{ownerRoom.RoomId} 방향 DoorTarget의 잠금 ItemUseTarget 참조가 다릅니다.");
        Check(errors, doorSerialized.FindProperty("lockedMessagePanel")?.objectReferenceValue == messagePanel,
            $"{ownerRoom.RoomId} 방향 DoorTarget의 잠금 메시지 참조가 다릅니다.");
    }

    /// <summary>Save1~Save5에 계단 문 잠금 상태를 중복 없이 기록한다.</summary>
    private static void ConfigureCheckpointDoorwayStates(Doorway stairDoorway)
    {
        foreach (string checkpointId in new[] { "Save1", "Save2", "Save3", "Save4", "Save5" })
        {
            CheckpointDefinition checkpoint = FindCheckpoint(checkpointId);
            if (checkpoint == null)
            {
                throw new InvalidOperationException($"체크포인트 '{checkpointId}'를 찾지 못했습니다.");
            }

            bool locked = checkpointId is "Save1" or "Save2" or "Save3";
            SerializedObject serialized = new(checkpoint);
            SerializedProperty states = serialized.FindProperty("doorwayStates");
            int existingIndex = -1;
            for (int index = 0; index < states.arraySize; index++)
            {
                if (states.GetArrayElementAtIndex(index).FindPropertyRelative("doorway").objectReferenceValue == stairDoorway)
                {
                    existingIndex = index;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                existingIndex = states.arraySize;
                states.InsertArrayElementAtIndex(existingIndex);
            }

            SerializedProperty state = states.GetArrayElementAtIndex(existingIndex);
            state.FindPropertyRelative("doorway").objectReferenceValue = stairDoorway;
            state.FindPropertyRelative("locked").boolValue = locked;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(checkpoint);
        }
    }

    /// <summary>S4_BasementCorridor 최초 진입 조건과 Save4 활성화 규칙을 기존 Resolver에 연결한다.</summary>
    private static void ConfigureBasementEntryResolverRule(CheckpointResolver resolver, CheckpointDefinition save4)
    {
        SerializedObject serialized = new(resolver);
        SerializedProperty rules = serialized.FindProperty("rules");
        if (rules == null)
        {
            throw new InvalidOperationException("CheckpointResolver의 rules SerializedProperty를 찾지 못했습니다.");
        }

        int ruleIndex = -1;
        for (int index = 0; index < rules.arraySize; index++)
        {
            SerializedProperty rule = rules.GetArrayElementAtIndex(index);
            string subjectId = rule.FindPropertyRelative("subjectId").stringValue;
            string flagId = rule.FindPropertyRelative("flagId").stringValue;
            if (subjectId == BasementRoomId || flagId == StoryProgressFlags.BasementEntered)
            {
                ruleIndex = index;
                break;
            }
        }

        if (ruleIndex < 0)
        {
            ruleIndex = rules.arraySize;
            rules.InsertArrayElementAtIndex(ruleIndex);
        }

        SerializedProperty targetRule = rules.GetArrayElementAtIndex(ruleIndex);
        targetRule.FindPropertyRelative("eventType").enumValueIndex = (int)StoryEventType.RoomEntered;
        targetRule.FindPropertyRelative("subjectId").stringValue = BasementRoomId;
        targetRule.FindPropertyRelative("requiredFlagId").stringValue = StoryProgressFlags.BasementKeyAcquired;
        targetRule.FindPropertyRelative("flagId").stringValue = StoryProgressFlags.BasementEntered;
        targetRule.FindPropertyRelative("checkpoint").objectReferenceValue = save4;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(resolver);
    }

    /// <summary>체크포인트의 방·Stage·열쇠·계단 문 복원 상태를 검사한다.</summary>
    private static void ValidateCheckpointState(
        CheckpointDefinition[] checkpoints,
        Doorway stairDoorway,
        Room basementRoom,
        List<string> errors)
    {
        foreach (CheckpointDefinition checkpoint in checkpoints)
        {
            if (checkpoint == null)
            {
                continue;
            }

            Check(errors, checkpoint.TryValidateConfiguration(out string checkpointError), checkpointError);
            CheckpointDoorwayState doorwayState = checkpoint.DoorwayStates?
                .FirstOrDefault(state => state != null && state.Doorway == stairDoorway);
            Check(errors, doorwayState != null,
                $"{checkpoint.CheckpointId}에 계단 문 복원 상태가 없습니다.");

            if (doorwayState != null)
            {
                bool expectedLocked = checkpoint.CheckpointId is "Save1" or "Save2" or "Save3";
                Check(errors, doorwayState.Locked == expectedLocked,
                    $"{checkpoint.CheckpointId} 계단 문 잠금 상태가 예상과 다릅니다.");
            }
        }

        CheckpointDefinition save4 = checkpoints.FirstOrDefault(checkpoint => checkpoint?.CheckpointId == "Save4");
        if (save4 == null)
        {
            return;
        }

        Check(errors, save4.RespawnRoom == basementRoom, "Save4 RespawnRoom이 S4_BasementCorridor가 아닙니다.");
        Check(errors, save4.StoryStage == StoryStage.Sector4, "Save4 StoryStage가 Sector4가 아닙니다.");
        Check(errors, save4.ProgressFlags.Contains(StoryProgressFlags.BasementKeyAcquired),
            "Save4에 BasementKeyAcquired flag가 없습니다.");
        Check(errors, save4.ProgressFlags.Contains(StoryProgressFlags.BasementEntered),
            "Save4에 BasementEntered flag가 없습니다.");
        Check(errors, save4.InventoryItems.Any(item => item != null && item.ItemId == "basement_key"),
            "Save4 Inventory에 BasementKey가 없습니다.");
    }

    /// <summary>Save1~Save5를 고유 ID로 찾아 반환한다.</summary>
    private static CheckpointDefinition[] FindCheckpoints(List<string> errors)
    {
        var checkpoints = new List<CheckpointDefinition>();
        foreach (string id in new[] { "Save1", "Save2", "Save3", "Save4", "Save5" })
        {
            CheckpointDefinition checkpoint = FindCheckpoint(id, errors);
            if (checkpoint != null)
            {
                checkpoints.Add(checkpoint);
            }
        }

        return checkpoints.ToArray();
    }

    /// <summary>현재 Resolver에 BasementCorridor 진입 규칙이 존재하는지 확인한다.</summary>
    private static bool HasResolverRuleForBasementEntry()
    {
        CheckpointResolver resolver = FindFirst<CheckpointResolver>();
        if (resolver == null)
        {
            return false;
        }

        SerializedObject serialized = new(resolver);
        SerializedProperty rules = serialized.FindProperty("rules");
        if (rules == null)
        {
            return false;
        }

        for (int index = 0; index < rules.arraySize; index++)
        {
            SerializedProperty rule = rules.GetArrayElementAtIndex(index);
            if (rule.FindPropertyRelative("eventType").enumValueIndex == (int)StoryEventType.RoomEntered &&
                rule.FindPropertyRelative("subjectId").stringValue == BasementRoomId &&
                rule.FindPropertyRelative("requiredFlagId").stringValue == StoryProgressFlags.BasementKeyAcquired &&
                rule.FindPropertyRelative("flagId").stringValue == StoryProgressFlags.BasementEntered &&
                (rule.FindPropertyRelative("checkpoint").objectReferenceValue as CheckpointDefinition)?.CheckpointId == "Save4")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Doorway에서 지정한 소유 방의 방향별 DoorTarget을 찾는다.</summary>
    private static DoorTarget FindDoorTarget(Doorway doorway, Room ownerRoom, List<string> errors = null)
    {
        DoorTarget[] targets = doorway == null
            ? Array.Empty<DoorTarget>()
            : doorway.GetComponentsInChildren<DoorTarget>(true)
                .Where(target => target != null && target.GetComponent<Target>()?.OwnerRoom == ownerRoom)
                .ToArray();

        if (targets.Length == 1)
        {
            return targets[0];
        }

        if (targets.Length == 0)
        {
            errors?.Add($"{ownerRoom?.RoomId ?? "알 수 없음"} 방향 계단 DoorTarget을 찾지 못했습니다.");
        }
        else
        {
            errors?.Add($"{ownerRoom.RoomId} 방향 계단 DoorTarget이 여러 개입니다: " +
                        string.Join(", ", targets.Select(target => target.name)));
        }

        return null;
    }

    /// <summary>계단 Doorway를 이름과 양쪽 Room으로 유일하게 찾는다.</summary>
    private static Doorway FindStairDoorway(Room serviceRoom, Room stairwellRoom, List<string> errors = null)
    {
        Doorway[] candidates = UnityEngine.Object.FindObjectsByType<Doorway>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Where(doorway => doorway != null && doorway.name == StairDoorName &&
                             ((doorway.RoomA == serviceRoom && doorway.RoomB == stairwellRoom) ||
                              (doorway.RoomA == stairwellRoom && doorway.RoomB == serviceRoom)))
            .ToArray();

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        if (candidates.Length == 0)
        {
            errors?.Add($"'{StairDoorName}' Doorway가 {ServiceRoomId} ↔ {StairwellRoomId} 연결로 구성되지 않았습니다.");
        }
        else
        {
            errors?.Add($"'{StairDoorName}' Doorway 후보가 여러 개입니다.");
        }

        return null;
    }

    /// <summary>방 ID로 현재 씬의 Room을 찾는다.</summary>
    private static Room FindRoom(string roomId, List<string> errors = null)
    {
        Room room = UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.RoomId == roomId);
        if (room == null)
        {
            errors?.Add($"Room '{roomId}'를 찾지 못했습니다.");
        }

        return room;
    }

    /// <summary>체크포인트 ID로 현재 씬의 CheckpointDefinition을 찾는다.</summary>
    private static CheckpointDefinition FindCheckpoint(string checkpointId, List<string> errors = null)
    {
        CheckpointDefinition checkpoint = UnityEngine.Object.FindObjectsByType<CheckpointDefinition>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.CheckpointId == checkpointId);
        if (checkpoint == null)
        {
            errors?.Add($"CheckpointDefinition '{checkpointId}'를 찾지 못했습니다.");
        }

        return checkpoint;
    }

    /// <summary>씬에서 지정한 타입의 첫 객체를 찾는다.</summary>
    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    }

    /// <summary>씬에서 필수 타입 참조를 찾고 없으면 구성 오류를 발생시킨다.</summary>
    private static T FindRequired<T>(string label) where T : UnityEngine.Object
    {
        return FindFirst<T>() ?? throw new InvalidOperationException($"{label} 참조를 찾지 못했습니다.");
    }

    /// <summary>컴포넌트가 없을 때만 Undo 가능한 방식으로 추가한다.</summary>
    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    /// <summary>private Unity Object 참조 필드를 직렬화한다.</summary>
    private static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>private 문자열 필드를 직렬화한다.</summary>
    private static void SetString(UnityEngine.Object target, string propertyName, string value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        }

        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>private bool 필드를 직렬화한다.</summary>
    private static void SetBool(UnityEngine.Object target, string propertyName, bool value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        }

        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>검사 조건이 거짓이면 오류 목록에 메시지를 추가한다.</summary>
    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition && !string.IsNullOrWhiteSpace(message))
        {
            errors.Add(message);
        }
    }

    /// <summary>현재 Editor가 Play Mode가 아님을 확인한다.</summary>
    private static bool EnsureEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Sector3 → Sector4 Setup/Validate는 Play Mode가 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return false;
        }

        return true;
    }

    /// <summary>현재 활성 씬이 CoreSandbox인지 확인한다.</summary>
    private static bool EnsureScene()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning($"Sector3 → Sector4 Setup/Validate는 '{ScenePath}' 씬에서 실행해야 합니다.");
            return false;
        }

        return true;
    }
}
#endif
