#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 Sector3 DiningRoom에 힌트보드와 일기장 조사 Target을 구성하는 Editor 도구다.
/// 기존 MessageTarget·Target 표현 시스템을 재사용하고, FreezerHintRead flag는 MessageTarget의 선택적 완료 flag 기능으로 기록한다.
/// </summary>
public static class HospitalDiningRoomSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string DiningRoomId = "S3_DiningRoom";
    private const string TargetRootName = "Sector3DiningTargets";
    private const string FreezerHintTargetName = "Story_FreezerHintBoard";
    private const string DiaryTargetName = "Story_DiaryPage_Dining";
    private const string FreezerAnchorName = "DiningRoom_FreezerHintBoardAnchor";
    private const string DiaryAnchorName = "DiningRoom_DiaryPageAnchor";
    private const string FreezerMessage = "[식당 힌트보드]";
    private const string DiaryMessage = "[일기장 2번]";

    /// <summary>DiningRoom의 두 조사 Target을 멱등적으로 생성하거나 기존 설정을 갱신한다.</summary>
    [MenuItem("Haunted House/Setup/Hospital Sector3 DiningRoom")]
    public static void Configure()
    {
        EnsureScene();

        Room diningRoom = FindRequiredRoom(DiningRoomId);
        MessagePanel messagePanel = FindRequired<MessagePanel>("MessagePanel");
        GameSession gameSession = FindRequired<GameSession>("GameSession");
        PlayerRoomTracker playerRoomTracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        TargetMarkerPool markerPool = FindRequired<TargetMarkerPool>("TargetMarkerPool");
        Camera worldCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (worldCamera == null)
        {
            throw new InvalidOperationException("DiningRoom Target이 사용할 Camera를 찾지 못했습니다.");
        }

        Transform targetRoot = GetOrCreateChild(diningRoom.transform, TargetRootName);
        Transform freezerAnchor = ResolveAnchor(diningRoom, FreezerAnchorName, false);
        Transform diaryAnchor = ResolveAnchor(diningRoom, DiaryAnchorName, true);

        Target freezerHint = ConfigureMessageTarget(
            diningRoom,
            targetRoot,
            FreezerHintTargetName,
            freezerAnchor,
            FreezerMessage,
            FreezerHintTargetName,
            StoryProgressFlags.FreezerHintRead,
            messagePanel,
            gameSession,
            playerRoomTracker,
            markerPool,
            worldCamera);

        Target diary = ConfigureMessageTarget(
            diningRoom,
            targetRoot,
            DiaryTargetName,
            diaryAnchor,
            DiaryMessage,
            DiaryTargetName,
            string.Empty,
            messagePanel,
            gameSession,
            playerRoomTracker,
            markerPool,
            worldCamera);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!ValidateConfiguration(out string report))
        {
            throw new InvalidOperationException(report);
        }

        GameLog.Log($"Sector3 DiningRoom Target 구성을 완료했습니다: {freezerHint.name}, {diary.name}.");
    }

    /// <summary>DiningRoom Target과 메시지·flag·Room 참조를 변경 없이 검사한다.</summary>
    [MenuItem("Haunted House/Validate/Hospital Sector3 DiningRoom")]
    public static void Validate()
    {
        EnsureScene();

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
    private static void EnsureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("DiningRoom 설정·검증은 Play 모드가 아닌 Edit Mode에서 실행해야 합니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException($"DiningRoom 설정은 CoreSandbox 씬에서만 실행할 수 있습니다: {ScenePath}");
        }
    }

    /// <summary>현재 씬의 DiningRoom Target 구성이 계획서 계약을 만족하는지 검사한다.</summary>
    public static bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();
        Room diningRoom = FindRoomOrNull(DiningRoomId);
        MessagePanel messagePanel = UnityEngine.Object.FindFirstObjectByType<MessagePanel>(FindObjectsInactive.Include);
        GameSession gameSession = UnityEngine.Object.FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);
        Target freezerHint = FindTarget(diningRoom, FreezerHintTargetName);
        Target diary = FindTarget(diningRoom, DiaryTargetName);

        Check(errors, diningRoom != null, $"RoomId '{DiningRoomId}'를 찾지 못했습니다.");
        Check(errors, messagePanel != null, "MessagePanel을 찾지 못했습니다.");
        Check(errors, gameSession != null, "GameSession을 찾지 못했습니다.");
        Check(errors, CountTargets(diningRoom, FreezerHintTargetName) == 1,
            $"{FreezerHintTargetName} Target이 정확히 하나가 아닙니다.");
        Check(errors, CountTargets(diningRoom, DiaryTargetName) == 1,
            $"{DiaryTargetName} Target이 정확히 하나가 아닙니다.");

        CheckMessageTarget(errors, freezerHint, diningRoom, messagePanel, gameSession,
            FreezerMessage, StoryProgressFlags.FreezerHintRead, FreezerHintTargetName);
        CheckMessageTarget(errors, diary, diningRoom, messagePanel, gameSession,
            DiaryMessage, string.Empty, DiaryTargetName);

        report = errors.Count == 0
            ? "Sector3 DiningRoom의 힌트보드·일기장 Target 구성이 유효합니다."
            : "Sector3 DiningRoom Target 검증 실패:\n- " + string.Join("\n- ", errors);
        return errors.Count == 0;
    }

    /// <summary>지정한 Room에서 이름이 일치하는 Target을 찾는다.</summary>
    private static Target FindTarget(Room room, string targetName)
    {
        return room == null
            ? null
            : room.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => target != null && target.name == targetName);
    }

    /// <summary>지정한 Room에서 이름이 일치하는 Target의 개수를 센다.</summary>
    private static int CountTargets(Room room, string targetName)
    {
        return room == null
            ? 0
            : room.GetComponentsInChildren<Target>(true).Count(target => target != null && target.name == targetName);
    }

    /// <summary>메시지 Target의 공통 설정과 선택적인 Story Flag를 직렬화한다.</summary>
    private static Target ConfigureMessageTarget(
        Room ownerRoom,
        Transform root,
        string targetName,
        Transform anchor,
        string message,
        string completionId,
        string completionFlagId,
        MessagePanel messagePanel,
        GameSession gameSession,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Transform targetTransform = GetOrCreateChild(root, targetName);
        Vector3 targetPosition = anchor.position + Vector3.up * 1.1f;
        Vector3 requestedApproach = anchor.position + Vector3.up * 0.2f;
        Target target = ConfigureTargetShell(
            ownerRoom,
            targetTransform,
            targetPosition,
            requestedApproach,
            playerRoomTracker,
            markerPool,
            worldCamera);

        MessageTarget messageTarget = GetOrAdd<MessageTarget>(targetTransform.gameObject);
        SetObject(messageTarget, "messagePanel", messagePanel);
        SetString(messageTarget, "message", message);
        SetString(messageTarget, "completionId", completionId);
        SetObject(messageTarget, "gameSession", gameSession);
        SetString(messageTarget, "completionFlagId", completionFlagId);
        SetObject(target, "actionComponent", messageTarget);
        target.SetSelectable(true);
        EditorUtility.SetDirty(targetTransform.gameObject);
        return target;
    }

    /// <summary>Target의 이동·소유 Room·Collider·Presenter를 공통 규칙으로 설정한다.</summary>
    private static Target ConfigureTargetShell(
        Room ownerRoom,
        Transform targetTransform,
        Vector3 targetPosition,
        Vector3 requestedApproach,
        PlayerRoomTracker playerRoomTracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        targetTransform.position = targetPosition;

        // 클릭 영역은 물리 이동을 막지 않도록 Trigger로 만들고, UI Anchor는 대상 위에 둔다.
        BoxCollider clickArea = GetOrAdd<BoxCollider>(targetTransform.gameObject);
        clickArea.isTrigger = true;
        clickArea.center = Vector3.zero;
        clickArea.size = new Vector3(1.8f, 1.8f, 1.8f);

        Transform approachPoint = GetOrCreateChild(targetTransform, "ApproachPoint");
        approachPoint.position = SampleNavMesh(requestedApproach, requestedApproach);
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

    /// <summary>씬에 준비된 보드·테이블을 우선 사용하고 없으면 Room 기준의 명시적 Anchor를 만든다.</summary>
    private static Transform ResolveAnchor(Room room, string anchorName, bool preferTable)
    {
        Transform anchor = room.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate != null && candidate.name == anchorName);
        if (anchor != null)
        {
            return anchor;
        }

        string[] preferredNames = preferTable
            ? new[] { "TableWhiteKitchen", "DiningTable", "Table" }
            : new[] { "Story_FreezerHintBoard", "BulletinBoard", "NoticeBoard", "Board" };
        foreach (string preferredName in preferredNames)
        {
            anchor = room.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate != null &&
                                             candidate.name.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (anchor != null)
            {
                return anchor;
            }
        }

        // 아트 소품이 아직 없는 상태에서도 Target 설정을 재현할 수 있도록 논리 Room 기준의 Anchor를 만든다.
        anchor = GetOrCreateChild(room.transform, anchorName);
        anchor.position = room.GraphPosition + (preferTable ? Vector3.zero : Vector3.forward * 2f);
        anchor.rotation = room.RoomCamera != null ? room.RoomCamera.transform.rotation : Quaternion.identity;
        EditorUtility.SetDirty(anchor.gameObject);
        GameLog.LogWarning($"'{room.RoomId}'에서 '{anchorName}'에 사용할 아트 소품을 찾지 못해 논리 Anchor를 만들었습니다.", room);
        return anchor;
    }

    /// <summary>요청 위치 주변의 NavMesh에서 플레이어 접근 지점을 샘플링한다.</summary>
    private static Vector3 SampleNavMesh(Vector3 requested, Vector3 fallback)
    {
        if (NavMesh.SamplePosition(requested, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        // 테이블·벽처럼 대상 자체가 NavMesh를 가리는 경우에는 수평 주변을 먼저 조사한다.
        // 큰 반경 단일 샘플은 다른 층의 NavMesh를 선택할 수 있으므로 후보 중 가장 가까운 점을 고른다.
        bool foundCandidate = false;
        float bestDistance = float.MaxValue;
        Vector3 bestPosition = fallback;
        Vector3[] directions =
        {
            Vector3.forward,
            Vector3.back,
            Vector3.right,
            Vector3.left,
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
                // 수직으로 약간 떨어진 층 바닥도 후보에 포함하되, 최종 선택은 요청점과의 거리로 결정한다.
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit ringHit, 4f, NavMesh.AllAreas))
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

        if (foundCandidate)
        {
            return bestPosition;
        }

        if (NavMesh.SamplePosition(fallback, out hit, 20f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return requested;
    }

    /// <summary>메시지 Target 하나의 참조·문구·완료 flag 설정을 검사한다.</summary>
    private static void CheckMessageTarget(
        List<string> errors,
        Target target,
        Room ownerRoom,
        MessagePanel messagePanel,
        GameSession gameSession,
        string expectedMessage,
        string expectedFlag,
        string targetName)
    {
        Check(errors, target != null, $"{targetName} Target을 찾지 못했습니다.");
        if (target == null)
        {
            return;
        }

        Check(errors, target.OwnerRoom == ownerRoom, $"{targetName}의 OwnerRoom이 {DiningRoomId}가 아닙니다.");
        Check(errors, target.TryValidateConfiguration(out string configurationError),
            $"{targetName}: {configurationError}");
        Check(errors, target.GetComponent<TargetPresenter>() != null, $"{targetName}에 TargetPresenter가 없습니다.");
        Check(errors, target.GetComponent<BoxCollider>() != null, $"{targetName}에 클릭용 BoxCollider가 없습니다.");
        Check(errors, target.Action is MessageTarget, $"{targetName}의 행동이 MessageTarget이 아닙니다.");

        if (target.Action is not MessageTarget messageTarget)
        {
            return;
        }

        SerializedObject serialized = new(messageTarget);
        string actualMessage = serialized.FindProperty("message")?.stringValue;
        string actualCompletionId = serialized.FindProperty("completionId")?.stringValue;
        string actualFlag = serialized.FindProperty("completionFlagId")?.stringValue;
        UnityEngine.Object actualPanel = serialized.FindProperty("messagePanel")?.objectReferenceValue;
        UnityEngine.Object actualSession = serialized.FindProperty("gameSession")?.objectReferenceValue;
        Check(errors, actualPanel == messagePanel, $"{targetName}의 MessagePanel 참조가 잘못됐습니다.");
        Check(errors, actualSession == gameSession, $"{targetName}의 GameSession 참조가 잘못됐습니다.");
        Check(errors, actualMessage == expectedMessage, $"{targetName}의 메시지가 계획 문구와 다릅니다.");
        Check(errors, actualCompletionId == targetName, $"{targetName}의 completionId가 잘못됐습니다.");
        Check(errors, actualFlag == expectedFlag, $"{targetName}의 완료 Story Flag가 잘못됐습니다.");
    }

    /// <summary>RoomId로 Room을 찾고 누락 시 예외를 발생시킨다.</summary>
    private static Room FindRequiredRoom(string roomId)
    {
        return FindRoomOrNull(roomId)
            ?? throw new InvalidOperationException($"RoomId '{roomId}'를 찾지 못했습니다.");
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
        Undo.RegisterCreatedObjectUndo(childObject, "Create DiningRoom target child");
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
