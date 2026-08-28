#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Dialogue;
using HauntedHouse.Enemies;
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
/// CoreSandbox의 Sector2 강제 숨기 이벤트에 필요한 씬 오브젝트와 직렬화 참조를 구성한다.
/// 기존 DoorTarget과 Enemy AI를 보존하고 전용 래퍼·Controller·연출 지점만 멱등적으로 추가한다.
/// </summary>
public static class HospitalSector2HideEncounterSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string SetupMenuPath = "Haunted House/Setup/Hospital Sector2 Forced Hide Encounter";
    private const string ValidateMenuPath = "Haunted House/Validate/Hospital Sector2 Forced Hide Encounter";
    private const string EventObjectName = "Sector2HideEncounter";
    private const string PointRootName = "Sector2HideEncounterPoints";
    private const string SpawnPointName = "EnemySpawnPoint";
    private const string RevealPointName = "EnemyRevealPoint";
    private const string SearchPointName = "MachineRoomSearchPoint";
    private const string ExitPointName = "EnemyExitPoint";
    private const string ExitTargetName = "From_A_Target";
    private const string ToolCaseTargetName = "S2_MachineRoom_ToolCase";
    private const string WarningDialoguePath = "Assets/Data/Dialogue/S2_HideWarningDialogue.asset";

    /// <summary>
    /// 기존 MachineRoom 구성 위에 강제 숨기 이벤트의 모든 필수 오브젝트와 참조를 연결한다.
    /// Play 모드가 아닌 CoreSandbox에서만 실행할 수 있다.
    /// </summary>
    [MenuItem(SetupMenuPath)]
    public static void Configure()
    {
        EnsureScene();

        Room machineRoom = FindRoom("S2_MachineRoom");
        Room hallway = FindRoom("S2_Hallway");
        Room enemySpawnRoom = FindRoom("S2_EnemySpawnRoom");
        GameSession gameSession = FindRequired<GameSession>("GameSession");
        Player player = FindRequired<Player>("Player");
        PlayerRoomTracker playerRoomTracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        RoomGraph roomGraph = FindRequired<RoomGraph>("RoomGraph");
        DialogueService dialogueService = FindRequired<DialogueService>("DialogueService");
        StealthHideSystem stealthHideSystem = FindRequired<StealthHideSystem>("StealthHideSystem");
        EnemyController enemy = FindRequired<EnemyController>("EnemyController");
        TargetMarkerPool markerPool = FindRequired<TargetMarkerPool>("TargetMarkerPool");
        Camera worldCamera = Camera.main != null
            ? Camera.main
            : FindRequired<Camera>("World Camera");

        // EnemyController는 Renderer·Collider를 끄는 논리 비활성 방식을 사용하므로
        // 루트 GameObject는 활성 상태여야 NavMeshAgent가 ScriptedSequence 스폰에 바인딩된다.
        enemy.gameObject.SetActive(true);
        SetBool(enemy, "initiallyActive", false);

        Target toolCaseTarget = FindTarget(machineRoom, ToolCaseTargetName);
        Target exitTarget = FindTarget(machineRoom, ExitTargetName);
        if (toolCaseTarget == null || exitTarget == null)
        {
            throw new InvalidOperationException(
                "MachineRoom Target 구성이 없습니다. 먼저 'Hospital Sector2 MachineRoom' 설정을 실행해야 합니다.");
        }

        DialogueSequence warningDialogue = GetOrCreateWarningDialogue();
        Transform pointRoot = GetOrCreateChild(gameSession.transform, PointRootName);
        PatrolPoint spawnPoint = ConfigureSpawnPoint(pointRoot, enemySpawnRoom);
        // EnemyAgent가 씬 시작부터 유효한 NavMesh 근처에서 생성되도록 기준 위치를 맞춘다.
        // 이벤트 전에는 initiallyActive=false로 Renderer·Collider가 숨겨져 화면에는 나타나지 않는다.
        enemy.transform.SetPositionAndRotation(spawnPoint.transform.position, spawnPoint.transform.rotation);
        Transform revealPoint = ConfigurePoint(pointRoot, RevealPointName, hallway, new Vector3(270f, 1.1f, 116.5f));
        Transform searchPoint = ConfigurePoint(pointRoot, SearchPointName, machineRoom, new Vector3(350f, 1.1f, 165f));
        Transform exitPoint = ConfigurePoint(pointRoot, ExitPointName, hallway, new Vector3(343.9f, 1.1f, 116.47f));

        Sector2HideEncounterController controller = GetOrAdd<Sector2HideEncounterController>(
            GetOrCreateChild(gameSession.transform, EventObjectName).gameObject);
        SetObject(controller, "gameSession", gameSession);
        SetObject(controller, "player", player);
        SetObject(controller, "playerRoomTracker", playerRoomTracker);
        SetObject(controller, "roomGraph", roomGraph);
        SetObject(controller, "machineRoom", machineRoom);
        SetObject(controller, "enemy", enemy);
        SetObject(controller, "enemySpawnPoint", spawnPoint);
        SetObject(controller, "enemyRevealRoom", hallway);
        SetObject(controller, "enemyRevealPoint", revealPoint);
        SetObject(controller, "enemySearchRoom", machineRoom);
        SetObject(controller, "enemySearchPoint", searchPoint);
        SetObject(controller, "enemyExitRoom", hallway);
        SetObject(controller, "enemyExitPoint", exitPoint);
        SetObject(controller, "dialogueService", dialogueService);
        SetObject(controller, "warningDialogue", warningDialogue);
        SetObject(controller, "stealthHideSystem", stealthHideSystem);
        SetObject(controller, "toolCaseTarget", toolCaseTarget);
        SetFloat(controller, "searchDuration", 3f);

        MachineRoomExitEncounterTarget wrapper = GetOrAdd<MachineRoomExitEncounterTarget>(exitTarget.gameObject);
        DoorTarget normalDoor = exitTarget.GetComponent<DoorTarget>();
        SetObject(wrapper, "normalExitDoor", normalDoor);
        SetObject(wrapper, "encounterController", controller);
        SetObject(exitTarget, "actionComponent", wrapper);
        exitTarget.SetSelectable(true);

        // ToolCase는 강제 은신 이벤트 전·중·후 모두 사용 가능한 기계실 기본 은신처로 유지한다.
        toolCaseTarget.SetSelectable(true);
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(wrapper);
        EditorUtility.SetDirty(exitTarget);
        EditorUtility.SetDirty(toolCaseTarget);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameLog.Log(
            $"Sector2 강제 숨기 이벤트 구성을 완료했습니다: Controller={controller.name}, Exit={exitTarget.name}.");
    }

    /// <summary>Sector2 강제 숨기 이벤트의 씬 참조·경로·Target 연결을 읽기 전용으로 검사한다.</summary>
    [MenuItem(ValidateMenuPath)]
    public static void Validate()
    {
        EnsureScene();

        List<string> errors = new();
        Room machineRoom = FindRoomOrNull("S2_MachineRoom");
        Room hallway = FindRoomOrNull("S2_Hallway");
        Room enemySpawnRoom = FindRoomOrNull("S2_EnemySpawnRoom");
        Sector2HideEncounterController controller = UnityEngine.Object.FindFirstObjectByType<
            Sector2HideEncounterController>(FindObjectsInactive.Include);
        Check(errors, machineRoom != null, "S2_MachineRoom Room이 없습니다.");
        Check(errors, hallway != null, "S2_Hallway Room이 없습니다.");
        Check(errors, enemySpawnRoom != null, "S2_EnemySpawnRoom Room이 없습니다.");
        Check(errors, controller != null, "Sector2HideEncounterController가 없습니다.");

        if (controller != null)
        {
            Check(errors, controller.TryValidateConfiguration(out string configurationError), configurationError);
        }

        Target exitTarget = machineRoom != null ? FindTarget(machineRoom, ExitTargetName) : null;
        Target toolCaseTarget = machineRoom != null ? FindTarget(machineRoom, ToolCaseTargetName) : null;
        Check(errors, exitTarget != null, $"{ExitTargetName}를 찾지 못했습니다.");
        Check(errors, toolCaseTarget != null, $"{ToolCaseTargetName}를 찾지 못했습니다.");
        Check(errors, exitTarget != null && exitTarget.Action is MachineRoomExitEncounterTarget,
            $"{ExitTargetName}의 행동이 MachineRoomExitEncounterTarget이 아닙니다.");
        Check(errors, toolCaseTarget != null && toolCaseTarget.GetComponent<StealthHideTarget>() != null,
            $"{ToolCaseTargetName}의 행동이 StealthHideTarget이 아닙니다.");

        if (controller != null && controller.TryValidateConfiguration(out _))
        {
            CheckPath(errors, controller, "EnemySpawnRoom → Reveal", enemySpawnRoom, hallway);
            CheckPath(errors, controller, "Reveal → MachineRoom", hallway, machineRoom);
            CheckPath(errors, controller, "MachineRoom → Exit", machineRoom, hallway);
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Sector2 강제 숨기 이벤트 검증 실패:\n- " + string.Join("\n- ", errors));
        }

        GameLog.Log("Sector2 강제 숨기 이벤트 구성이 유효합니다.");
    }

    /// <summary>이벤트 Controller의 RoomGraph 경로를 확인해 세 방 상태가 연결됐는지 검사한다.</summary>
    private static void CheckPath(List<string> errors, Sector2HideEncounterController controller,
        string label, Room source, Room destination)
    {
        RoomGraph graph = FindFirst<RoomGraph>();
        Check(errors, graph != null && graph.TryFindPath(source, destination, out _),
            $"{label} 경로를 RoomGraph에서 찾지 못했습니다.");
    }

    /// <summary>EnemySpawnRoom 안에 적의 ScriptedSequence 시작용 PatrolPoint를 만든다.</summary>
    private static PatrolPoint ConfigureSpawnPoint(Transform parent, Room room)
    {
        Transform pointTransform = GetOrCreateChild(parent, SpawnPointName);
        pointTransform.position = SampleNavMesh(new Vector3(254.4f, 1.1f, 145f), new Vector3(254.4f, 0f, 145f));
        PatrolPoint point = GetOrAdd<PatrolPoint>(pointTransform.gameObject);
        SetObject(point, "room", room);
        EditorUtility.SetDirty(point);
        return point;
    }

    /// <summary>요청한 방 안에 연출용 Transform을 만들고 NavMesh 위치를 저장한다.</summary>
    private static Transform ConfigurePoint(Transform parent, string pointName, Room room, Vector3 requestedPosition)
    {
        Transform point = GetOrCreateChild(parent, pointName);
        point.position = SampleNavMesh(requestedPosition, room != null ? room.GraphPosition : requestedPosition);
        point.rotation = Quaternion.identity;
        EditorUtility.SetDirty(point.gameObject);
        return point;
    }

    /// <summary>강제 숨기 경고 Dialogue asset을 생성하거나 기존 asset을 재사용한다.</summary>
    private static DialogueSequence GetOrCreateWarningDialogue()
    {
        DialogueSequence dialogue = AssetDatabase.LoadAssetAtPath<DialogueSequence>(WarningDialoguePath);
        if (dialogue == null)
        {
            dialogue = ScriptableObject.CreateInstance<DialogueSequence>();
            AssetDatabase.CreateAsset(dialogue, WarningDialoguePath);
        }

        SerializedObject serialized = new(dialogue);
        serialized.FindProperty("pauseGame").boolValue = true;
        serialized.FindProperty("autoAdvanceDelay").floatValue = 0f;
        SerializedProperty lines = serialized.FindProperty("lines");
        if (lines.arraySize == 0)
        {
            lines.InsertArrayElementAtIndex(0);
            lines.InsertArrayElementAtIndex(1);
            SerializedProperty first = lines.GetArrayElementAtIndex(0);
            SerializedProperty second = lines.GetArrayElementAtIndex(1);
            first.FindPropertyRelative("speaker").enumValueIndex = 0;
            first.FindPropertyRelative("text").stringValue = "[빨리 숨어야 한다]";
            second.FindPropertyRelative("speaker").enumValueIndex = 0;
            second.FindPropertyRelative("text").stringValue = "살인마가 돌아다니고 있으니 조심해야 한다";
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(dialogue);
        return dialogue;
    }

    /// <summary>현재 씬과 실행 모드를 검사한다.</summary>
    private static void EnsureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Sector2 강제 숨기 설정은 Play 모드에서 실행할 수 없습니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException($"CoreSandbox 씬에서만 실행할 수 있습니다: {ScenePath}");
        }
    }

    /// <summary>씬에서 지정한 타입의 필수 단일 참조를 찾는다.</summary>
    private static T FindRequired<T>(string label) where T : UnityEngine.Object
    {
        T result = FindFirst<T>();
        if (result == null)
        {
            throw new InvalidOperationException($"{label} 참조를 찾지 못했습니다.");
        }

        return result;
    }

    /// <summary>씬에서 지정한 타입의 첫 참조를 찾는다.</summary>
    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    }

    /// <summary>RoomId로 Room을 찾는다.</summary>
    private static Room FindRoom(string roomId)
    {
        return FindRoomOrNull(roomId) ?? throw new InvalidOperationException($"Room '{roomId}'가 없습니다.");
    }

    /// <summary>RoomId로 Room을 찾되 없으면 null을 반환한다.</summary>
    private static Room FindRoomOrNull(string roomId)
    {
        return UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => string.Equals(room.RoomId, roomId, StringComparison.Ordinal));
    }

    /// <summary>Room 자식 Target 목록에서 이름이 일치하는 Target을 찾는다.</summary>
    private static Target FindTarget(Room room, string targetName)
    {
        return room == null
            ? null
            : room.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => string.Equals(target.name, targetName, StringComparison.Ordinal));
    }

    /// <summary>부모 아래에 지정한 이름의 자식을 만들거나 기존 자식을 재사용한다.</summary>
    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new(childName);
        childObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(childObject, "Create Sector2 hide encounter object");
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

    /// <summary>컴포넌트의 private float 필드를 직렬화해 저장한다.</summary>
    private static void SetFloat(UnityEngine.Object target, string propertyName, float value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        }

        property.floatValue = value;
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

    /// <summary>요청 위치 주변에서 가장 가까운 NavMesh 위치를 선택한다.</summary>
    private static Vector3 SampleNavMesh(Vector3 requested, Vector3 fallback)
    {
        if (NavMesh.SamplePosition(requested, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        if (NavMesh.SamplePosition(fallback, out hit, 30f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return requested;
    }

    /// <summary>검증 오류 목록에 조건 실패를 추가한다.</summary>
    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition && !string.IsNullOrWhiteSpace(message))
        {
            errors.Add(message);
        }
    }
}
#endif
