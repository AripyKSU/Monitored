#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using HauntedHouse.Stealth;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 Sector4 ControlRoom·UtilityRoom에 기타 Hide/Message Target을 구성하는 Editor 도구다.
/// 기존 TargetPresenter, MessageTarget, StealthHideTarget과 공용 은신 시스템을 재사용하며 멱등적으로 동작한다.
/// </summary>
public static class HospitalSector4MiscTargetsSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string SetupMenuPath = "Haunted House/Setup/Hospital Sector4 Misc Targets";
    private const string ValidateMenuPath = "Haunted House/Validate/Hospital Sector4 Misc Targets";

    private const string ControlRoomId = "S4_ControlRoom";
    private const string UtilityRoomId = "S4_UtilityRoom";
    private const string ControlTargetRootName = "Sector4ControlRoomTargets";
    private const string UtilityTargetRootName = "Sector4UtilityRoomTargets";

    private const string ControlConsoleTargetName = "Control_ConsoleDesk";
    private const string UtilitySwitchTargetName = "Utility_SwitchCase";
    private const string ElectricalCabinetTargetName = "Utility_ElectricalCabinet";

    private const string ControlConsoleAnchorName = "Control_ConsoleDeskAnchor";
    private const string UtilitySwitchAnchorName = "Utility_SwitchCaseAnchor";
    private const string ElectricalCabinetAnchorName = "Utility_ElectricalCabinetAnchor";

    // 29번 명세에서 문구의 실제 본문은 사용자 검수 대상으로 남겨 두었으므로 현재는 명세 표기만 사용한다.
    private const string SurvivorMessage = "[생존자의 메시지]";

    /// <summary>Sector4의 세 가지 기타 Target을 생성하거나 현재 씬 설정에 맞게 갱신한다.</summary>
    [MenuItem(SetupMenuPath)]
    public static void Configure()
    {
        EnsureEditModeAndScene();

        Room controlRoom = FindRequiredRoom(ControlRoomId);
        Room utilityRoom = FindRequiredRoom(UtilityRoomId);
        MessagePanel messagePanel = FindRequired<MessagePanel>("MessagePanel");
        PlayerRoomTracker playerRoomTracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        TargetMarkerPool markerPool = FindRequired<TargetMarkerPool>("TargetMarkerPool");
        StealthHideSystem stealthHideSystem = FindRequired<StealthHideSystem>("StealthHideSystem");
        Camera worldCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (worldCamera == null)
        {
            throw new InvalidOperationException("Sector4 기타 Target이 사용할 Camera를 찾지 못했습니다.");
        }

        Transform controlRoot = GetOrCreateChild(controlRoom.transform, ControlTargetRootName);
        Transform utilityRoot = GetOrCreateChild(utilityRoom.transform, UtilityTargetRootName);

        Transform consoleAnchor = ResolvePropOrAnchor(
            controlRoom, ControlConsoleTargetName, ControlConsoleAnchorName, Vector3.forward * 2f);
        Transform switchAnchor = ResolvePropOrAnchor(
            utilityRoom, UtilitySwitchTargetName, UtilitySwitchAnchorName, Vector3.forward * 2f);
        Transform cabinetAnchor = ResolvePropOrAnchor(
            utilityRoom, ElectricalCabinetTargetName, ElectricalCabinetAnchorName, Vector3.forward * 2f);

        Target consoleTarget = ConfigureHideTarget(
            controlRoom, controlRoot, ControlConsoleTargetName, consoleAnchor, stealthHideSystem,
            playerRoomTracker, markerPool, worldCamera, 0.8f, 2.5f);
        Target switchTarget = ConfigureHideTarget(
            utilityRoom, utilityRoot, UtilitySwitchTargetName, switchAnchor, stealthHideSystem,
            playerRoomTracker, markerPool, worldCamera, 0.8f, 2.5f);
        Target cabinetTarget = ConfigureMessageTarget(
            utilityRoom, utilityRoot, ElectricalCabinetTargetName, cabinetAnchor, SurvivorMessage,
            messagePanel, playerRoomTracker, markerPool, worldCamera);

        EditorUtility.SetDirty(controlRoom.gameObject);
        EditorUtility.SetDirty(utilityRoom.gameObject);
        EditorUtility.SetDirty(controlRoot.gameObject);
        EditorUtility.SetDirty(utilityRoot.gameObject);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!ValidateConfiguration(out string report))
        {
            throw new InvalidOperationException(report);
        }

        GameLog.Log(
            $"Sector4 기타 Target 구성을 완료했습니다: {consoleTarget.name}, {switchTarget.name}, {cabinetTarget.name}.");
    }

    /// <summary>ControlRoom·UtilityRoom의 Target과 공용 참조를 변경 없이 검사한다.</summary>
    [MenuItem(ValidateMenuPath)]
    public static void Validate()
    {
        EnsureEditModeAndScene();

        if (ValidateConfiguration(out string report))
        {
            GameLog.Log(report);
        }
        else
        {
            GameLog.LogError(report);
        }
    }

    /// <summary>현재 씬과 Editor 상태가 Sector4 기타 Target 설정 조건에 맞는지 확인한다.</summary>
    private static void EnsureEditModeAndScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Sector4 기타 Target 설정·검증은 Play 모드가 아닌 Edit Mode에서 실행해야 합니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException($"Sector4 기타 Target 설정은 CoreSandbox 씬에서만 실행할 수 있습니다: {ScenePath}");
        }
    }

    /// <summary>세 Target의 Room·행동·Presenter 참조가 29번 명세를 만족하는지 검사한다.</summary>
    public static bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();
        Room controlRoom = FindRoomOrNull(ControlRoomId);
        Room utilityRoom = FindRoomOrNull(UtilityRoomId);
        MessagePanel messagePanel = UnityEngine.Object.FindFirstObjectByType<MessagePanel>(FindObjectsInactive.Include);
        PlayerRoomTracker playerRoomTracker = UnityEngine.Object.FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        TargetMarkerPool markerPool = UnityEngine.Object.FindFirstObjectByType<TargetMarkerPool>(FindObjectsInactive.Include);
        StealthHideSystem stealthHideSystem = UnityEngine.Object.FindFirstObjectByType<StealthHideSystem>(FindObjectsInactive.Include);
        Camera worldCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);

        Transform controlRoot = controlRoom != null ? controlRoom.transform.Find(ControlTargetRootName) : null;
        Transform utilityRoot = utilityRoom != null ? utilityRoom.transform.Find(UtilityTargetRootName) : null;
        Target consoleTarget = FindTarget(controlRoot, ControlConsoleTargetName);
        Target switchTarget = FindTarget(utilityRoot, UtilitySwitchTargetName);
        Target cabinetTarget = FindTarget(utilityRoot, ElectricalCabinetTargetName);

        Check(errors, controlRoom != null, $"RoomId '{ControlRoomId}'를 찾지 못했습니다.");
        Check(errors, utilityRoom != null, $"RoomId '{UtilityRoomId}'를 찾지 못했습니다.");
        Check(errors, messagePanel != null, "MessagePanel을 찾지 못했습니다.");
        Check(errors, playerRoomTracker != null, "PlayerRoomTracker를 찾지 못했습니다.");
        Check(errors, markerPool != null, "TargetMarkerPool을 찾지 못했습니다.");
        Check(errors, stealthHideSystem != null, "StealthHideSystem을 찾지 못했습니다.");
        Check(errors, worldCamera != null, "Sector4 기타 Target이 사용할 Camera를 찾지 못했습니다.");
        Check(errors, controlRoot != null, $"'{ControlTargetRootName}' 루트를 찾지 못했습니다.");
        Check(errors, utilityRoot != null, $"'{UtilityTargetRootName}' 루트를 찾지 못했습니다.");
        Check(errors, CountTargets(controlRoot, ControlConsoleTargetName) == 1,
            $"{ControlConsoleTargetName} Target이 정확히 하나가 아닙니다.");
        Check(errors, CountTargets(utilityRoot, UtilitySwitchTargetName) == 1,
            $"{UtilitySwitchTargetName} Target이 정확히 하나가 아닙니다.");
        Check(errors, CountTargets(utilityRoot, ElectricalCabinetTargetName) == 1,
            $"{ElectricalCabinetTargetName} Target이 정확히 하나가 아닙니다.");

        CheckHideTarget(errors, consoleTarget, controlRoom, stealthHideSystem, playerRoomTracker,
            markerPool, worldCamera, ControlConsoleTargetName);
        CheckHideTarget(errors, switchTarget, utilityRoom, stealthHideSystem, playerRoomTracker,
            markerPool, worldCamera, UtilitySwitchTargetName);
        CheckMessageTarget(errors, cabinetTarget, utilityRoom, messagePanel, playerRoomTracker,
            markerPool, worldCamera, ElectricalCabinetTargetName);

        report = errors.Count == 0
            ? "Sector4 ControlRoom·UtilityRoom 기타 Hide/Message Target 구성이 유효합니다."
            : "Sector4 기타 Target 검증 실패:\n- " + string.Join("\n- ", errors);
        return errors.Count == 0;
    }

    /// <summary>실제 소품을 우선 사용하고 없을 때만 Room 기준 논리 Anchor를 생성한다.</summary>
    private static Transform ResolvePropOrAnchor(Room room, string targetName, string anchorName, Vector3 fallbackOffset)
    {
        Transform prop = room.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate != null &&
                                         candidate.name == targetName &&
                                         candidate.GetComponent<Target>() == null);
        if (prop != null)
        {
            return prop;
        }

        Transform anchor = GetOrCreateChild(room.transform, anchorName);
        anchor.position = room.GraphPosition + fallbackOffset;
        anchor.rotation = room.RoomCamera != null ? room.RoomCamera.transform.rotation : Quaternion.identity;
        EditorUtility.SetDirty(anchor.gameObject);
        GameLog.LogWarning(
            $"'{room.RoomId}'에서 '{targetName}' 소품을 찾지 못해 '{anchorName}' 논리 Anchor를 만들었습니다.", room);
        return anchor;
    }

    /// <summary>전기 캐비닛에 일회성 생존자 메시지 행동을 연결한다.</summary>
    private static Target ConfigureMessageTarget(
        Room ownerRoom,
        Transform targetRoot,
        string targetName,
        Transform anchor,
        string message,
        MessagePanel messagePanel,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Transform targetTransform = GetOrCreateChild(targetRoot, targetName);
        Target target = ConfigureTargetShell(ownerRoom, targetTransform, anchor, 1.1f, 2f,
            playerRoomTracker, markerPool, worldCamera);
        MessageTarget messageTarget = GetOrAdd<MessageTarget>(targetTransform.gameObject);
        SetObject(messageTarget, "messagePanel", messagePanel);
        SetString(messageTarget, "message", message);
        SetString(messageTarget, "completionId", targetName);
        // 29번 메시지는 별도 Story Flag가 없으므로 기존 완료 ID만 사용한다.
        SetObject(messageTarget, "gameSession", null);
        SetString(messageTarget, "completionFlagId", string.Empty);
        SetObject(target, "actionComponent", messageTarget);
        target.SetSelectable(true);
        EditorUtility.SetDirty(targetTransform.gameObject);
        return target;
    }

    /// <summary>지정된 소품에 기존 StealthHideSystem으로 진입하는 행동을 연결한다.</summary>
    private static Target ConfigureHideTarget(
        Room ownerRoom,
        Transform targetRoot,
        string targetName,
        Transform anchor,
        StealthHideSystem stealthHideSystem,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera,
        float height,
        float approachDistance)
    {
        Transform targetTransform = GetOrCreateChild(targetRoot, targetName);
        Target target = ConfigureTargetShell(ownerRoom, targetTransform, anchor, height, approachDistance,
            playerRoomTracker, markerPool, worldCamera);
        StealthHideTarget hideTarget = GetOrAdd<StealthHideTarget>(targetTransform.gameObject);
        SetObject(hideTarget, "stealthHideSystem", stealthHideSystem);
        SetObject(target, "actionComponent", hideTarget);
        target.SetSelectable(true);
        EditorUtility.SetDirty(targetTransform.gameObject);
        return target;
    }

    /// <summary>Target의 이동 위치·Room·클릭 영역·Presenter를 공통 규칙으로 설정한다.</summary>
    private static Target ConfigureTargetShell(
        Room ownerRoom,
        Transform targetTransform,
        Transform anchor,
        float height,
        float approachDistance,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Vector3 targetPosition = anchor.position + Vector3.up * height;
        targetTransform.position = targetPosition;

        // 실제 소품의 Collider와 분리된 Trigger를 사용해 클릭 영역이 물리 이동을 막지 않도록 한다.
        BoxCollider clickArea = GetOrAdd<BoxCollider>(targetTransform.gameObject);
        clickArea.isTrigger = true;
        clickArea.center = Vector3.zero;
        clickArea.size = new Vector3(1.8f, 1.8f, 1.8f);

        // 소품 정면을 우선 사용하고 NavMesh가 막히면 주변 후보 중 가장 가까운 지점을 선택한다.
        Vector3 requestedApproach = anchor.position + anchor.forward * approachDistance;
        Transform approachPoint = GetOrCreateChild(targetTransform, "ApproachPoint");
        approachPoint.position = SampleNavMesh(requestedApproach, anchor.position + Vector3.forward * approachDistance);
        Vector3 facingDirection = targetPosition - approachPoint.position;
        approachPoint.rotation = facingDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(facingDirection, Vector3.up)
            : Quaternion.identity;

        Transform uiAnchor = GetOrCreateChild(targetTransform, "UIAnchor");
        uiAnchor.position = targetPosition + Vector3.up * 0.6f;

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

    /// <summary>요청 위치와 주변 후보 중 NavMesh에 가장 가까운 위치를 반환한다.</summary>
    private static Vector3 SampleNavMesh(Vector3 requested, Vector3 fallback)
    {
        if (NavMesh.SamplePosition(requested, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        bool foundCandidate = false;
        float bestDistance = float.MaxValue;
        Vector3 bestPosition = fallback;
        Vector3[] directions =
        {
            Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
            (Vector3.forward + Vector3.right).normalized,
            (Vector3.forward + Vector3.left).normalized,
            (Vector3.back + Vector3.right).normalized,
            (Vector3.back + Vector3.left).normalized
        };

        for (int radius = 2; radius <= 12; radius += 2)
        {
            foreach (Vector3 direction in directions)
            {
                Vector3 candidate = requested + direction * radius;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit ringHit, 2f, NavMesh.AllAreas))
                {
                    continue;
                }

                float distance = Vector3.Distance(requested, ringHit.position);
                if (distance < bestDistance)
                {
                    foundCandidate = true;
                    bestDistance = distance;
                    bestPosition = ringHit.position;
                }
            }
        }

        return foundCandidate ? bestPosition : requested;
    }

    /// <summary>메시지 Target의 공용 참조와 29번 명세 문구를 검사한다.</summary>
    private static void CheckMessageTarget(
        List<string> errors,
        Target target,
        Room ownerRoom,
        MessagePanel messagePanel,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera,
        string targetName)
    {
        CheckCommonTarget(errors, target, ownerRoom, playerRoomTracker, markerPool, worldCamera, targetName);
        Check(errors, target == null || target.Action is MessageTarget,
            $"{targetName}의 행동이 MessageTarget이 아닙니다.");
        if (target?.Action is not MessageTarget messageTarget)
        {
            return;
        }

        SerializedObject serialized = new(messageTarget);
        Check(errors, serialized.FindProperty("messagePanel")?.objectReferenceValue == messagePanel,
            $"{targetName}의 MessagePanel 참조가 잘못됐습니다.");
        Check(errors, serialized.FindProperty("message")?.stringValue == SurvivorMessage,
            $"{targetName}의 메시지가 계획 표기와 다릅니다.");
        Check(errors, serialized.FindProperty("completionId")?.stringValue == targetName,
            $"{targetName}의 completionId가 잘못됐습니다.");
        Check(errors, serialized.FindProperty("gameSession")?.objectReferenceValue == null,
            $"{targetName}에 불필요한 GameSession 참조가 연결됐습니다.");
        Check(errors, string.IsNullOrWhiteSpace(serialized.FindProperty("completionFlagId")?.stringValue),
            $"{targetName}에 불필요한 Story Flag가 연결됐습니다.");
    }

    /// <summary>Hide Target이 기존 StealthHideSystem과 공용 Target 설정을 사용하는지 검사한다.</summary>
    private static void CheckHideTarget(
        List<string> errors,
        Target target,
        Room ownerRoom,
        StealthHideSystem stealthHideSystem,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera,
        string targetName)
    {
        CheckCommonTarget(errors, target, ownerRoom, playerRoomTracker, markerPool, worldCamera, targetName);
        Check(errors, target == null || target.Action is StealthHideTarget,
            $"{targetName}의 행동이 StealthHideTarget이 아닙니다.");
        if (target?.Action is not StealthHideTarget hideTarget)
        {
            return;
        }

        SerializedObject serialized = new(hideTarget);
        Check(errors, serialized.FindProperty("stealthHideSystem")?.objectReferenceValue == stealthHideSystem,
            $"{targetName}의 StealthHideSystem 참조가 잘못됐습니다.");
    }

    /// <summary>세 Target에 공통인 Room·접근 지점·Presenter 참조를 검사한다.</summary>
    private static void CheckCommonTarget(
        List<string> errors,
        Target target,
        Room ownerRoom,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera,
        string targetName)
    {
        Check(errors, target != null, $"{targetName} Target을 찾지 못했습니다.");
        if (target == null)
        {
            return;
        }

        Check(errors, target.OwnerRoom == ownerRoom, $"{targetName}의 OwnerRoom이 {ownerRoom.RoomId}가 아닙니다.");
        Check(errors, target.TryValidateConfiguration(out string configurationError),
            $"{targetName}: {configurationError}");
        TargetPresenter presenter = target.GetComponent<TargetPresenter>();
        Check(errors, presenter != null, $"{targetName}에 TargetPresenter가 없습니다.");
        Check(errors, target.GetComponent<BoxCollider>() != null, $"{targetName}에 클릭용 BoxCollider가 없습니다.");
        if (presenter == null)
        {
            return;
        }

        SerializedObject serialized = new(presenter);
        Check(errors, serialized.FindProperty("target")?.objectReferenceValue == target,
            $"{targetName} Presenter의 Target 참조가 잘못됐습니다.");
        Check(errors, serialized.FindProperty("playerRoomTracker")?.objectReferenceValue == playerRoomTracker,
            $"{targetName} Presenter의 PlayerRoomTracker 참조가 잘못됐습니다.");
        Check(errors, serialized.FindProperty("markerPool")?.objectReferenceValue == markerPool,
            $"{targetName} Presenter의 MarkerPool 참조가 잘못됐습니다.");
        Check(errors, serialized.FindProperty("worldCamera")?.objectReferenceValue == worldCamera,
            $"{targetName} Presenter의 Camera 참조가 잘못됐습니다.");
    }

    /// <summary>RoomId로 필요한 Room을 찾는다.</summary>
    private static Room FindRequiredRoom(string roomId)
    {
        return FindRoomOrNull(roomId)
               ?? throw new InvalidOperationException($"RoomId '{roomId}'를 찾지 못했습니다.");
    }

    /// <summary>RoomId로 Room을 찾되 없으면 null을 반환한다.</summary>
    private static Room FindRoomOrNull(string roomId)
    {
        return UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && string.Equals(room.RoomId, roomId, StringComparison.Ordinal));
    }

    /// <summary>씬에서 필요한 컴포넌트를 찾아 없으면 설정을 중단한다.</summary>
    private static T FindRequired<T>(string label) where T : Component
    {
        return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include)
               ?? throw new InvalidOperationException($"{label} 컴포넌트를 찾지 못했습니다.");
    }

    /// <summary>Target 루트 아래 지정된 이름의 Target을 찾는다.</summary>
    private static Target FindTarget(Transform root, string targetName)
    {
        return root == null
            ? null
            : root.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => target != null && target.name == targetName);
    }

    /// <summary>Target 루트 아래 지정된 이름의 Target 개수를 센다.</summary>
    private static int CountTargets(Transform root, string targetName)
    {
        return root == null
            ? 0
            : root.GetComponentsInChildren<Target>(true)
                .Count(target => target != null && target.name == targetName);
    }

    /// <summary>부모 아래 지정된 이름의 자식을 하나만 만들고 반환한다.</summary>
    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new(childName);
        childObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(childObject, "Create Sector4 misc target child");
        return childObject.transform;
    }

    /// <summary>같은 GameObject에 지정 컴포넌트가 없을 때만 추가한다.</summary>
    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    /// <summary>SerializedObject의 Unity 참조 필드를 명시적으로 설정한다.</summary>
    private static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>SerializedObject의 문자열 필드를 명시적으로 설정한다.</summary>
    private static void SetString(UnityEngine.Object target, string propertyName, string value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>SerializedObject의 bool 필드를 명시적으로 설정한다.</summary>
    private static void SetBool(UnityEngine.Object target, string propertyName, bool value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>검사 조건이 false일 때 오류를 누적한다.</summary>
    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition && !string.IsNullOrWhiteSpace(message))
        {
            errors.Add(message);
        }
    }
}
#endif
