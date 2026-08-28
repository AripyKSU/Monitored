#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Dialogue;
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
/// CoreSandbox의 Sector4 MorgueRoom2에 시체안치함 A·B Target과 마스터키 무전기 대화를 구성하는 Editor 도구다.
/// 기존 Target·MessageTarget·DialogueService·PlayerInventory를 재사용하며, 실행할 때마다 같은 오브젝트를 갱신한다.
/// </summary>
public static class HospitalMorgueRoom2Setup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string RoomId = "S4_MorgueRoom2";
    private const string TargetRootName = "Sector4MorgueRoom2Targets";
    private const string BoxATargetName = "MorgueBoxA";
    private const string BoxBTargetName = "MorgueBoxB";
    private const string BoxAAnchorName = "MorgueRoom2_BoxAAnchor";
    private const string BoxBAnchorName = "MorgueRoom2_BoxBAnchor";
    private const string BoxAMessage = "시체안치함 A";
    private const string BoxBUnopenedMessage = "시체안치함 B";
    private const string BoxBOpenedMessage = "시체안치함을 열어보았다.";
    private const string DialogueAssetPath = "Assets/Data/Dialogue/S4_MasterKeyRadioDialogue.asset";
    private const string DialogueMessage = "[마스터키 획득 후 무전기]";

    /// <summary>현재 열린 CoreSandbox에 MorgueRoom2 Target과 Dialogue asset을 멱등적으로 구성한다.</summary>
    [MenuItem("Haunted House/Setup/Hospital Sector4 MorgueRoom2")]
    public static void Configure()
    {
        EnsureEditModeAndScene();

        Room room = FindRequiredRoom();
        MessagePanel messagePanel = FindRequired<MessagePanel>("MessagePanel");
        GameSession gameSession = FindRequired<GameSession>("GameSession");
        PlayerInventory inventory = FindRequired<PlayerInventory>("PlayerInventory");
        DialogueService dialogueService = FindRequired<DialogueService>("DialogueService");
        PlayerRoomTracker roomTracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        TargetMarkerPool markerPool = FindRequired<TargetMarkerPool>("TargetMarkerPool");
        Camera worldCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        ItemDefinition masterKey = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Data/Items/MasterKey.asset");
        if (masterKey == null)
        {
            throw new InvalidOperationException("Assets/Data/Items/MasterKey.asset을 찾지 못했습니다.");
        }

        if (worldCamera == null)
        {
            throw new InvalidOperationException("MorgueRoom2 Target이 사용할 Camera를 찾지 못했습니다.");
        }

        DialogueSequence dialogue = GetOrCreateDialogue();
        Transform root = GetOrCreateChild(room.transform, TargetRootName);
        Transform boxAAnchor = GetOrCreateAnchor(room, BoxAAnchorName, Vector3.left * 2f);
        Transform boxBAnchor = GetOrCreateAnchor(room, BoxBAnchorName, Vector3.right * 2f);

        Target boxA = ConfigureMessageTarget(
            room, root, BoxATargetName, boxAAnchor, BoxAMessage, messagePanel, gameSession,
            roomTracker, markerPool, worldCamera);
        Target boxB = ConfigureBoxBTarget(
            room, root, BoxBTargetName, boxBAnchor, messagePanel, gameSession, inventory,
            dialogueService, masterKey, dialogue, roomTracker, markerPool, worldCamera);

        EditorUtility.SetDirty(room.gameObject);
        EditorUtility.SetDirty(root.gameObject);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!ValidateConfiguration(out string report))
        {
            throw new InvalidOperationException(report);
        }

        GameLog.Log($"Sector4 MorgueRoom2 Target 구성을 완료했습니다: {boxA.name}, {boxB.name}.");
    }

    /// <summary>MorgueRoom2 Target과 마스터키 대화의 참조·문구·Room 설정을 변경 없이 검사한다.</summary>
    [MenuItem("Haunted House/Validate/Hospital Sector4 MorgueRoom2")]
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

    /// <summary>설정 도구가 Edit Mode의 CoreSandbox에서만 실행되도록 확인한다.</summary>
    private static void EnsureEditModeAndScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("MorgueRoom2 설정·검증은 Play 모드가 아닌 Edit Mode에서 실행해야 합니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException($"MorgueRoom2 설정은 CoreSandbox 씬에서만 실행할 수 있습니다: {ScenePath}");
        }
    }

    /// <summary>현재 씬의 MorgueRoom2 구성이 28번 계획서 계약을 만족하는지 검사한다.</summary>
    public static bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();
        Room room = FindRoomOrNull();
        Transform root = room != null ? room.transform.Find(TargetRootName) : null;
        Target boxA = FindTarget(root, BoxATargetName);
        Target boxB = FindTarget(root, BoxBTargetName);
        MessagePanel messagePanel = UnityEngine.Object.FindFirstObjectByType<MessagePanel>(FindObjectsInactive.Include);
        GameSession gameSession = UnityEngine.Object.FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);
        PlayerInventory inventory = UnityEngine.Object.FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);
        DialogueService dialogueService = UnityEngine.Object.FindFirstObjectByType<DialogueService>(FindObjectsInactive.Include);
        PlayerRoomTracker roomTracker = UnityEngine.Object.FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        TargetMarkerPool markerPool = UnityEngine.Object.FindFirstObjectByType<TargetMarkerPool>(FindObjectsInactive.Include);
        Camera worldCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        ItemDefinition masterKey = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Data/Items/MasterKey.asset");
        DialogueSequence dialogue = AssetDatabase.LoadAssetAtPath<DialogueSequence>(DialogueAssetPath);

        Check(errors, room != null, $"RoomId '{RoomId}'를 찾지 못했습니다.");
        Check(errors, root != null, $"'{TargetRootName}' 루트를 찾지 못했습니다.");
        Check(errors, messagePanel != null, "MessagePanel을 찾지 못했습니다.");
        Check(errors, gameSession != null, "GameSession을 찾지 못했습니다.");
        Check(errors, inventory != null, "PlayerInventory를 찾지 못했습니다.");
        Check(errors, dialogueService != null, "DialogueService를 찾지 못했습니다.");
        Check(errors, masterKey != null, "MasterKey ItemDefinition을 찾지 못했습니다.");
        Check(errors, dialogue != null, $"Dialogue asset을 찾지 못했습니다: {DialogueAssetPath}");
        Check(errors, CountTargets(root, BoxATargetName) == 1, $"{BoxATargetName} Target이 정확히 하나가 아닙니다.");
        Check(errors, CountTargets(root, BoxBTargetName) == 1, $"{BoxBTargetName} Target이 정확히 하나가 아닙니다.");

        CheckCommonTarget(errors, boxA, room, roomTracker, markerPool, worldCamera, BoxATargetName);
        Check(errors, boxA == null || boxA.Action is MessageTarget, $"{BoxATargetName}의 행동이 MessageTarget이 아닙니다.");
        if (boxA?.Action is MessageTarget boxAMessage)
        {
            SerializedObject serialized = new(boxAMessage);
            Check(errors, serialized.FindProperty("message")?.stringValue == BoxAMessage,
                $"{BoxATargetName}의 메시지가 계획 문구와 다릅니다.");
            Check(errors, serialized.FindProperty("completionFlagId")?.stringValue == string.Empty,
                $"{BoxATargetName}에 불필요한 완료 Story Flag가 설정됐습니다.");
        }

        CheckCommonTarget(errors, boxB, room, roomTracker, markerPool, worldCamera, BoxBTargetName);
        Check(errors, boxB == null || boxB.Action is MorgueBoxBTarget, $"{BoxBTargetName}의 행동이 MorgueBoxBTarget이 아닙니다.");
        if (boxB?.Action is MorgueBoxBTarget boxBAction)
        {
            Check(errors, boxBAction.TryValidateConfiguration(out string actionError),
                $"{BoxBTargetName}: {actionError}");
            SerializedObject serialized = new(boxBAction);
            Check(errors, serialized.FindProperty("masterKey")?.objectReferenceValue == masterKey,
                $"{BoxBTargetName}의 MasterKey 참조가 잘못됐습니다.");
            Check(errors, serialized.FindProperty("masterKeyDialogue")?.objectReferenceValue == dialogue,
                $"{BoxBTargetName}의 Dialogue 참조가 잘못됐습니다.");
            Check(errors, serialized.FindProperty("unopenedMessage")?.stringValue == BoxBUnopenedMessage,
                $"{BoxBTargetName}의 기본 조사 메시지가 잘못됐습니다.");
            Check(errors, serialized.FindProperty("openedMessage")?.stringValue == BoxBOpenedMessage,
                $"{BoxBTargetName}의 개봉 메시지가 잘못됐습니다.");
        }

        report = errors.Count == 0
            ? "Sector4 MorgueRoom2의 Box A·Box B Target과 마스터키 Dialogue 구성이 유효합니다."
            : "Sector4 MorgueRoom2 Target 검증 실패:\n- " + string.Join("\n- ", errors);
        return errors.Count == 0;
    }

    /// <summary>마스터키 획득 후 무전기 DialogueSequence asset을 생성하거나 갱신한다.</summary>
    private static DialogueSequence GetOrCreateDialogue()
    {
        DialogueSequence dialogue = AssetDatabase.LoadAssetAtPath<DialogueSequence>(DialogueAssetPath);
        if (dialogue == null)
        {
            dialogue = ScriptableObject.CreateInstance<DialogueSequence>();
            dialogue.name = "S4_MasterKeyRadioDialogue";
            AssetDatabase.CreateAsset(dialogue, DialogueAssetPath);
        }

        SerializedObject serialized = new(dialogue);
        serialized.FindProperty("pauseGame").boolValue = true;
        serialized.FindProperty("autoAdvanceDelay").floatValue = 0f;
        SerializedProperty lines = serialized.FindProperty("lines");
        lines.arraySize = 1;
        SerializedProperty line = lines.GetArrayElementAtIndex(0);
        line.FindPropertyRelative("speaker").enumValueIndex = (int)DialogueSpeaker.Radio;
        line.FindPropertyRelative("text").stringValue = DialogueMessage;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(dialogue);
        return dialogue;
    }

    /// <summary>Box A에 기존 MessageTarget을 연결한다.</summary>
    private static Target ConfigureMessageTarget(
        Room room, Transform root, string targetName, Transform anchor, string message,
        MessagePanel messagePanel, GameSession gameSession, PlayerRoomTracker roomTracker,
        TargetMarkerPool markerPool, Camera worldCamera)
    {
        Transform targetTransform = GetOrCreateChild(root, targetName);
        Target target = ConfigureTargetShell(room, targetTransform, anchor, roomTracker, markerPool, worldCamera);
        MessageTarget action = GetOrAdd<MessageTarget>(targetTransform.gameObject);
        SetObject(action, "messagePanel", messagePanel);
        SetString(action, "message", message);
        SetString(action, "completionId", targetName);
        SetObject(action, "gameSession", gameSession);
        SetString(action, "completionFlagId", string.Empty);
        SetObject(target, "actionComponent", action);
        target.SetSelectable(true);
        EditorUtility.SetDirty(targetTransform.gameObject);
        return target;
    }

    /// <summary>Box B에 조건부 마스터키 행동과 기존 Target 표현을 연결한다.</summary>
    private static Target ConfigureBoxBTarget(
        Room room, Transform root, string targetName, Transform anchor, MessagePanel messagePanel,
        GameSession gameSession, PlayerInventory inventory, DialogueService dialogueService,
        ItemDefinition masterKey, DialogueSequence dialogue, PlayerRoomTracker roomTracker,
        TargetMarkerPool markerPool, Camera worldCamera)
    {
        Transform targetTransform = GetOrCreateChild(root, targetName);
        Target target = ConfigureTargetShell(room, targetTransform, anchor, roomTracker, markerPool, worldCamera);
        MorgueBoxBTarget action = GetOrAdd<MorgueBoxBTarget>(targetTransform.gameObject);
        SetObject(action, "gameSession", gameSession);
        SetObject(action, "inventory", inventory);
        SetObject(action, "messagePanel", messagePanel);
        SetObject(action, "dialogueService", dialogueService);
        SetObject(action, "masterKey", masterKey);
        SetObject(action, "masterKeyDialogue", dialogue);
        SetString(action, "completionId", targetName);
        SetString(action, "unopenedMessage", BoxBUnopenedMessage);
        SetString(action, "openedMessage", BoxBOpenedMessage);
        SetObject(target, "actionComponent", action);
        target.SetSelectable(true);
        EditorUtility.SetDirty(targetTransform.gameObject);
        return target;
    }

    /// <summary>Target의 접근 지점·OwnerRoom·Presenter를 공통 규칙으로 설정한다.</summary>
    private static Target ConfigureTargetShell(
        Room room, Transform targetTransform, Transform anchor, PlayerRoomTracker roomTracker,
        TargetMarkerPool markerPool, Camera worldCamera)
    {
        targetTransform.position = anchor.position + Vector3.up * 1.1f;

        BoxCollider clickArea = GetOrAdd<BoxCollider>(targetTransform.gameObject);
        clickArea.isTrigger = true;
        clickArea.center = Vector3.zero;
        clickArea.size = new Vector3(1.8f, 1.8f, 1.8f);

        Transform approachPoint = GetOrCreateChild(targetTransform, "ApproachPoint");
        approachPoint.position = SampleNavMesh(anchor.position + anchor.forward * 2f, anchor.position + Vector3.forward * 2f);
        Vector3 facingDirection = targetTransform.position - approachPoint.position;
        approachPoint.rotation = facingDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(facingDirection, Vector3.up)
            : Quaternion.identity;

        Transform uiAnchor = GetOrCreateChild(targetTransform, "UIAnchor");
        uiAnchor.position = targetTransform.position + Vector3.up * 0.6f;

        Target target = GetOrAdd<Target>(targetTransform.gameObject);
        SetObject(target, "approachPoint", approachPoint);
        SetObject(target, "ownerRoom", room);
        SetBool(target, "selectable", true);

        TargetPresenter presenter = GetOrAdd<TargetPresenter>(targetTransform.gameObject);
        SetObject(presenter, "target", target);
        SetObject(presenter, "clickArea", clickArea);
        SetObject(presenter, "uiAnchor", uiAnchor);
        SetObject(presenter, "playerRoomTracker", roomTracker);
        SetObject(presenter, "worldCamera", worldCamera);
        SetObject(presenter, "markerPool", markerPool);
        EditorUtility.SetDirty(targetTransform.gameObject);
        return target;
    }

    /// <summary>기존 논리 Anchor를 사용하거나 Room 기준점 주변에 새 Anchor를 만든다.</summary>
    private static Transform GetOrCreateAnchor(Room room, string anchorName, Vector3 offset)
    {
        Transform anchor = room.transform.Find(anchorName);
        if (anchor != null)
        {
            return anchor;
        }

        GameObject anchorObject = new(anchorName);
        anchorObject.transform.SetParent(room.transform, false);
        anchorObject.transform.position = room.GraphPosition + offset;
        anchorObject.transform.rotation = room.RoomCamera != null
            ? room.RoomCamera.transform.rotation
            : Quaternion.identity;
        Undo.RegisterCreatedObjectUndo(anchorObject, "Create MorgueRoom2 target anchor");
        return anchorObject.transform;
    }

    /// <summary>요청 위치 주변의 NavMesh에서 가장 가까운 접근 지점을 찾는다.</summary>
    private static Vector3 SampleNavMesh(Vector3 requested, Vector3 fallback)
    {
        if (NavMesh.SamplePosition(requested, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        foreach (Vector3 direction in directions)
        {
            if (NavMesh.SamplePosition(requested + direction * 2f, out NavMeshHit candidate, 2f, NavMesh.AllAreas))
            {
                return candidate.position;
            }
        }

        return fallback;
    }

    /// <summary>RoomId로 MorgueRoom2를 찾아 반환한다.</summary>
    private static Room FindRequiredRoom()
    {
        return FindRoomOrNull() ?? throw new InvalidOperationException($"RoomId '{RoomId}'를 찾지 못했습니다.");
    }

    /// <summary>RoomId로 MorgueRoom2를 찾아 없으면 null을 반환한다.</summary>
    private static Room FindRoomOrNull()
    {
        return UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.RoomId == RoomId);
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
        return root == null ? null : root.GetComponentsInChildren<Target>(true)
            .FirstOrDefault(candidate => candidate != null && candidate.name == targetName);
    }

    /// <summary>루트 아래 지정된 이름의 Target 개수를 센다.</summary>
    private static int CountTargets(Transform root, string targetName)
    {
        return root == null ? 0 : root.GetComponentsInChildren<Target>(true)
            .Count(candidate => candidate != null && candidate.name == targetName);
    }

    /// <summary>Target의 공통 Room·Presenter·접근 설정을 검사한다.</summary>
    private static void CheckCommonTarget(
        List<string> errors, Target target, Room room, PlayerRoomTracker roomTracker,
        TargetMarkerPool markerPool, Camera worldCamera, string targetName)
    {
        Check(errors, target != null, $"{targetName} Target을 찾지 못했습니다.");
        if (target == null)
        {
            return;
        }

        Check(errors, target.OwnerRoom == room, $"{targetName}의 OwnerRoom이 {RoomId}가 아닙니다.");
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
        Check(errors, serialized.FindProperty("playerRoomTracker")?.objectReferenceValue == roomTracker,
            $"{targetName} Presenter의 PlayerRoomTracker 참조가 잘못됐습니다.");
        Check(errors, serialized.FindProperty("markerPool")?.objectReferenceValue == markerPool,
            $"{targetName} Presenter의 MarkerPool 참조가 잘못됐습니다.");
        Check(errors, serialized.FindProperty("worldCamera")?.objectReferenceValue == worldCamera,
            $"{targetName} Presenter의 Camera 참조가 잘못됐습니다.");
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
        Undo.RegisterCreatedObjectUndo(childObject, "Create MorgueRoom2 target child");
        return childObject.transform;
    }

    /// <summary>같은 GameObject에 지정 컴포넌트가 없을 때만 추가한다.</summary>
    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    /// <summary>SerializedObject의 Unity 참조 필드를 설정한다.</summary>
    private static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>SerializedObject의 문자열 필드를 설정한다.</summary>
    private static void SetString(UnityEngine.Object target, string propertyName, string value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>SerializedObject의 bool 필드를 설정한다.</summary>
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
