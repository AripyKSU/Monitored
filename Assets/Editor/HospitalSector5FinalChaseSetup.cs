#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Dialogue;
using HauntedHouse.Enemies;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 S4_Stairwell 도착 Target에 최종 추격 시작 컨트롤러를 연결한다.
/// 기존 문·Target·Enemy를 재사용하고 Sector5 전용 Controller와 Dialogue 참조만 멱등적으로 구성한다.
/// </summary>
public static class HospitalSector5FinalChaseSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string ControllerObjectName = "Sector5FinalChaseController";
    private const string BasementRoomId = "S4_BasementCorridor";
    private const string StairwellRoomId = "S4_Stairwell";
    private const string StorageRoomId = "S4_Storage";
    private const string StairDoorName = "SlideDoor_BasementStair";
    private const string SpawnPointName = "Patrol_S4_Storage";
    private const string DialogueAssetPath = "Assets/Data/Dialogue/S5_RunDialogue.asset";

    /// <summary>현재 열린 CoreSandbox에 최종 추격 시작 Controller를 멱등적으로 구성한다.</summary>
    [MenuItem("Haunted House/Setup/Hospital Sector5 Final Chase")]
    public static void Configure()
    {
        EnsureEditModeAndScene();

        Room basementRoom = FindRequiredRoom(BasementRoomId);
        Room stairwellRoom = FindRequiredRoom(StairwellRoomId);
        Room storageRoom = FindRequiredRoom(StorageRoomId);
        Target stairwellTarget = FindStairwellArrivalTarget(basementRoom, stairwellRoom);
        EnemyController enemy = FindRequiredEnemy();
        PatrolPoint spawnPoint = FindRequiredSpawnPoint(storageRoom);
        GameSession gameSession = FindRequired<GameSession>("GameSession");
        Player player = FindRequired<Player>("Player");
        PlayerRoomTracker roomTracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        TargetCommandRunner commandRunner = FindRequired<TargetCommandRunner>("TargetCommandRunner");
        HauntedHouse.Core.GamePauseService pauseService =
            FindRequired<HauntedHouse.Core.GamePauseService>("GamePauseService");
        DialogueService dialogueService = FindRequired<DialogueService>("DialogueService");
        DialogueSequence runDialogue = GetOrCreateRunDialogue();

        GameObject controllerObject = FindControllerObject();
        if (controllerObject == null)
        {
            controllerObject = new GameObject(ControllerObjectName);
            Undo.RegisterCreatedObjectUndo(controllerObject, "Create Sector5 final chase controller");
            SceneManager.MoveGameObjectToScene(controllerObject, SceneManager.GetActiveScene());
        }

        Sector5FinalChaseController controller = controllerObject.GetComponent<Sector5FinalChaseController>() ??
                                                  Undo.AddComponent<Sector5FinalChaseController>(controllerObject);
        SetObject(controller, "gameSession", gameSession);
        SetObject(controller, "player", player);
        SetObject(controller, "playerRoomTracker", roomTracker);
        SetObject(controller, "targetCommandRunner", commandRunner);
        SetObject(controller, "pauseService", pauseService);
        SetObject(controller, "dialogueService", dialogueService);
        SetObject(controller, "stairwellArrivalTarget", stairwellTarget);
        SetObject(controller, "stairwellRoom", stairwellRoom);
        SetObject(controller, "enemy", enemy);
        SetObject(controller, "enemySpawnPoint", spawnPoint);
        SetObject(controller, "runDialogue", runDialogue);

        EditorUtility.SetDirty(controllerObject);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        // 다른 작업자가 열어 둔 씬까지 저장하지 않도록 현재 활성 씬만 저장한다.
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!ValidateConfiguration(out string report))
        {
            throw new InvalidOperationException(report);
        }

        GameLog.Log(report, controller);
    }

    /// <summary>
    /// Unity batch 실행에서 CoreSandbox를 먼저 열고 Setup을 적용한다.
    /// 공용 Editor를 점유하지 않는 단독 검증 환경에서만 호출하도록 한다.
    /// </summary>
    public static void ConfigureBatch()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        Configure();
    }

    /// <summary>최종 추격 시작 Controller와 도착·등장·Dialogue 참조를 읽기 전용으로 검사한다.</summary>
    [MenuItem("Haunted House/Validate/Hospital Sector5 Final Chase")]
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
            throw new InvalidOperationException("Sector5 최종 추격 설정·검증은 Play Mode가 아닌 Edit Mode에서 실행해야 합니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException($"Sector5 최종 추격 설정은 CoreSandbox 씬에서만 실행할 수 있습니다: {ScenePath}");
        }
    }

    /// <summary>현재 씬의 Sector5 시작 구성이 계획서 계약을 만족하는지 검사한다.</summary>
    public static bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();
        Room basementRoom = FindRoom(BasementRoomId, errors);
        Room stairwellRoom = FindRoom(StairwellRoomId, errors);
        Room storageRoom = FindRoom(StorageRoomId, errors);
        Target stairwellTarget = FindStairwellArrivalTarget(basementRoom, stairwellRoom, errors);
        EnemyController enemy = FindEnemy(errors);
        PatrolPoint spawnPoint = FindSpawnPoint(storageRoom, errors);
        DialogueSequence dialogue = AssetDatabase.LoadAssetAtPath<DialogueSequence>(DialogueAssetPath);
        Sector5FinalChaseController controller = FindController(errors);

        Check(errors, FindFirst<GameSession>() != null, "GameSession을 찾지 못했습니다.");
        Check(errors, FindFirst<Player>() != null, "Player를 찾지 못했습니다.");
        Check(errors, FindFirst<PlayerRoomTracker>() != null, "PlayerRoomTracker를 찾지 못했습니다.");
        Check(errors, FindFirst<TargetCommandRunner>() != null, "TargetCommandRunner를 찾지 못했습니다.");
        Check(errors, FindFirst<HauntedHouse.Core.GamePauseService>() != null, "GamePauseService를 찾지 못했습니다.");
        Check(errors, FindFirst<DialogueService>() != null, "DialogueService를 찾지 못했습니다.");
        Check(errors, dialogue != null, $"최종 추격 시작 Dialogue asset이 없습니다: {DialogueAssetPath}");

        if (dialogue != null)
        {
            Check(errors, dialogue.PauseGame, "최종 추격 시작 Dialogue는 PauseGame이어야 합니다.");
            Check(errors, dialogue.Lines != null && dialogue.Lines.Count > 0,
                "최종 추격 시작 Dialogue에 Line이 없습니다.");
        }

        if (controller != null)
        {
            Check(errors, controller.TryValidateConfiguration(out string controllerError), controllerError);

            SerializedObject serialized = new(controller);
            Check(errors, serialized.FindProperty("stairwellArrivalTarget")?.objectReferenceValue == stairwellTarget,
                "Controller의 Stairwell 도착 Target 참조가 올바르지 않습니다.");
            Check(errors, serialized.FindProperty("stairwellRoom")?.objectReferenceValue == stairwellRoom,
                "Controller의 Stairwell Room 참조가 올바르지 않습니다.");
            Check(errors, serialized.FindProperty("enemy")?.objectReferenceValue == enemy,
                "Controller의 Enemy 참조가 올바르지 않습니다.");
            Check(errors, serialized.FindProperty("enemySpawnPoint")?.objectReferenceValue == spawnPoint,
                "Controller의 Enemy SpawnPoint 참조가 올바르지 않습니다.");
            Check(errors, serialized.FindProperty("runDialogue")?.objectReferenceValue == dialogue,
                "Controller의 시작 Dialogue 참조가 올바르지 않습니다.");
        }

        if (errors.Count > 0)
        {
            report = "Sector5 최종 추격 시작 구성 검증 실패:\n- " + string.Join("\n- ", errors);
            return false;
        }

        report = "Sector5 최종 추격 시작 Controller, 지하 상행 Stairwell 도착 Target, Storage 등장 지점과 Dialogue 구성이 유효합니다.";
        return true;
    }

    /// <summary>시작 Dialogue asset을 생성하거나 기존 asset을 명세대로 갱신한다.</summary>
    private static DialogueSequence GetOrCreateRunDialogue()
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
        if (lines.arraySize == 0)
        {
            lines.InsertArrayElementAtIndex(0);
        }

        SerializedProperty line = lines.GetArrayElementAtIndex(0);
        line.FindPropertyRelative("speaker").enumValueIndex = 0;
        line.FindPropertyRelative("text").stringValue = "[도망쳐]";
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(dialogue);
        return dialogue;
    }

    /// <summary>계단 도착 방향의 Target을 Doorway 통과 목적지로 식별한다.</summary>
    private static Target FindStairwellArrivalTarget(
        Room basementRoom, Room stairwellRoom, List<string> errors = null)
    {
        Doorway[] doorwayCandidates = UnityEngine.Object.FindObjectsByType<Doorway>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(doorway => doorway != null && doorway.name == StairDoorName &&
                             ((doorway.RoomA == basementRoom && doorway.RoomB == stairwellRoom) ||
                              (doorway.RoomA == stairwellRoom && doorway.RoomB == basementRoom)))
            .ToArray();

        if (doorwayCandidates.Length != 1)
        {
            errors?.Add(doorwayCandidates.Length == 0
                ? $"'{StairDoorName}' Doorway가 {BasementRoomId} ↔ {StairwellRoomId} 연결로 구성되지 않았습니다."
                : $"'{StairDoorName}' Doorway 후보가 여러 개입니다.");
            return null;
        }

        Doorway stairDoorway = doorwayCandidates[0];
        List<Target> candidates = stairDoorway.GetComponentsInChildren<DoorTarget>(true)
            .Select(doorTarget => doorTarget != null ? doorTarget.GetComponent<Target>() : null)
            .Where(target => target != null && target.OwnerRoom == basementRoom &&
                             stairDoorway.TryGetTraversal(target.OwnerRoom, out DoorTraversal traversal) &&
                             traversal.DestinationRoom == stairwellRoom)
            .ToList();

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (candidates.Count > 1)
        {
            errors?.Add("S4_Stairwell 도착 Target 후보가 여러 개입니다: " +
                        string.Join(", ", candidates.Select(candidate => candidate.name)));
        }
        else
        {
            errors?.Add("S4_Stairwell로 통과하는 도착 Target을 찾지 못했습니다.");
        }

        return null;
    }

    /// <summary>Sector4 Storage PatrolPoint를 이름과 Room으로 식별한다.</summary>
    private static PatrolPoint FindSpawnPoint(Room storageRoom, List<string> errors = null)
    {
        PatrolPoint[] points = UnityEngine.Object.FindObjectsByType<PatrolPoint>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        PatrolPoint point = points.FirstOrDefault(candidate => candidate != null &&
                                                               candidate.name == SpawnPointName &&
                                                               candidate.Room == storageRoom) ??
                            points.FirstOrDefault(candidate => candidate != null && candidate.Room == storageRoom);
        if (point == null)
        {
            errors?.Add($"{StorageRoomId}의 최종 추격 등장 PatrolPoint를 찾지 못했습니다.");
        }

        return point;
    }

    /// <summary>씬의 살인마 EnemyController를 이름 우선 규칙으로 찾는다.</summary>
    private static EnemyController FindEnemy(List<string> errors = null)
    {
        EnemyController[] enemies = UnityEngine.Object.FindObjectsByType<EnemyController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        EnemyController enemy = enemies.FirstOrDefault(candidate => candidate != null && candidate.name == "Enemy") ??
                                enemies.FirstOrDefault();
        if (enemy == null)
        {
            errors?.Add("EnemyController를 찾지 못했습니다.");
        }

        return enemy;
    }

    /// <summary>현재 Controller가 없으면 오류를 기록하고 반환한다.</summary>
    private static Sector5FinalChaseController FindController(List<string> errors = null)
    {
        Sector5FinalChaseController controller = UnityEngine.Object.FindObjectsByType<Sector5FinalChaseController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.name == ControllerObjectName);
        if (controller == null)
        {
            errors?.Add($"'{ControllerObjectName}'를 찾지 못했습니다. Setup 메뉴를 먼저 실행하세요.");
        }

        return controller;
    }

    /// <summary>Controller가 붙은 기존 GameObject를 반환한다.</summary>
    private static GameObject FindControllerObject()
    {
        return UnityEngine.Object.FindObjectsByType<Sector5FinalChaseController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.name == ControllerObjectName)?.gameObject;
    }

    /// <summary>RoomId로 Room을 찾고 누락 시 오류를 기록한다.</summary>
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

    /// <summary>필수 Room을 찾고 누락 시 설정을 중단한다.</summary>
    private static Room FindRequiredRoom(string roomId)
    {
        return FindRoom(roomId) ?? throw new InvalidOperationException($"RoomId '{roomId}'를 찾지 못했습니다.");
    }

    /// <summary>필수 살인마를 찾고 누락 시 설정을 중단한다.</summary>
    private static EnemyController FindRequiredEnemy()
    {
        return FindEnemy() ?? throw new InvalidOperationException("Sector5 최종 추격 EnemyController를 찾지 못했습니다.");
    }

    /// <summary>필수 등장 지점을 찾고 누락 시 설정을 중단한다.</summary>
    private static PatrolPoint FindRequiredSpawnPoint(Room storageRoom)
    {
        return FindSpawnPoint(storageRoom) ??
               throw new InvalidOperationException("Sector5 최종 추격 Storage 등장 PatrolPoint를 찾지 못했습니다.");
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
