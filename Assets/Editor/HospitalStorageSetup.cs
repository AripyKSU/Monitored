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
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 Sector 2 Storage에 조사 Target과 Fuse 획득 Target을 구성한다.
/// Storage Room 하위의 지정된 선반만 사용하며 다른 Room의 Target이나 공유 오브젝트는 순회하지 않는다.
/// </summary>
public static class HospitalStorageSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string SetupMenuPath = "Haunted House/Setup/Hospital Sector2 Storage";
    private const string ValidateMenuPath = "Haunted House/Validate/Hospital Sector2 Storage";
    private const string FuseAssetPath = "Assets/Data/Items/Fuse.asset";
    private const string EmptyTargetName = "S2_Storage_TargetA";
    private const string FuseTargetName = "S2_Storage_TargetB_Fuse";
    private const string FuseVisualName = "FuseVisual";

    /// <summary>
    /// 현재 CoreSandbox의 S2_Storage에 Storage 조사와 Fuse 획득 상호작용을 생성하거나 갱신한다.
    /// 이 메서드는 Play 모드가 아닌 Unity Editor에서 메뉴로 실행해야 한다.
    /// </summary>
    [MenuItem(SetupMenuPath)]
    public static void Configure()
    {
        EnsureScene();

        Room storage = FindRoom("S2_Storage");
        MessagePanel messagePanel = FindRequired<MessagePanel>("MessagePanel");
        PlayerInventory inventory = FindRequired<PlayerInventory>("PlayerInventory");
        PlayerRoomTracker playerRoomTracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        TargetMarkerPool markerPool = FindRequired<TargetMarkerPool>("TargetMarkerPool");
        Camera worldCamera = Camera.main != null
            ? Camera.main
            : UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (worldCamera == null)
        {
            throw new InvalidOperationException("Storage Target이 사용할 Camera를 찾지 못했습니다.");
        }

        ItemDefinition fuse = FindFuse();
        Transform westShelfA = FindProp(storage, "Storage_Shelf_West_1");
        Transform westShelfB = FindProp(storage, "Storage_Shelf_West_2");
        Transform targetRoot = GetOrCreateChild(storage.transform, "Sector2StorageTargets");

        // 선반의 동쪽 면에 Target을 두고, 플레이어는 선반을 돌아 들어갈 수 있는 NavMesh 위치로 이동시킨다.
        Target emptyTarget = ConfigureMessageTarget(
            storage,
            targetRoot,
            EmptyTargetName,
            westShelfA.position + new Vector3(4.3f, 5.2f, 0f),
            westShelfA.position + new Vector3(10f, 0f, 0f),
            "아무것도 발견되지 않았다.",
            "S2_Storage_TargetA",
            messagePanel,
            playerRoomTracker,
            markerPool,
            worldCamera);

        ItemPickupTarget fusePickup = ConfigureFuseTarget(
            storage,
            targetRoot,
            westShelfB,
            fuse,
            inventory,
            messagePanel,
            playerRoomTracker,
            markerPool,
            worldCamera);

        // Fuse는 발전기 설치 시 소비되는 아이템이므로 Storage에서 획득하는 순간에는 별도 flag를 기록하지 않는다.
        // 체크포인트 복원기는 모든 ICompletedState를 먼저 초기화하므로, 아직 Save3가 활성화되기 전에는 Fuse가 다시 나타난다.
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameLog.Log(
            $"Hospital Sector 2 Storage Target 구성을 완료했습니다: {emptyTarget.name}, {fusePickup.name}.");
    }

    /// <summary>
    /// S2_Storage의 두 Target이 기존 상호작용 계약과 Fuse 아이템을 올바르게 참조하는지 검사한다.
    /// 검증은 씬을 변경하지 않고 누락된 설정만 오류로 보고한다.
    /// </summary>
    [MenuItem(ValidateMenuPath)]
    public static void Validate()
    {
        EnsureScene();

        List<string> errors = new();
        Room storage = FindRoomOrNull("S2_Storage");
        Check(errors, storage != null, "S2_Storage Room이 없습니다.");

        Target emptyTarget = FindTarget(storage, EmptyTargetName);
        Target fuseTarget = FindTarget(storage, FuseTargetName);
        CheckTarget(errors, emptyTarget, storage, EmptyTargetName);
        CheckTarget(errors, fuseTarget, storage, FuseTargetName);

        if (emptyTarget != null)
        {
            Check(errors, emptyTarget.Action is MessageTarget,
                $"{EmptyTargetName}의 행동이 MessageTarget이 아닙니다.");
            CheckMessage(errors, emptyTarget, "아무것도 발견되지 않았다.", EmptyTargetName);
        }

        if (fuseTarget != null)
        {
            Check(errors, fuseTarget.Action is ItemPickupTarget,
                $"{FuseTargetName}의 행동이 ItemPickupTarget이 아닙니다.");
            CheckFusePickup(errors, fuseTarget);
        }

        Check(errors, storage != null && FindPropOrNull(storage, "Storage_Shelf_West_1") != null,
            "Storage_Shelf_West_1을 찾지 못했습니다.");
        Check(errors, storage != null && FindPropOrNull(storage, "Storage_Shelf_West_2") != null,
            "Storage_Shelf_West_2를 찾지 못했습니다.");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Hospital Sector 2 Storage 검증 실패:\n- " + string.Join("\n- ", errors));
        }

        GameLog.Log("Hospital Sector 2 Storage 구성이 유효합니다.");
    }

    /// <summary>현재 씬과 Editor 실행 상태가 Storage 설정 조건에 맞는지 확인한다.</summary>
    private static void EnsureScene()
    {
        if (EditorApplication.isPlaying)
        {
            throw new InvalidOperationException("Hospital Sector 2 Storage 설정은 Play 모드에서 실행할 수 없습니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException("Hospital Sector 2 Storage 설정은 CoreSandbox 씬에서만 실행할 수 있습니다.");
        }
    }

    /// <summary>씬에서 필요한 Component를 찾아 없으면 설정을 중단한다.</summary>
    private static T FindRequired<T>(string label) where T : Component
    {
        T component = UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        return component != null
            ? component
            : throw new InvalidOperationException($"{label} 컴포넌트를 찾지 못했습니다.");
    }

    /// <summary>RoomId로 Storage Room을 찾는다.</summary>
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

    /// <summary>ItemId와 고정 에셋 경로가 일치하는 Fuse 정의를 찾는다.</summary>
    private static ItemDefinition FindFuse()
    {
        ItemDefinition fuse = AssetDatabase.LoadAssetAtPath<ItemDefinition>(FuseAssetPath);
        if (fuse == null || !string.Equals(fuse.ItemId, "fuse", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Fuse 아이템 에셋이 없거나 ItemId가 잘못됐습니다: {FuseAssetPath}");
        }

        return fuse;
    }

    /// <summary>Storage Room의 지정된 기존 가구를 배치 기준으로 찾는다.</summary>
    private static Transform FindProp(Room room, string propName)
    {
        return FindPropOrNull(room, propName)
            ?? throw new InvalidOperationException($"Room '{room.RoomId}'에서 가구 '{propName}'을 찾지 못했습니다.");
    }

    /// <summary>Storage Room의 자식 계층에서 지정된 가구를 찾되 없으면 null을 반환한다.</summary>
    private static Transform FindPropOrNull(Room room, string propName)
    {
        return room == null
            ? null
            : room.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == propName);
    }

    /// <summary>기존 Storage 대상 Root 아래에서 Target을 이름으로 확인한다.</summary>
    private static Target FindTarget(Room room, string targetName)
    {
        return room == null
            ? null
            : room.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => target.name == targetName);
    }

    /// <summary>메시지 조사 Target을 생성하고 공용 MessagePanel과 Target 표현 참조를 연결한다.</summary>
    private static Target ConfigureMessageTarget(
        Room ownerRoom,
        Transform root,
        string targetName,
        Vector3 targetPosition,
        Vector3 approachPosition,
        string message,
        string completionId,
        MessagePanel messagePanel,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Target target = ConfigureTargetShell(
            ownerRoom,
            GetOrCreateChild(root, targetName),
            targetPosition,
            approachPosition,
            playerRoomTracker,
            markerPool,
            worldCamera);
        MessageTarget messageTarget = GetOrAdd<MessageTarget>(target.gameObject);
        SetObject(messageTarget, "messagePanel", messagePanel);
        SetString(messageTarget, "message", message);
        SetString(messageTarget, "completionId", completionId);
        SetObject(target, "actionComponent", messageTarget);
        EditorUtility.SetDirty(target.gameObject);
        return target;
    }

    /// <summary>서쪽 선반 앞에 Fuse 획득 Target과 간단한 월드 Fuse 표현을 구성한다.</summary>
    private static ItemPickupTarget ConfigureFuseTarget(
        Room ownerRoom,
        Transform root,
        Transform shelf,
        ItemDefinition fuse,
        PlayerInventory inventory,
        MessagePanel messagePanel,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Transform targetTransform = GetOrCreateChild(root, FuseTargetName);
        Vector3 targetPosition = shelf.position + new Vector3(3.8f, 5.2f, 0f);
        Vector3 approachPosition = shelf.position + new Vector3(10f, 0f, 0f);
        Target target = ConfigureTargetShell(
            ownerRoom,
            targetTransform,
            targetPosition,
            approachPosition,
            playerRoomTracker,
            markerPool,
            worldCamera);

        ItemPickupTarget pickup = GetOrAdd<ItemPickupTarget>(targetTransform.gameObject);
        SetObject(pickup, "item", fuse);
        SetObject(pickup, "inventory", inventory);
        SetObject(pickup, "messagePanel", messagePanel);
        SetObject(pickup, "worldObject", GetOrCreateFuseVisual(targetTransform));
        SetString(pickup, "pickupMessage", "퓨즈를 획득했습니다.");
        SetString(pickup, "completionId", FuseTargetName);
        SetObject(target, "actionComponent", pickup);
        target.SetSelectable(true);
        EditorUtility.SetDirty(targetTransform.gameObject);
        return pickup;
    }

    /// <summary>Target의 접근 지점, OwnerRoom, 클릭 영역, 마커 표현을 공통 규칙으로 설정한다.</summary>
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

        // Target의 클릭 영역은 물리 이동을 막지 않도록 Trigger로 만들고, Marker 기준점은 대상 위에 둔다.
        BoxCollider clickArea = GetOrAdd<BoxCollider>(targetTransform.gameObject);
        clickArea.isTrigger = true;
        clickArea.center = Vector3.up;
        clickArea.size = new Vector3(2f, 2f, 2f);

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

    /// <summary>Fuse가 획득되면 꺼지고 복원 시 다시 켜질 수 있는 간단한 월드 표현을 만든다.</summary>
    private static GameObject GetOrCreateFuseVisual(Transform targetTransform)
    {
        Transform existing = targetTransform.Find(FuseVisualName);
        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            return existing.gameObject;
        }

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = FuseVisualName;
        Undo.RegisterCreatedObjectUndo(visual, "Create Storage Fuse visual");
        visual.transform.SetParent(targetTransform, false);
        visual.transform.localPosition = new Vector3(0f, -0.35f, 0f);
        visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        visual.transform.localScale = new Vector3(0.12f, 0.38f, 0.12f);

        // 월드 표현이 추가 클릭 영역이나 플레이어 충돌을 만들지 않도록 기본 Collider를 제거한다.
        Collider collider = visual.GetComponent<Collider>();
        if (collider != null)
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }

        EditorUtility.SetDirty(visual);
        return visual;
    }

    /// <summary>요청 위치 주변의 NavMesh를 사용해 플레이어 접근 위치를 결정한다.</summary>
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

    /// <summary>부모 아래에 지정된 이름의 자식을 하나만 만들고 반환한다.</summary>
    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new(childName);
        childObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(childObject, "Create Hospital Sector2 Storage child");
        return childObject.transform;
    }

    /// <summary>같은 GameObject에 컴포넌트가 없을 때만 추가한다.</summary>
    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        // UnityEngine.Object는 네이티브 객체가 제거된 뒤에도 관리 객체가 남을 수 있으므로
        // C# null 병합 연산자 대신 Unity의 overloaded 비교를 사용해 실제 존재 여부를 검사한다.
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    /// <summary>컴포넌트의 UnityEngine.Object 필드를 SerializedProperty로 설정한다.</summary>
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

    /// <summary>컴포넌트의 문자열 필드를 SerializedProperty로 설정한다.</summary>
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

    /// <summary>컴포넌트의 bool 필드를 SerializedProperty로 설정한다.</summary>
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

    /// <summary>Target의 공통 설정과 소유 Room을 검사한다.</summary>
    private static void CheckTarget(List<string> errors, Target target, Room ownerRoom, string targetName)
    {
        Check(errors, target != null, $"{targetName}을 찾지 못했습니다.");
        if (target == null)
        {
            return;
        }

        Check(errors, target.OwnerRoom == ownerRoom, $"{targetName}의 OwnerRoom이 S2_Storage가 아닙니다.");
        if (!target.TryValidateConfiguration(out string configurationError))
        {
            errors.Add($"{targetName}: {configurationError}");
        }

        Transform approachPoint = target.ApproachPoint;
        Check(errors, approachPoint != null, $"{targetName}의 ApproachPoint가 비어 있습니다.");
        Check(errors, target.GetComponent<TargetPresenter>() != null,
            $"{targetName}에 TargetPresenter가 없습니다.");
        Check(errors, target.GetComponent<BoxCollider>() != null,
            $"{targetName}에 클릭용 BoxCollider가 없습니다.");
    }

    /// <summary>조사 Target의 메시지가 계획 문구와 일치하는지 검사한다.</summary>
    private static void CheckMessage(List<string> errors, Target target, string expectedMessage, string targetName)
    {
        if (target == null || target.Action is not MessageTarget messageTarget)
        {
            return;
        }

        string actualMessage = ReadString(messageTarget, "message");
        Check(errors, string.Equals(actualMessage, expectedMessage, StringComparison.Ordinal),
            $"{targetName}의 조사 메시지가 계획과 다릅니다.");
    }

    /// <summary>Fuse Target의 아이템, 월드 표현, 획득 메시지를 검사한다.</summary>
    private static void CheckFusePickup(List<string> errors, Target target)
    {
        if (target == null || target.Action is not ItemPickupTarget pickup)
        {
            return;
        }

        SerializedObject serialized = new(pickup);
        SerializedProperty item = serialized.FindProperty("item");
        SerializedProperty worldObject = serialized.FindProperty("worldObject");
        Check(errors, item != null && item.objectReferenceValue is ItemDefinition definition &&
                      string.Equals(definition.ItemId, "fuse", StringComparison.Ordinal),
            $"{FuseTargetName}의 Item이 Fuse.asset을 참조하지 않습니다.");
        Check(errors, worldObject != null && worldObject.objectReferenceValue != null,
            $"{FuseTargetName}의 worldObject가 비어 있습니다.");
        Check(errors, target.transform.Find(FuseVisualName) != null,
            $"{FuseTargetName}의 FuseVisual이 없습니다.");
        Check(errors, string.Equals(ReadString(pickup, "pickupMessage"), "퓨즈를 획득했습니다.", StringComparison.Ordinal),
            $"{FuseTargetName}의 획득 메시지가 계획과 다릅니다.");
    }

    /// <summary>SerializedObject에서 문자열 필드를 읽고 누락 시 빈 문자열을 반환한다.</summary>
    private static string ReadString(UnityEngine.Object target, string propertyName)
    {
        if (target == null)
        {
            return string.Empty;
        }

        SerializedObject serialized = new(target);
        return serialized.FindProperty(propertyName)?.stringValue ?? string.Empty;
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
