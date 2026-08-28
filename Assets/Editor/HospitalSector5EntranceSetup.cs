#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Core;
using HauntedHouse.Dialogue;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 Sector5 정문 Room에 도착 이벤트 Controller와 안내 Dialogue를 연결한다.
/// 기존 Room·GameSession·DialogueService를 재사용하며 반복 실행해도 구성을 중복 생성하지 않는다.
/// </summary>
public static class HospitalSector5EntranceSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string ControllerObjectName = "Sector5EntranceController";
    private const string EntranceRoomId = "S5_DoorArea";
    private const string DialogueAssetPath = "Assets/Data/Dialogue/S5_EntranceDialogue.asset";

    /// <summary>현재 열린 CoreSandbox에 Sector5 정문 도착 구성을 멱등적으로 적용한다.</summary>
    [MenuItem("Haunted House/Setup/Hospital Sector5 Entrance")]
    public static void Configure()
    {
        EnsureEditModeAndScene();

        Room entranceRoom = FindRequiredRoom(EntranceRoomId);
        GameSession gameSession = FindRequired<GameSession>("GameSession");
        PlayerRoomTracker roomTracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        DialogueService dialogueService = FindRequired<DialogueService>("DialogueService");
        DialogueSequence dialogue = GetOrCreateEntranceDialogue();

        GameObject controllerObject = FindControllerObject();
        if (controllerObject == null)
        {
            controllerObject = new GameObject(ControllerObjectName);
            Undo.RegisterCreatedObjectUndo(controllerObject, "Create Sector5 entrance controller");
            SceneManager.MoveGameObjectToScene(controllerObject, SceneManager.GetActiveScene());
        }

        Sector5EntranceController controller = controllerObject.GetComponent<Sector5EntranceController>() ??
                                                Undo.AddComponent<Sector5EntranceController>(controllerObject);
        SetObject(controller, "entranceRoom", entranceRoom);
        SetObject(controller, "playerRoomTracker", roomTracker);
        SetObject(controller, "gameSession", gameSession);
        SetObject(controller, "dialogueService", dialogueService);
        SetObject(controller, "entranceDialogue", dialogue);

        EditorUtility.SetDirty(controllerObject);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        // 다른 씬이나 작업자의 씬을 저장하지 않고 현재 활성 씬만 저장한다.
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!ValidateConfiguration(out string report))
        {
            throw new InvalidOperationException(report);
        }

        GameLog.Log(report);
    }

    /// <summary>정문 Room·Controller·안내 Dialogue의 참조를 읽기 전용으로 검증한다.</summary>
    [MenuItem("Haunted House/Validate/Hospital Sector5 Entrance")]
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

    /// <summary>현재 씬이 Edit Mode의 CoreSandbox인지 확인한다.</summary>
    private static void EnsureEditModeAndScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Sector5 정문 설정·검증은 Play Mode가 아닌 Edit Mode에서 실행해야 합니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException($"Sector5 정문 설정은 CoreSandbox 씬에서만 실행할 수 있습니다: {ScenePath}");
        }
    }

    /// <summary>정문 도착 Controller와 필수 참조가 계획서 계약을 만족하는지 검사한다.</summary>
    public static bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();
        Room entranceRoom = FindRoom(EntranceRoomId, errors);
        Sector5EntranceController controller = FindController(errors);
        DialogueSequence dialogue = AssetDatabase.LoadAssetAtPath<DialogueSequence>(DialogueAssetPath);

        Check(errors, FindFirst<GameSession>() != null, "GameSession을 찾지 못했습니다.");
        Check(errors, FindFirst<PlayerRoomTracker>() != null, "PlayerRoomTracker를 찾지 못했습니다.");
        Check(errors, FindFirst<DialogueService>() != null, "DialogueService를 찾지 못했습니다.");
        Check(errors, dialogue != null, $"정문 안내 Dialogue asset이 없습니다: {DialogueAssetPath}");

        if (dialogue != null)
        {
            Check(errors, !dialogue.PauseGame, "정문 안내 Dialogue는 PauseGame=false여야 합니다.");
            Check(errors, dialogue.Lines != null && dialogue.Lines.Count > 0,
                "정문 안내 Dialogue에 Line이 없습니다.");
            Check(errors, dialogue.Lines != null && dialogue.Lines.Any(line => line.Text == "[왼쪽 방]"),
                "정문 안내 Dialogue에 '[왼쪽 방]' 문구가 없습니다.");
        }

        if (controller != null)
        {
            SerializedObject serialized = new(controller);
            Check(errors, serialized.FindProperty("entranceRoom")?.objectReferenceValue == entranceRoom,
                "Controller의 Entrance Room 참조가 S5_DoorArea가 아닙니다.");
            Check(errors, serialized.FindProperty("dialogueService")?.objectReferenceValue == FindFirst<DialogueService>(),
                "Controller의 DialogueService 참조가 올바르지 않습니다.");
            Check(errors, serialized.FindProperty("entranceDialogue")?.objectReferenceValue == dialogue,
                "Controller의 정문 안내 Dialogue 참조가 올바르지 않습니다.");
        }

        if (errors.Count > 0)
        {
            report = "Sector5 정문 도착 구성 검증 실패:\n- " + string.Join("\n- ", errors);
            return false;
        }

        report = "Sector5 정문 도착 Controller와 Non-Pause 안내 Dialogue 구성이 유효합니다.";
        return true;
    }

    /// <summary>정문 안내 Dialogue asset을 생성하거나 명세대로 갱신한다.</summary>
    private static DialogueSequence GetOrCreateEntranceDialogue()
    {
        DialogueSequence dialogue = AssetDatabase.LoadAssetAtPath<DialogueSequence>(DialogueAssetPath);
        if (dialogue == null)
        {
            dialogue = ScriptableObject.CreateInstance<DialogueSequence>();
            AssetDatabase.CreateAsset(dialogue, DialogueAssetPath);
        }

        SerializedObject serialized = new(dialogue);
        serialized.FindProperty("pauseGame").boolValue = false;
        serialized.FindProperty("autoAdvanceDelay").floatValue = 2f;
        SerializedProperty lines = serialized.FindProperty("lines");
        if (lines.arraySize == 0)
        {
            lines.InsertArrayElementAtIndex(0);
        }

        SerializedProperty line = lines.GetArrayElementAtIndex(0);
        line.FindPropertyRelative("speaker").enumValueIndex = 0;
        line.FindPropertyRelative("text").stringValue = "[왼쪽 방]";
        while (lines.arraySize > 1)
        {
            lines.DeleteArrayElementAtIndex(lines.arraySize - 1);
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(dialogue);
        return dialogue;
    }

    /// <summary>지정 RoomId의 Room을 찾고 실패 원인을 오류 목록에 기록한다.</summary>
    private static Room FindRoom(string roomId, List<string> errors = null)
    {
        Room room = UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.RoomId == roomId);
        if (room == null)
        {
            errors?.Add($"RoomId '{roomId}'를 찾지 못했습니다.");
        }

        return room;
    }

    /// <summary>필수 정문 Room을 찾고 누락 시 설정을 중단한다.</summary>
    private static Room FindRequiredRoom(string roomId)
    {
        return FindRoom(roomId) ?? throw new InvalidOperationException($"RoomId '{roomId}'를 찾지 못했습니다.");
    }

    /// <summary>현재 씬의 정문 Controller를 찾고 누락 시 오류를 기록한다.</summary>
    private static Sector5EntranceController FindController(List<string> errors = null)
    {
        Sector5EntranceController controller = UnityEngine.Object.FindObjectsByType<Sector5EntranceController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.name == ControllerObjectName);
        if (controller == null)
        {
            errors?.Add($"'{ControllerObjectName}'를 찾지 못했습니다. Setup 메뉴를 먼저 실행하세요.");
        }

        return controller;
    }

    /// <summary>정문 Controller가 붙은 기존 GameObject를 반환한다.</summary>
    private static GameObject FindControllerObject()
    {
        return UnityEngine.Object.FindObjectsByType<Sector5EntranceController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.name == ControllerObjectName)?.gameObject;
    }

    /// <summary>씬에서 지정 타입의 필수 단일 참조를 찾는다.</summary>
    private static T FindRequired<T>(string label) where T : UnityEngine.Object
    {
        return FindFirst<T>() ?? throw new InvalidOperationException($"{label} 참조를 찾지 못했습니다.");
    }

    /// <summary>씬에서 지정 타입의 첫 참조를 찾는다.</summary>
    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    }

    /// <summary>private Unity 참조 필드를 직렬화해 저장한다.</summary>
    private static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        property.objectReferenceValue = value;
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
