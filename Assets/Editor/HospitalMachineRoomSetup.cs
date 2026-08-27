#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 Sector 2 MachineRoom 장치와 ToolCase 은신 Target을 구성한다.
/// 기존 소품 Mesh를 직접 수정하지 않고 MachineRoom 하위의 명시적인 Target Root만 생성하거나 갱신한다.
/// </summary>
public static class HospitalMachineRoomSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string SetupMenuPath = "Haunted House/Setup/Hospital Sector2 MachineRoom";
    private const string ValidateMenuPath = "Haunted House/Validate/Hospital Sector2 MachineRoom";
    private const string MachineRoomId = "S2_MachineRoom";
    private const string MachineRoomTargetRootName = "Sector2MachineRoomTargets";
    private const string MachineTargetName = "S2_MachineRoom_Tank_1";
    private const string ToolCaseTargetName = "S2_MachineRoom_ToolCase";
    private const string TankPropName = "MachineRoom_Tank_1";
    private const string ToolCasePropName = "MachineRoom_ToolCase";
    private const string LogicalGateName = "LogicalGate_CenterHallway_A_B";

    /// <summary>
    /// 현재 CoreSandbox에 MachineRoom 장치, ToolCase 은신 Target과 체크포인트 상태를 생성하거나 갱신한다.
    /// 이 메서드는 Play 모드가 아닌 Unity Editor에서 메뉴로 실행해야 한다.
    /// </summary>
    [MenuItem(SetupMenuPath)]
    public static void Configure()
    {
        EnsureScene();

        Room machineRoom = FindRoom(MachineRoomId);
        GameSession gameSession = FindRequired<GameSession>("GameSession");
        GateController gateController = FindGateController();
        ScreenShake screenShake = FindRequired<ScreenShake>("Main Camera의 ScreenShake");
        MessagePanel messagePanel = FindRequired<MessagePanel>("MessagePanel");
        StealthHideSystem stealthHideSystem = FindRequired<StealthHideSystem>("StealthHideSystem");
        PlayerRoomTracker playerRoomTracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        TargetMarkerPool markerPool = FindRequired<TargetMarkerPool>("TargetMarkerPool");
        Camera worldCamera = Camera.main != null
            ? Camera.main
            : UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (worldCamera == null)
        {
            throw new InvalidOperationException("MachineRoom Target이 사용할 Camera를 찾지 못했습니다.");
        }

        Transform tankProp = FindProp(machineRoom, TankPropName);
        Transform toolCaseProp = FindProp(machineRoom, ToolCasePropName);
        Transform targetRoot = GetOrCreateChild(machineRoom.transform, MachineRoomTargetRootName);

        Target machineTarget = ConfigureMachineTarget(
            machineRoom,
            targetRoot,
            tankProp,
            gameSession,
            gateController,
            screenShake,
            messagePanel,
            playerRoomTracker,
            markerPool,
            worldCamera);

        Target toolCaseTarget = ConfigureToolCaseTarget(
            machineRoom,
            targetRoot,
            toolCaseProp,
            stealthHideSystem,
            playerRoomTracker,
            markerPool,
            worldCamera);

        ConfigureEncounterToolCaseReference(toolCaseTarget);
        ConfigureCheckpointStates(machineTarget.GetComponent<MachineRoomTarget>());

        EditorUtility.SetDirty(machineTarget.gameObject);
        EditorUtility.SetDirty(toolCaseTarget.gameObject);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameLog.Log(
            $"Hospital Sector 2 MachineRoom 구성을 완료했습니다: {machineTarget.name}, {toolCaseTarget.name}.");
    }

    /// <summary>현재 씬의 MachineRoom 장치와 ToolCase 구성을 읽기 전용으로 검사한다.</summary>
    [MenuItem(ValidateMenuPath)]
    public static void Validate()
    {
        EnsureScene();

        List<string> errors = new();
        Room machineRoom = FindRoomOrNull(MachineRoomId);
        Check(errors, machineRoom != null, $"{MachineRoomId} Room이 없습니다.");

        Target machineTarget = machineRoom != null
            ? FindTarget(machineRoom, MachineTargetName)
            : null;
        Target toolCaseTarget = machineRoom != null
            ? FindTarget(machineRoom, ToolCaseTargetName)
            : null;
        CheckTarget(errors, machineTarget, machineRoom, MachineTargetName);
        CheckTarget(errors, toolCaseTarget, machineRoom, ToolCaseTargetName);

        MachineRoomTarget machineAction = machineTarget != null
            ? machineTarget.Action as MachineRoomTarget
            : null;
        Check(errors, machineAction != null, $"{MachineTargetName}의 행동이 MachineRoomTarget이 아닙니다.");
        if (machineAction != null)
        {
            Check(errors, machineAction.TryValidateConfiguration(out string machineError), machineError);
            CheckCheckpointStates(errors, machineAction);
        }

        StealthHideTarget hideAction = toolCaseTarget != null
            ? toolCaseTarget.Action as StealthHideTarget
            : null;
        Check(errors, hideAction != null, $"{ToolCaseTargetName}의 행동이 StealthHideTarget이 아닙니다.");
        if (hideAction != null)
        {
            SerializedObject serialized = new(hideAction);
            Check(errors,
                serialized.FindProperty("stealthHideSystem")?.objectReferenceValue != null,
                $"{ToolCaseTargetName}의 StealthHideSystem 참조가 비어 있습니다.");
        }

        Check(errors, machineRoom != null && FindPropOrNull(machineRoom, TankPropName) != null,
            $"{TankPropName}을 찾지 못했습니다.");
        Check(errors, machineRoom != null && FindPropOrNull(machineRoom, ToolCasePropName) != null,
            $"{ToolCasePropName}을 찾지 못했습니다.");
        Check(errors, FindGateControllerOrNull() != null,
            $"{LogicalGateName}의 GateController를 찾지 못했습니다.");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Hospital Sector 2 MachineRoom 검증 실패:\n- " + string.Join("\n- ", errors));
        }

        GameLog.Log("Hospital Sector 2 MachineRoom 구성이 유효합니다.");
    }

    /// <summary>현재 활성 씬과 Editor 실행 상태가 MachineRoom 설정 조건에 맞는지 확인한다.</summary>
    private static void EnsureScene()
    {
        if (EditorApplication.isPlaying)
        {
            throw new InvalidOperationException("Hospital Sector 2 MachineRoom 설정은 Play 모드에서 실행할 수 없습니다.");
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path != ScenePath)
        {
            throw new InvalidOperationException("Hospital Sector 2 MachineRoom 설정은 CoreSandbox 씬에서만 실행할 수 있습니다.");
        }

        // 여러 에이전트가 같은 씬을 편집하는 동안 다른 작업자의 미저장 변경을
        // 이 도구가 함께 저장하지 않도록, 호출자가 먼저 씬을 저장한 경우에만 실행한다.
        if (activeScene.isDirty)
        {
            throw new InvalidOperationException(
                "CoreSandbox 씬에 미저장 변경이 있습니다. 다른 작업자의 변경을 확인하고 먼저 씬을 저장한 뒤 다시 실행하세요.");
        }
    }

    /// <summary>현재 씬에서 필수 컴포넌트를 찾는다.</summary>
    private static T FindRequired<T>(string label) where T : Component
    {
        T component = UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        return component != null
            ? component
            : throw new InvalidOperationException($"{label} 컴포넌트를 찾지 못했습니다.");
    }

    /// <summary>중앙 복도 논리 Gate에 연결된 GateController를 이름으로 찾는다.</summary>
    private static GateController FindGateController()
    {
        return FindGateControllerOrNull()
            ?? throw new InvalidOperationException($"{LogicalGateName}의 GateController를 찾지 못했습니다.");
    }

    /// <summary>중앙 복도 논리 Gate의 GateController를 찾되 없으면 null을 반환한다.</summary>
    private static GateController FindGateControllerOrNull()
    {
        return UnityEngine.Object.FindObjectsByType<GateController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .FirstOrDefault(controller => controller.name == LogicalGateName);
    }

    /// <summary>RoomId로 MachineRoom을 찾는다.</summary>
    private static Room FindRoom(string roomId)
    {
        return FindRoomOrNull(roomId)
            ?? throw new InvalidOperationException($"Room '{roomId}'을 찾지 못했습니다.");
    }

    /// <summary>RoomId로 Room을 찾되 없으면 null을 반환한다.</summary>
    private static Room FindRoomOrNull(string roomId)
    {
        return UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => string.Equals(room.RoomId, roomId, StringComparison.Ordinal));
    }

    /// <summary>Room의 자식 계층에서 지정한 소품을 찾는다.</summary>
    private static Transform FindProp(Room room, string propName)
    {
        return FindPropOrNull(room, propName)
            ?? throw new InvalidOperationException($"Room '{room.RoomId}'에서 소품 '{propName}'을 찾지 못했습니다.");
    }

    /// <summary>Room의 자식 계층에서 지정한 소품을 찾되 없으면 null을 반환한다.</summary>
    private static Transform FindPropOrNull(Room room, string propName)
    {
        return room == null
            ? null
            : room.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == propName);
    }

    /// <summary>MachineRoom Target Root 아래에서 이름이 일치하는 Target을 찾는다.</summary>
    private static Target FindTarget(Room room, string targetName)
    {
        return room == null
            ? null
            : room.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => target.name == targetName);
    }

    /// <summary>기계 장치 Target과 MachineRoomTarget 행동을 연결한다.</summary>
    private static Target ConfigureMachineTarget(
        Room ownerRoom,
        Transform targetRoot,
        Transform tankProp,
        GameSession gameSession,
        GateController gateController,
        ScreenShake screenShake,
        MessagePanel messagePanel,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Transform targetTransform = GetOrCreateChild(targetRoot, MachineTargetName);
        Vector3 targetPosition = tankProp.position + Vector3.up * 1.2f;
        Vector3 requestedApproachPosition = tankProp.position + Vector3.right * 8f;
        Target target = ConfigureTargetShell(
            ownerRoom,
            targetTransform,
            targetPosition,
            requestedApproachPosition,
            playerRoomTracker,
            markerPool,
            worldCamera);

        MachineRoomTarget machine = GetOrAdd<MachineRoomTarget>(targetTransform.gameObject);
        SetObject(machine, "gameSession", gameSession);
        SetObject(machine, "gateController", gateController);
        SetObject(machine, "screenShake", screenShake);
        SetObject(machine, "messagePanel", messagePanel);
        SetString(machine, "completionId", MachineTargetName);
        SetString(machine, "noPowerMessage", "파워가 들어오지 않는다.");
        SetObject(target, "actionComponent", machine);
        target.SetSelectable(true);
        EditorUtility.SetDirty(machine);
        return target;
    }

    /// <summary>ToolCase 소품 위치에 실제 은신 시스템으로 진입하는 Target을 연결한다.</summary>
    private static Target ConfigureToolCaseTarget(
        Room ownerRoom,
        Transform targetRoot,
        Transform toolCaseProp,
        StealthHideSystem stealthHideSystem,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Transform targetTransform = GetOrCreateChild(targetRoot, ToolCaseTargetName);
        Vector3 targetPosition = toolCaseProp.position + Vector3.up * 0.8f;
        Vector3 requestedApproachPosition = toolCaseProp.position + Vector3.forward * 5f;
        Target target = ConfigureTargetShell(
            ownerRoom,
            targetTransform,
            targetPosition,
            requestedApproachPosition,
            playerRoomTracker,
            markerPool,
            worldCamera);

        StealthHideTarget hideTarget = GetOrAdd<StealthHideTarget>(targetTransform.gameObject);
        SetObject(hideTarget, "stealthHideSystem", stealthHideSystem);
        SetObject(target, "actionComponent", hideTarget);
        target.SetSelectable(true);
        EditorUtility.SetDirty(hideTarget);
        return target;
    }

    /// <summary>
    /// 이미 씬에 존재하는 Sector2 강제 숨기 Controller가 ToolCase를 직접 제어하도록 연결한다.
    /// Controller가 아직 구성되지 않은 경우에는 16번 설정을 막지 않고 이후 17번 도구가 연결할 수 있게 둔다.
    /// </summary>
    private static void ConfigureEncounterToolCaseReference(Target toolCaseTarget)
    {
        Sector2HideEncounterController encounterController = UnityEngine.Object.FindFirstObjectByType<
            Sector2HideEncounterController>(FindObjectsInactive.Include);
        if (encounterController == null)
        {
            return;
        }

        SetObject(encounterController, "toolCaseTarget", toolCaseTarget);
        EditorUtility.SetDirty(encounterController);
    }

    /// <summary>Target의 접근 지점, 소유 방과 화면 표시 참조를 공통 규칙으로 설정한다.</summary>
    private static Target ConfigureTargetShell(
        Room ownerRoom,
        Transform targetTransform,
        Vector3 targetPosition,
        Vector3 requestedApproachPosition,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        targetTransform.position = targetPosition;

        // 클릭 영역은 소품 MeshCollider와 분리한 Trigger로 만들어 실제 플레이어 충돌을 바꾸지 않는다.
        BoxCollider clickArea = GetOrAdd<BoxCollider>(targetTransform.gameObject);
        clickArea.isTrigger = true;
        clickArea.center = Vector3.up;
        clickArea.size = new Vector3(2.5f, 2.5f, 2.5f);

        Transform approachPoint = GetOrCreateChild(targetTransform, "ApproachPoint");
        approachPoint.position = SampleNavMesh(requestedApproachPosition, requestedApproachPosition);
        Vector3 facingDirection = targetPosition - approachPoint.position;
        approachPoint.rotation = facingDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(facingDirection, Vector3.up)
            : Quaternion.identity;

        Transform uiAnchor = GetOrCreateChild(targetTransform, "UIAnchor");
        uiAnchor.position = targetPosition + Vector3.up * 1.6f;

        Target target = GetOrAdd<Target>(targetTransform.gameObject);
        SetObject(target, "approachPoint", approachPoint);
        SetObject(target, "ownerRoom", ownerRoom);
        SetBool(target, "selectable", true);

        TargetPresenter presenter = GetOrAdd<TargetPresenter>(targetTransform.gameObject);
        SetObject(presenter, "target", target);
        SetObject(presenter, "clickArea", clickArea);
        SetObject(presenter, "uiAnchor", uiAnchor);
        SetObject(presenter, "playerRoomTracker", playerRoomTracker);
        SetObject(presenter, "worldCamera", worldCamera);
        SetObject(presenter, "markerPool", markerPool);

        EditorUtility.SetDirty(targetTransform.gameObject);
        return target;
    }

    /// <summary>Save1~Save5에 MachineRoom 장치 완료 상태를 체크포인트 기준으로 추가한다.</summary>
    private static void ConfigureCheckpointStates(MachineRoomTarget machineTarget)
    {
        if (machineTarget == null)
        {
            throw new InvalidOperationException("MachineRoomTarget을 찾지 못해 체크포인트 상태를 구성할 수 없습니다.");
        }

        string[] checkpointIds = { "Save1", "Save2", "Save3", "Save4", "Save5" };
        foreach (string checkpointId in checkpointIds)
        {
            CheckpointDefinition checkpoint = UnityEngine.Object.FindObjectsByType<CheckpointDefinition>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.CheckpointId == checkpointId);
            if (checkpoint == null)
            {
                throw new InvalidOperationException($"CheckpointDefinition '{checkpointId}'를 찾지 못했습니다.");
            }

            bool completed = string.Equals(checkpointId, "Save3", StringComparison.Ordinal) ||
                             string.Equals(checkpointId, "Save4", StringComparison.Ordinal) ||
                             string.Equals(checkpointId, "Save5", StringComparison.Ordinal);
            SerializedObject serialized = new(checkpoint);
            SerializedProperty states = serialized.FindProperty("completedStates");
            if (states == null || !states.isArray)
            {
                throw new InvalidOperationException(
                    $"CheckpointDefinition '{checkpointId}'의 completedStates를 찾지 못했습니다.");
            }

            int existingIndex = -1;
            for (int index = 0; index < states.arraySize; index++)
            {
                SerializedProperty participant = states.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("participant");
                if (participant != null && participant.objectReferenceValue == machineTarget)
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
            state.FindPropertyRelative("participant").objectReferenceValue = machineTarget;
            state.FindPropertyRelative("completed").boolValue = completed;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(checkpoint);
        }
    }

    /// <summary>MachineRoomTarget이 모든 Save 체크포인트에 올바른 완료 값으로 등록됐는지 검사한다.</summary>
    private static void CheckCheckpointStates(List<string> errors, MachineRoomTarget machineTarget)
    {
        if (machineTarget == null)
        {
            return;
        }

        foreach (string checkpointId in new[] { "Save1", "Save2", "Save3", "Save4", "Save5" })
        {
            CheckpointDefinition checkpoint = UnityEngine.Object.FindObjectsByType<CheckpointDefinition>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.CheckpointId == checkpointId);
            Check(errors, checkpoint != null, $"CheckpointDefinition '{checkpointId}'를 찾지 못했습니다.");
            if (checkpoint == null)
            {
                continue;
            }

            bool expected = checkpointId is "Save3" or "Save4" or "Save5";
            Check(errors,
                checkpoint.CompletedStates.Any(state => state != null &&
                    state.CompletionId == machineTarget.CompletionId && state.Completed == expected),
                $"{checkpointId}에 MachineRoom 완료 상태 {expected}가 등록되지 않았습니다.");
        }
    }

    /// <summary>Target 공통 구성과 행동 설정을 검사한다.</summary>
    private static void CheckTarget(List<string> errors, Target target, Room ownerRoom, string targetName)
    {
        Check(errors, target != null, $"{targetName}을 찾지 못했습니다.");
        if (target == null)
        {
            return;
        }

        Check(errors, target.OwnerRoom == ownerRoom, $"{targetName}의 OwnerRoom이 {MachineRoomId}가 아닙니다.");
        Check(errors, target.GetComponent<TargetPresenter>() != null,
            $"{targetName}에 TargetPresenter가 없습니다.");
        Check(errors, target.GetComponent<BoxCollider>()?.isTrigger == true,
            $"{targetName}에 Trigger BoxCollider가 없습니다.");
        if (!target.TryValidateConfiguration(out string configurationError))
        {
            errors.Add($"{targetName}: {configurationError}");
        }
    }

    /// <summary>요청 위치 주변 NavMesh에서 플레이어 접근 지점을 선택한다.</summary>
    private static Vector3 SampleNavMesh(Vector3 requested, Vector3 fallback)
    {
        if (NavMesh.SamplePosition(requested, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        if (NavMesh.SamplePosition(fallback, out hit, 20f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return requested;
    }

    /// <summary>부모 아래에 지정한 이름의 자식을 하나만 만들고 반환한다.</summary>
    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new(childName);
        childObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(childObject, "Create Hospital Sector2 MachineRoom child");
        return childObject.transform;
    }

    /// <summary>같은 GameObject에 컴포넌트가 없을 때만 하나 추가한다.</summary>
    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    /// <summary>컴포넌트의 private Unity 참조 필드를 직렬화해 저장한다.</summary>
    private static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>컴포넌트의 private 문자열 필드를 직렬화해 저장한다.</summary>
    private static void SetString(UnityEngine.Object target, string propertyName, string value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        }

        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>컴포넌트의 private bool 필드를 직렬화해 저장한다.</summary>
    private static void SetBool(UnityEngine.Object target, string propertyName, bool value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        }

        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>검사 조건이 거짓이면 사람이 확인할 오류를 목록에 추가한다.</summary>
    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}
#endif
