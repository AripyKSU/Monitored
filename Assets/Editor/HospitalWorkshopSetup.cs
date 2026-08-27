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
/// CoreSandbox의 Sector4 Workshop에 일기 메시지 Target과 은신 Target을 구성하는 Editor 도구다.
/// 기존 MessageTarget·StealthHideTarget·TargetPresenter를 재사용하며, 실행할 때마다 같은 오브젝트를 갱신한다.
/// </summary>
public static class HospitalWorkshopSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string WorkshopRoomId = "S4_Workshop";
    private const string TargetRootName = "Sector4WorkshopTargets";
    private const string RestraintBedTargetName = "Workshop_RestraintBed";
    private const string MetalCaseTargetName = "Workshop_MetalCase";
    private const string RestraintBedAnchorName = "Workshop_RestraintBedAnchor";
    private const string MetalCaseAnchorName = "Workshop_MetalCaseAnchor";
    private const string RestraintBedMessage = "[살인마 일기 3번]";

    /// <summary>Workshop의 두 Target을 멱등적으로 생성하거나 기존 설정을 갱신한다.</summary>
    [MenuItem("Haunted House/Setup/Hospital Sector4 Workshop")]
    public static void Configure()
    {
        EnsureEditModeAndScene();

        Room workshop = FindRequiredRoom();
        MessagePanel messagePanel = FindRequired<MessagePanel>("MessagePanel");
        GameSession gameSession = FindRequired<GameSession>("GameSession");
        PlayerRoomTracker playerRoomTracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        TargetMarkerPool markerPool = FindRequired<TargetMarkerPool>("TargetMarkerPool");
        StealthHideSystem stealthHideSystem = FindRequired<StealthHideSystem>("StealthHideSystem");
        Camera worldCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (worldCamera == null)
        {
            throw new InvalidOperationException("Workshop Target이 사용할 Camera를 찾지 못했습니다.");
        }

        Transform targetRoot = GetOrCreateChild(workshop.transform, TargetRootName);
        Transform restraintBedAnchor = ResolvePropOrAnchor(workshop, RestraintBedTargetName, RestraintBedAnchorName, Vector3.forward * 2f);
        Transform metalCaseAnchor = ResolvePropOrAnchor(workshop, MetalCaseTargetName, MetalCaseAnchorName, Vector3.forward * 3f);

        Target restraintBed = ConfigureMessageTarget(
            workshop,
            targetRoot,
            RestraintBedTargetName,
            restraintBedAnchor,
            RestraintBedMessage,
            messagePanel,
            gameSession,
            playerRoomTracker,
            markerPool,
            worldCamera);

        Target metalCase = ConfigureHideTarget(
            workshop,
            targetRoot,
            MetalCaseTargetName,
            metalCaseAnchor,
            stealthHideSystem,
            playerRoomTracker,
            markerPool,
            worldCamera);

        EditorUtility.SetDirty(workshop.gameObject);
        EditorUtility.SetDirty(targetRoot.gameObject);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!ValidateConfiguration(out string report))
        {
            throw new InvalidOperationException(report);
        }

        GameLog.Log($"Sector4 Workshop Target 구성을 완료했습니다: {restraintBed.name}, {metalCase.name}.");
    }

    /// <summary>Workshop Target과 메시지·은신·Room 참조를 변경 없이 검사한다.</summary>
    [MenuItem("Haunted House/Validate/Hospital Sector4 Workshop")]
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

    /// <summary>설정 도구가 실행될 수 있는 씬과 Editor 상태인지 확인한다.</summary>
    private static void EnsureEditModeAndScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Workshop 설정·검증은 Play 모드가 아닌 Edit Mode에서 실행해야 합니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException($"Workshop 설정은 CoreSandbox 씬에서만 실행할 수 있습니다: {ScenePath}");
        }
    }

    /// <summary>현재 씬의 Workshop 구성이 26번 계획서 계약을 만족하는지 검사한다.</summary>
    public static bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();
        Room workshop = FindRoomOrNull(WorkshopRoomId);
        MessagePanel messagePanel = UnityEngine.Object.FindFirstObjectByType<MessagePanel>(FindObjectsInactive.Include);
        GameSession gameSession = UnityEngine.Object.FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);
        PlayerRoomTracker playerRoomTracker = UnityEngine.Object.FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        TargetMarkerPool markerPool = UnityEngine.Object.FindFirstObjectByType<TargetMarkerPool>(FindObjectsInactive.Include);
        StealthHideSystem stealthHideSystem = UnityEngine.Object.FindFirstObjectByType<StealthHideSystem>(FindObjectsInactive.Include);
        Camera worldCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        Transform targetRoot = workshop != null ? workshop.transform.Find(TargetRootName) : null;
        Target restraintBed = FindTarget(targetRoot, RestraintBedTargetName);
        Target metalCase = FindTarget(targetRoot, MetalCaseTargetName);

        Check(errors, workshop != null, $"RoomId '{WorkshopRoomId}'를 찾지 못했습니다.");
        Check(errors, messagePanel != null, "MessagePanel을 찾지 못했습니다.");
        Check(errors, gameSession != null, "GameSession을 찾지 못했습니다.");
        Check(errors, playerRoomTracker != null, "PlayerRoomTracker를 찾지 못했습니다.");
        Check(errors, markerPool != null, "TargetMarkerPool을 찾지 못했습니다.");
        Check(errors, stealthHideSystem != null, "StealthHideSystem을 찾지 못했습니다.");
        Check(errors, worldCamera != null, "Workshop Target이 사용할 Camera를 찾지 못했습니다.");
        Check(errors, targetRoot != null, $"'{TargetRootName}' 루트를 찾지 못했습니다.");
        Check(errors, CountTargets(targetRoot, RestraintBedTargetName) == 1,
            $"{RestraintBedTargetName} Target이 정확히 하나가 아닙니다.");
        Check(errors, CountTargets(targetRoot, MetalCaseTargetName) == 1,
            $"{MetalCaseTargetName} Target이 정확히 하나가 아닙니다.");

        CheckMessageTarget(errors, restraintBed, workshop, messagePanel, gameSession, playerRoomTracker,
            markerPool, worldCamera, RestraintBedMessage, RestraintBedTargetName);
        CheckHideTarget(errors, metalCase, workshop, stealthHideSystem, playerRoomTracker,
            markerPool, worldCamera, MetalCaseTargetName);

        report = errors.Count == 0
            ? "Sector4 Workshop의 RestraintBed·MetalCase Target 구성이 유효합니다."
            : "Sector4 Workshop Target 검증 실패:\n- " + string.Join("\n- ", errors);
        return errors.Count == 0;
    }

    /// <summary>침대 소품을 우선 사용하고 없을 때만 명시적인 논리 Anchor를 만든다.</summary>
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
        GameLog.LogWarning($"'{room.RoomId}'에서 '{targetName}' 소품을 찾지 못해 '{anchorName}' 논리 Anchor를 만들었습니다.", room);
        return anchor;
    }

    /// <summary>침대에 메시지 행동과 KillerDiary3Read 완료 flag를 연결한다.</summary>
    private static Target ConfigureMessageTarget(
        Room ownerRoom,
        Transform targetRoot,
        string targetName,
        Transform anchor,
        string message,
        MessagePanel messagePanel,
        GameSession gameSession,
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
        SetObject(messageTarget, "gameSession", gameSession);
        SetString(messageTarget, "completionFlagId", StoryProgressFlags.KillerDiary3Read);
        SetObject(target, "actionComponent", messageTarget);
        target.SetSelectable(true);
        EditorUtility.SetDirty(targetTransform.gameObject);
        return target;
    }

    /// <summary>금속 케이스에 기존 은신 시스템으로 진입하는 행동을 연결한다.</summary>
    private static Target ConfigureHideTarget(
        Room ownerRoom,
        Transform targetRoot,
        string targetName,
        Transform anchor,
        StealthHideSystem stealthHideSystem,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Transform targetTransform = GetOrCreateChild(targetRoot, targetName);
        Target target = ConfigureTargetShell(ownerRoom, targetTransform, anchor, 0.8f, 3f,
            playerRoomTracker, markerPool, worldCamera);
        StealthHideTarget hideTarget = GetOrAdd<StealthHideTarget>(targetTransform.gameObject);
        SetObject(hideTarget, "stealthHideSystem", stealthHideSystem);
        SetObject(target, "actionComponent", hideTarget);
        target.SetSelectable(true);
        EditorUtility.SetDirty(targetTransform.gameObject);
        return target;
    }

    /// <summary>Target의 이동·소유 Room·Collider·Presenter를 공통 규칙으로 설정한다.</summary>
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

        // 클릭 영역은 실제 소품 MeshCollider와 분리한 Trigger로 만들어 물리 이동을 막지 않는다.
        BoxCollider clickArea = GetOrAdd<BoxCollider>(targetTransform.gameObject);
        clickArea.isTrigger = true;
        clickArea.center = Vector3.zero;
        clickArea.size = new Vector3(1.8f, 1.8f, 1.8f);

        // 소품의 정면을 우선 접근 방향으로 사용하고, NavMesh가 막힌 경우 주변 후보를 조사한다.
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

    /// <summary>요청 위치 주변의 같은 층 NavMesh에서 가장 가까운 접근 지점을 찾는다.</summary>
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

    /// <summary>메시지 Target의 참조·문구·완료 flag 설정을 검사한다.</summary>
    private static void CheckMessageTarget(
        List<string> errors,
        Target target,
        Room ownerRoom,
        MessagePanel messagePanel,
        GameSession gameSession,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera,
        string expectedMessage,
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
        Check(errors, serialized.FindProperty("gameSession")?.objectReferenceValue == gameSession,
            $"{targetName}의 GameSession 참조가 잘못됐습니다.");
        Check(errors, serialized.FindProperty("message")?.stringValue == expectedMessage,
            $"{targetName}의 메시지가 계획 문구와 다릅니다.");
        Check(errors, serialized.FindProperty("completionId")?.stringValue == targetName,
            $"{targetName}의 completionId가 잘못됐습니다.");
        Check(errors, serialized.FindProperty("completionFlagId")?.stringValue == StoryProgressFlags.KillerDiary3Read,
            $"{targetName}의 완료 Story Flag가 KillerDiary3Read가 아닙니다.");
    }

    /// <summary>은신 Target의 참조와 기존 StealthHideSystem 연결을 검사한다.</summary>
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

    /// <summary>두 Target이 공유하는 Room·접근·Presenter 참조를 검사한다.</summary>
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

        Check(errors, target.OwnerRoom == ownerRoom, $"{targetName}의 OwnerRoom이 {WorkshopRoomId}가 아닙니다.");
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

    /// <summary>RoomId로 Workshop Room을 찾아 없으면 예외를 발생시킨다.</summary>
    private static Room FindRequiredRoom()
    {
        return FindRoomOrNull(WorkshopRoomId)
               ?? throw new InvalidOperationException($"RoomId '{WorkshopRoomId}'를 찾지 못했습니다.");
    }

    /// <summary>RoomId로 Room을 찾아 없으면 null을 반환한다.</summary>
    private static Room FindRoomOrNull(string roomId)
    {
        return UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && string.Equals(room.RoomId, roomId, StringComparison.Ordinal));
    }

    /// <summary>씬에서 필수 컴포넌트를 찾아 없으면 설정을 중단한다.</summary>
    private static T FindRequired<T>(string label) where T : Component
    {
        return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include)
               ?? throw new InvalidOperationException($"{label} 컴포넌트를 찾지 못했습니다.");
    }

    /// <summary>루트 아래 지정된 이름의 Target을 찾는다.</summary>
    private static Target FindTarget(Transform root, string targetName)
    {
        return root == null
            ? null
            : root.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => target != null && target.name == targetName);
    }

    /// <summary>루트 아래 지정된 이름의 Target 개수를 센다.</summary>
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
        Undo.RegisterCreatedObjectUndo(childObject, "Create Workshop target child");
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
