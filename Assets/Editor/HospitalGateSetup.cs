#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Core;
using HauntedHouse.Dialogue;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 Sector 2 철창 Root, 논리 Doorway, 최초 진입 대화와 체크포인트 flag를 구성한다.
/// 공유 씬을 자동으로 순회해 임의의 Target을 바꾸지 않고 명시된 Gate 오브젝트만 수정한다.
/// </summary>
public static class HospitalGateSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string SetupMenuPath = "Haunted House/Setup/Hospital Sector2 Gate";
    private const string ValidateMenuPath = "Haunted House/Validate/Hospital Sector2 Gate";
    private const string DialogueAssetPath = "Assets/Data/Dialogue/S2_CenterHallwayGateDialogue.asset";
    private const string GateRootName = "GateRoot";

    /// <summary>
    /// 현재 CoreSandbox에 Sector 2 철창의 런타임 참조와 최초 진입 대화를 생성하거나 갱신한다.
    /// 이 메서드는 Play 모드가 아닌 Unity Editor에서 사람이 메뉴로 실행해야 한다.
    /// </summary>
    [MenuItem(SetupMenuPath)]
    public static void Configure()
    {
        EnsureScene();

        GameSession gameSession = FindRequired<GameSession>("GameSession");
        DialogueService dialogueService = FindRequired<DialogueService>("DialogueService");
        PlayerRoomTracker playerRoomTracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        CameraDirector cameraDirector = FindRequired<CameraDirector>("CameraDirector");
        MessagePanel messagePanel = FindRequired<MessagePanel>("MessagePanel");

        Room centerA = FindRoom("CenterHallway_A");
        Room centerB = FindRoom("CenterHallway_B");
        Doorway logicalGate = FindDoorway("LogicalGate_CenterHallway_A_B", centerA, centerB);

        GameObject gateRoot = GetOrCreateGateRoot(logicalGate.transform);
        GateController gateController = GetOrAdd<GateController>(logicalGate.gameObject);
        SetObject(gateController, "gateRoot", gateRoot);
        SetObject(gateController, "logicalGate", logicalGate);
        SetObject(gateController, "gameSession", gameSession);
        EditorUtility.SetDirty(gateController);

        ConfigureGateTargets(logicalGate, playerRoomTracker, cameraDirector, messagePanel);

        DialogueSequence dialogue = GetOrCreateDialogue();
        ConfigureDiscoveryEntry(centerA, gameSession, playerRoomTracker, dialogueService, dialogue);
        ConfigureCheckpointFlags();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameLog.Log("Hospital Sector 2 철창 Root, 논리 잠금, 최초 진입 대화 구성을 완료했습니다.");
    }

    /// <summary>현재 씬의 Sector 2 철창 구성이 유효한지 읽기 전용으로 검사한다.</summary>
    [MenuItem(ValidateMenuPath)]
    public static void Validate()
    {
        EnsureScene();

        List<string> errors = new();
        Room centerA = FindRoomOrNull("CenterHallway_A");
        Room centerB = FindRoomOrNull("CenterHallway_B");
        Check(errors, centerA != null, "CenterHallway_A Room이 없습니다.");
        Check(errors, centerB != null, "CenterHallway_B Room이 없습니다.");

        Doorway logicalGate = centerA != null && centerB != null
            ? FindDoorwayOrNull("LogicalGate_CenterHallway_A_B", centerA, centerB)
            : null;
        Check(errors, logicalGate != null, "LogicalGate_CenterHallway_A_B를 찾지 못했습니다.");

        GateController gateController = logicalGate != null
            ? logicalGate.GetComponent<GateController>()
            : null;
        Check(errors, gateController != null, "논리 철창에 GateController가 없습니다.");
        if (gateController != null && !gateController.TryValidateConfiguration(out string gateError))
        {
            errors.Add(gateError);
        }

        MessagePanel messagePanel = UnityEngine.Object.FindFirstObjectByType<MessagePanel>(FindObjectsInactive.Include);
        PlayerRoomTracker tracker = UnityEngine.Object.FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        CameraDirector cameraDirector = UnityEngine.Object.FindFirstObjectByType<CameraDirector>(FindObjectsInactive.Include);
        if (logicalGate != null)
        {
            DoorTarget[] doorTargets = logicalGate.GetComponentsInChildren<DoorTarget>(true);
            Check(errors, doorTargets.Length >= 2, "논리 철창의 양방향 DoorTarget이 부족합니다.");
            foreach (DoorTarget doorTarget in doorTargets)
            {
                Target target = doorTarget.GetComponent<Target>();
                Check(errors, target != null, $"철창 DoorTarget '{doorTarget.name}'에 Target이 없습니다.");
                Check(errors, target != null && target.OwnerRoom != null, $"철창 DoorTarget '{doorTarget.name}'의 OwnerRoom이 비어 있습니다.");
                Check(errors, messagePanel != null && cameraDirector != null && tracker != null,
                    "철창 DoorTarget을 검사하려면 공용 MessagePanel, CameraDirector, PlayerRoomTracker가 필요합니다.");
            }
        }

        Transform eventTransform = centerA != null
            ? centerA.transform.Find("Sector2StoryEvents/S2_CenterHallwayGateDiscoveryEntry")
            : null;
        RoomEntryEvent entryEvent = eventTransform != null
            ? eventTransform.GetComponent<RoomEntryEvent>()
            : null;
        RoomEntryDialogueSequence dialoguePlayer = eventTransform != null
            ? eventTransform.GetComponent<RoomEntryDialogueSequence>()
            : null;
        Check(errors, entryEvent != null, "S2_CenterHallwayGateDiscoveryEntry가 없습니다.");
        Check(errors, dialoguePlayer != null, "철창 최초 진입 대화 재생 컴포넌트가 없습니다.");
        if (dialoguePlayer != null && !dialoguePlayer.TryValidateConfiguration(out string dialogueError))
        {
            errors.Add(dialogueError);
        }
        Check(errors, entryEvent != null && entryEvent.OnEntered != null &&
                      entryEvent.OnEntered.GetPersistentEventCount() > 0,
            "철창 최초 진입 이벤트의 OnEntered UnityEvent가 비어 있습니다.");

        Check(errors, AssetDatabase.LoadAssetAtPath<DialogueSequence>(DialogueAssetPath) != null,
            $"철창 대화 에셋이 없습니다: {DialogueAssetPath}");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Hospital Sector 2 철창 검증 실패:\n- " + string.Join("\n- ", errors));
        }

        GameLog.Log("Hospital Sector 2 철창 구성이 유효합니다.");
    }

    /// <summary>현재 활성 씬과 Editor 실행 상태가 설정 조건에 맞는지 확인한다.</summary>
    private static void EnsureScene()
    {
        if (EditorApplication.isPlaying)
        {
            throw new InvalidOperationException("Play 모드에서는 Hospital Sector 2 철창 설정을 실행할 수 없습니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException("Hospital Sector 2 철창 설정은 CoreSandbox 씬에서만 실행할 수 있습니다.");
        }
    }

    /// <summary>현재 씬에서 필수 컴포넌트를 찾아 없으면 설정을 중단한다.</summary>
    private static T FindRequired<T>(string label) where T : Component
    {
        T component = UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        return component != null
            ? component
            : throw new InvalidOperationException($"{label} 컴포넌트를 찾지 못했습니다.");
    }

    /// <summary>RoomId로 씬의 Room을 찾는다.</summary>
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

    /// <summary>지정한 두 Room 사이의 이름이 같은 Doorway를 찾는다.</summary>
    private static Doorway FindDoorway(string doorwayName, Room first, Room second)
    {
        return FindDoorwayOrNull(doorwayName, first, second)
            ?? throw new InvalidOperationException(
                $"Doorway '{doorwayName}'을 '{first.RoomId}'와 '{second.RoomId}' 사이에서 찾지 못했습니다.");
    }

    /// <summary>지정한 두 Room 사이의 Doorway를 찾되 없으면 null을 반환한다.</summary>
    private static Doorway FindDoorwayOrNull(string doorwayName, Room first, Room second)
    {
        return UnityEngine.Object.FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(doorway => doorway.name == doorwayName &&
                ((doorway.RoomA == first && doorway.RoomB == second) ||
                 (doorway.RoomA == second && doorway.RoomB == first)));
    }

    /// <summary>논리 Doorway 아래에 시각 철창을 넣을 별도 Root를 생성하거나 기존 Root를 반환한다.</summary>
    private static GameObject GetOrCreateGateRoot(Transform logicalGateTransform)
    {
        Transform existing = logicalGateTransform.Find(GateRootName);
        if (existing != null)
        {
            return existing.gameObject;
        }

        GameObject root = new(GateRootName);
        root.transform.SetParent(logicalGateTransform, false);
        EditorUtility.SetDirty(root);
        return root;
    }

    /// <summary>철창의 양방향 DoorTarget을 논리 Gate에 맞게 구성한다.</summary>
    private static void ConfigureGateTargets(
        Doorway logicalGate,
        PlayerRoomTracker playerRoomTracker,
        CameraDirector cameraDirector,
        MessagePanel messagePanel)
    {
        foreach (DoorTarget doorTarget in logicalGate.GetComponentsInChildren<DoorTarget>(true))
        {
            SetObject(doorTarget, "doorway", logicalGate);
            SetObject(doorTarget, "playerRoomTracker", playerRoomTracker);
            SetObject(doorTarget, "cameraDirector", cameraDirector);
            SetObject(doorTarget, "lockedItemUseTarget", null);
            SetObject(doorTarget, "lockedMessagePanel", messagePanel);
            SetString(doorTarget, "lockedMessage", "철창은 잠겨 있다. 다른 방법을 찾아야 한다.");

            Target target = doorTarget.GetComponent<Target>();
            target?.SetSelectable(true);
            EditorUtility.SetDirty(doorTarget.gameObject);
        }
    }

    /// <summary>CenterHallway_A 최초 진입 시 대화를 재생하고 완료 flag를 저장하도록 연결한다.</summary>
    private static void ConfigureDiscoveryEntry(
        Room centerA,
        GameSession gameSession,
        PlayerRoomTracker playerRoomTracker,
        DialogueService dialogueService,
        DialogueSequence dialogue)
    {
        Transform root = GetOrCreateChild(centerA.transform, "Sector2StoryEvents");
        Transform eventTransform = GetOrCreateChild(root, "S2_CenterHallwayGateDiscoveryEntry");
        RoomEntryEvent entryEvent = GetOrAdd<RoomEntryEvent>(eventTransform.gameObject);
        RoomEntryDialogueSequence dialoguePlayer = GetOrAdd<RoomEntryDialogueSequence>(eventTransform.gameObject);

        SetObject(entryEvent, "targetRoom", centerA);
        SetString(entryEvent, "completionFlagId", GateController.GateDiscoveryDialogueShownFlag);
        SetBool(entryEvent, "markCompletionAfterEvent", true);
        SetObject(entryEvent, "playerRoomTracker", playerRoomTracker);
        SetObject(entryEvent, "gameSession", gameSession);

        SetObject(dialoguePlayer, "dialogueService", dialogueService);
        SetObject(dialoguePlayer, "sequence", dialogue);
        SetObject(dialoguePlayer, "completionEvent", entryEvent);

        ClearPersistentListeners(entryEvent);
        UnityEventTools.AddPersistentListener(entryEvent.OnEntered, dialoguePlayer.Play);
        EditorUtility.SetDirty(eventTransform.gameObject);
    }

    /// <summary>철창 발견 대화 완료 여부가 Save2 이후에도 유지되도록 체크포인트 flag를 보완한다.</summary>
    private static void ConfigureCheckpointFlags()
    {
        CheckpointDefinition[] checkpoints = UnityEngine.Object.FindObjectsByType<CheckpointDefinition>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        string[] ids = { "Save2", "Save3", "Save4", "Save5" };

        foreach (string id in ids)
        {
            CheckpointDefinition checkpoint = checkpoints.FirstOrDefault(candidate => candidate.CheckpointId == id);
            if (checkpoint == null)
            {
                throw new InvalidOperationException($"CheckpointDefinition '{id}'를 찾지 못했습니다.");
            }

            SerializedObject serialized = new(checkpoint);
            SerializedProperty flags = serialized.FindProperty("progressFlags");
            if (flags == null)
            {
                throw new InvalidOperationException($"CheckpointDefinition '{id}'의 progressFlags를 찾지 못했습니다.");
            }

            var values = new List<string>();
            for (int index = 0; index < flags.arraySize; index++)
            {
                values.Add(flags.GetArrayElementAtIndex(index).stringValue);
            }

            if (!values.Contains(GateController.GateDiscoveryDialogueShownFlag, StringComparer.Ordinal))
            {
                values.Add(GateController.GateDiscoveryDialogueShownFlag);
            }

            flags.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
            {
                flags.GetArrayElementAtIndex(index).stringValue = values[index];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(checkpoint);
        }
    }

    /// <summary>철창 발견 대화 에셋을 생성하거나 계획된 두 줄로 갱신한다.</summary>
    private static DialogueSequence GetOrCreateDialogue()
    {
        DialogueSequence dialogue = AssetDatabase.LoadAssetAtPath<DialogueSequence>(DialogueAssetPath);
        if (dialogue == null)
        {
            dialogue = ScriptableObject.CreateInstance<DialogueSequence>();
            AssetDatabase.CreateAsset(dialogue, DialogueAssetPath);
        }

        SerializedObject serialized = new(dialogue);
        serialized.FindProperty("pauseGame").boolValue = true;
        serialized.FindProperty("autoAdvanceDelay").floatValue = 0f;
        SerializedProperty lines = serialized.FindProperty("lines");
        lines.arraySize = 2;
        SetDialogueLine(lines.GetArrayElementAtIndex(0), DialogueSpeaker.Radio, "[중앙 복도 철창 발견]");
        SetDialogueLine(lines.GetArrayElementAtIndex(1), DialogueSpeaker.Player,
            "철창 때문에 바로 나갈 수 없다. 다른 방법을 찾아야 한다.");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(dialogue);
        return dialogue;
    }

    /// <summary>직렬화된 DialogueLine 하나의 화자와 문장을 설정한다.</summary>
    private static void SetDialogueLine(SerializedProperty line, DialogueSpeaker speaker, string text)
    {
        line.FindPropertyRelative("speaker").enumValueIndex = (int)speaker;
        line.FindPropertyRelative("text").stringValue = text;
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
        EditorUtility.SetDirty(childObject);
        return childObject.transform;
    }

    /// <summary>컴포넌트가 없을 때만 같은 GameObject에 하나 추가한다.</summary>
    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        return target.GetComponent<T>() ?? target.AddComponent<T>();
    }

    /// <summary>컴포넌트의 private UnityEngine.Object 참조를 직렬화해 설정한다.</summary>
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

    /// <summary>컴포넌트의 private string 값을 직렬화해 설정한다.</summary>
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

    /// <summary>컴포넌트의 private bool 값을 직렬화해 설정한다.</summary>
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

    /// <summary>RoomEntryEvent에 남아 있는 이전 Persistent Listener를 제거한다.</summary>
    private static void ClearPersistentListeners(RoomEntryEvent entryEvent)
    {
        SerializedObject serialized = new(entryEvent);
        SerializedProperty calls = serialized.FindProperty("onEntered.m_PersistentCalls.m_Calls");
        if (calls == null)
        {
            throw new InvalidOperationException("RoomEntryEvent의 onEntered PersistentCall 목록을 찾지 못했습니다.");
        }

        calls.arraySize = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>검증 조건이 거짓이면 결과 목록에 사람이 확인할 오류를 추가한다.</summary>
    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}
#endif
