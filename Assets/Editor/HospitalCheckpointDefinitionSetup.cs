#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 병원 맵의 중앙 복도 논리 분할과 Save1~Save5 체크포인트 기준 데이터를 반복 실행 가능하게 구성한다.
/// 실제 스토리 Target과 적 Patrol은 각 Sector 구현 단계에서 연결하며 이 도구는 복원 기준만 소유한다.
/// </summary>
public static class HospitalCheckpointDefinitionSetup
{
    private const string MenuPath = "Haunted House/Setup/Hospital Checkpoint Definitions";
    private const float HallBoundaryZ = 240f;

    /// <summary>현재 열린 CoreSandbox 씬에 중앙 복도 A/B와 다섯 체크포인트를 생성하고 검증한다.</summary>
    [MenuItem(MenuPath)]
    public static void Configure()
    {
        if (SceneManager.GetActiveScene().path != "Assets/Scenes/CoreSandbox.unity")
        {
            throw new InvalidOperationException("Hospital Checkpoint 설정은 CoreSandbox 씬에서만 실행할 수 있습니다.");
        }

        Room centerA = FindRoom("CenterHallway", "CenterHallway_A");
        centerA.name = "CenterHallway_A";
        SetString(centerA, "roomId", "CenterHallway_A");

        Room centerB = GetOrCreateCenterHallwayB(centerA);
        SplitCenterHallwayConnections(centerA, centerB);
        Doorway logicalGate = GetOrCreateLogicalGate(centerA, centerB);
        ConfigureCenterHallwayCameras(centerA, centerB);
        ConfigureAdjacency(centerA, centerB, logicalGate);

        RoomGraph graph = UnityEngine.Object.FindFirstObjectByType<RoomGraph>();
        if (graph == null)
        {
            throw new InvalidOperationException("RoomGraph를 찾지 못했습니다.");
        }

        if (!graph.BakeFromScene(out string graphError))
        {
            throw new InvalidOperationException($"중앙 복도 분할 후 RoomGraph Bake에 실패했습니다: {graphError}");
        }

        ItemDefinition flashlight = GetOrCreateItem("Flashlight", "flashlight", "손전등", "어두운 병실을 밝히는 손전등입니다.");
        ItemDefinition wardKey = GetOrCreateItem("WardHallKey", "ward_hall_key", "병실 복도 열쇠", "병실 구역과 중앙 복도를 잇는 열쇠입니다.");
        ItemDefinition basementKey = GetOrCreateItem("BasementKey", "basement_key", "지하 통로 열쇠", "지하계단으로 이어지는 통로 열쇠입니다.");
        ItemDefinition masterKey = GetOrCreateItem("MasterKey", "master_key", "마스터키", "병원 정문을 열 수 있는 마스터키입니다.");

        CheckpointDefinition[] saves = ConfigureCheckpoints(
            centerA, centerB, logicalGate, flashlight, wardKey, basementKey, masterKey, graph);
        ConfigureCheckpointService(saves[0]);
        ConfigureResolver(saves);
        ConfigureInitialEnemyState();

        EditorUtility.SetDirty(graph);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        GameLog.Log("병원 중앙 복도 A/B와 Save1~Save5 체크포인트 정의 구성을 완료했습니다.", centerA);
    }

    /// <summary>
    /// Save1 복원 코루틴이 시작되기 전 Enemy의 NavMeshAgent가 잘못 초기화되지 않도록 씬 기본 상태를 비활성으로 둔다.
    /// 이후 Save3/Save4의 Patrol Route가 연결되면 CheckpointEnemyState가 필요한 시점에 다시 활성화한다.
    /// </summary>
    private static void ConfigureInitialEnemyState()
    {
        foreach (EnemyController enemy in UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            enemy.gameObject.SetActive(false);
            EditorUtility.SetDirty(enemy.gameObject);
        }
    }

    /// <summary>기존 중앙 복도를 찾고 이미 분할된 경우 A를 반환한다.</summary>
    private static Room FindRoom(params string[] roomIds)
    {
        Room room = UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => roomIds.Contains(candidate.RoomId, StringComparer.Ordinal));
        return room != null ? room : throw new InvalidOperationException($"Room을 찾지 못했습니다: {string.Join(", ", roomIds)}");
    }

    /// <summary>CenterHallway_A와 같은 계층에 B 논리 방을 생성한다.</summary>
    private static Room GetOrCreateCenterHallwayB(Room centerA)
    {
        Room existing = UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room.RoomId == "CenterHallway_B");
        if (existing != null)
        {
            return existing;
        }

        var roomObject = new GameObject("CenterHallway_B");
        roomObject.transform.SetParent(centerA.transform.parent, false);
        roomObject.transform.SetPositionAndRotation(centerA.transform.position, centerA.transform.rotation);
        Room room = roomObject.AddComponent<Room>();
        SetString(room, "roomId", "CenterHallway_B");
        return room;
    }

    /// <summary>경계 북쪽 Doorway와 이동 Target을 CenterHallway_B 소유로 변경한다.</summary>
    private static void SplitCenterHallwayConnections(Room centerA, Room centerB)
    {
        foreach (Doorway doorway in UnityEngine.Object.FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (doorway.name == "LogicalGate_CenterHallway_A_B")
            {
                continue;
            }

            SerializedObject serializedDoorway = new SerializedObject(doorway);
            bool changed = false;
            float boundaryPositionZ = GetDoorwayBoundaryZ(doorway);
            if (boundaryPositionZ > HallBoundaryZ && doorway.RoomA == centerA)
            {
                serializedDoorway.FindProperty("roomA").objectReferenceValue = centerB;
                changed = true;
            }
            if (boundaryPositionZ > HallBoundaryZ && doorway.RoomB == centerA)
            {
                serializedDoorway.FindProperty("roomB").objectReferenceValue = centerB;
                changed = true;
            }
            if (changed)
            {
                serializedDoorway.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        foreach (Target target in UnityEngine.Object.FindObjectsByType<Target>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Doorway parentDoorway = target.GetComponentInParent<Doorway>();
            if (parentDoorway != null && parentDoorway.name == "LogicalGate_CenterHallway_A_B")
            {
                continue;
            }

            if (target.OwnerRoom == centerA && target.ApproachPoint != null && target.ApproachPoint.position.z > HallBoundaryZ)
            {
                SetObject(target, "ownerRoom", centerB);
            }
        }
    }

    /// <summary>
    /// Doorway 루트가 모델 원점 오프셋을 가질 수 있으므로 실제 방향 Target 접근점에서 복도 경계 Z를 계산한다.
    /// </summary>
    private static float GetDoorwayBoundaryZ(Doorway doorway)
    {
        Target[] targets = doorway.GetComponentsInChildren<Target>(true);
        Transform approach = targets.Select(target => target.ApproachPoint).FirstOrDefault(point => point != null);
        return approach != null ? approach.position.z : doorway.transform.position.z;
    }

    /// <summary>기존 양방향 문 구성을 복제해 A/B 경계의 논리 철창 Doorway를 만든다.</summary>
    private static Doorway GetOrCreateLogicalGate(Room centerA, Room centerB)
    {
        Doorway existing = UnityEngine.Object.FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(doorway => doorway.name == "LogicalGate_CenterHallway_A_B");
        Doorway gate = existing;
        if (gate == null)
        {
            Doorway template = UnityEngine.Object.FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .First(doorway => doorway.name == "SlideDoor_To_Sector3Hallway");
            gate = UnityEngine.Object.Instantiate(template, centerA.transform.parent);
            gate.name = "LogicalGate_CenterHallway_A_B";

            Target placementTarget = gate.GetComponentsInChildren<Target>(true).First(target => target.name == "From_A_Target");
            Vector3 targetCenter = placementTarget.transform.position;
            Vector3 desiredCenter = new Vector3(191.8f, targetCenter.y, HallBoundaryZ);
            gate.transform.position += desiredCenter - targetCenter;
            gate.transform.RotateAround(desiredCenter, Vector3.up, 90f);
        }

        Target fromA = gate.GetComponentsInChildren<Target>(true).First(target => target.name == "From_A_Target");
        Target fromB = gate.GetComponentsInChildren<Target>(true).First(target => target.name == "From_B_Target");

        SerializedObject serializedGate = new SerializedObject(gate);
        // 복제 원본을 90도 돌리면 From_A가 북쪽, From_B가 남쪽을 향하므로 Room 순서를 이에 맞춘다.
        serializedGate.FindProperty("roomA").objectReferenceValue = centerB;
        serializedGate.FindProperty("roomB").objectReferenceValue = centerA;
        serializedGate.FindProperty("locked").boolValue = true;
        serializedGate.ApplyModifiedPropertiesWithoutUndo();

        SetObject(fromA, "ownerRoom", centerB);
        SetObject(fromB, "ownerRoom", centerA);
        return gate;
    }

    /// <summary>A의 기존 CCTV를 명확히 이름 붙이고 복제본을 B의 기본 CCTV로 연결한다.</summary>
    private static void ConfigureCenterHallwayCameras(Room centerA, Room centerB)
    {
        RoomCamera cameraA = centerA.RoomCamera ?? centerA.GetComponentInChildren<RoomCamera>(true);
        if (cameraA == null)
        {
            throw new InvalidOperationException("CenterHallway_A의 기존 CCTV를 찾지 못했습니다.");
        }

        cameraA.name = "CCTV_CenterHallway_A";
        SetObject(centerA, "roomCamera", cameraA);
        SetObject(centerA, "graphPositionSource", cameraA.transform);

        RoomCamera cameraB = centerB.GetComponentInChildren<RoomCamera>(true);
        if (cameraB == null)
        {
            cameraB = UnityEngine.Object.Instantiate(cameraA, centerB.transform);
            cameraB.name = "CCTV_CenterHallway_B";
            cameraB.transform.position = new Vector3(cameraA.transform.position.x, cameraA.transform.position.y, 360f);
        }

        SetObject(centerB, "roomCamera", cameraB);
        SetObject(centerB, "graphPositionSource", cameraB.transform);
    }

    /// <summary>기존 인접 관계를 남쪽 A와 북쪽 B로 나누고 서로를 직접 연결한다.</summary>
    private static void ConfigureAdjacency(Room centerA, Room centerB, Doorway gate)
    {
        var south = new List<Room>();
        var north = new List<Room>();
        foreach (Doorway doorway in UnityEngine.Object.FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (doorway == gate)
            {
                continue;
            }

            if (doorway.RoomA == centerA) south.Add(doorway.RoomB);
            if (doorway.RoomB == centerA) south.Add(doorway.RoomA);
            if (doorway.RoomA == centerB) north.Add(doorway.RoomB);
            if (doorway.RoomB == centerB) north.Add(doorway.RoomA);
        }

        south.Add(centerB);
        north.Add(centerA);
        SetRoomArray(centerA, south);
        SetRoomArray(centerB, north);

        foreach (Room neighbor in north.Where(room => room != centerA))
        {
            SetRoomArray(neighbor, neighbor.AdjacentRooms.Select(room => room == centerA ? centerB : room));
        }
    }

    /// <summary>계획 문서의 누적 진행 상태를 가진 Save1~Save5 정의를 생성한다.</summary>
    private static CheckpointDefinition[] ConfigureCheckpoints(
        Room centerA, Room centerB, Doorway gate, ItemDefinition flashlight, ItemDefinition wardKey,
        ItemDefinition basementKey, ItemDefinition masterKey, RoomGraph graph)
    {
        GameSession session = UnityEngine.Object.FindFirstObjectByType<GameSession>();
        if (session == null)
        {
            throw new InvalidOperationException("GameSession을 찾지 못했습니다.");
        }

        Transform root = GetOrCreateChild(session.transform, "HospitalCheckpoints");
        Room start = FindRoom("S1_StartBedroom");
        Room basement = FindRoom("S4_BasementCorridor");
        Room morgue2 = FindRoom("S4_MorgueRoom2");

        CheckpointDefinition save1 = ConfigureCheckpoint(root, "Save1", start,
            GetSpawn(start, "Save1_StartBedroomPoint", FindExistingInitialPoint(start, new Vector3(10f, 1f, 195f))),
            StoryStage.Sector1, Array.Empty<ItemDefinition>(), Array.Empty<string>(), graph, gate, true);

        string[] save2Flags =
        {
            StoryProgressFlags.IntroDialogueCompleted, StoryProgressFlags.StartHallDoorScareSeen,
            StoryProgressFlags.FlashlightAcquired, StoryProgressFlags.NursingRoomFlashlightActivated,
            StoryProgressFlags.WardHallKeyAcquired, StoryProgressFlags.CenterHallFirstEntered
        };
        CheckpointDefinition save2 = ConfigureCheckpoint(root, "Save2", centerA,
            GetSpawn(centerA, "Save2_CenterHallway_A_Point", new Vector3(191.8f, 1f, 125f)),
            StoryStage.Sector2, new[] { flashlight, wardKey }, save2Flags, graph, gate, true);

        string[] save3Flags = save2Flags.Concat(new[]
        {
            StoryProgressFlags.FuseInstalled, StoryProgressFlags.GeneratorPowered,
            StoryProgressFlags.GateActivated, StoryProgressFlags.Sector2HideEncounterCompleted,
            StoryProgressFlags.CenterHallSecondEntered
        }).ToArray();
        CheckpointDefinition save3 = ConfigureCheckpoint(root, "Save3", centerB,
            GetSpawn(centerB, "Save3_CenterHallway_B_Point", new Vector3(191.8f, 1f, 265f)),
            StoryStage.Sector3, new[] { flashlight, wardKey }, save3Flags, graph, gate, false);

        string[] save4Flags = save3Flags.Concat(new[]
        {
            StoryProgressFlags.ExitDoorHintShown, StoryProgressFlags.FreezerHintRead,
            StoryProgressFlags.BasementKeyAcquired, StoryProgressFlags.BasementEntered
        }).ToArray();
        CheckpointDefinition save4 = ConfigureCheckpoint(root, "Save4", basement,
            GetSpawn(basement, "Save4_BasementCorridor_Point", new Vector3(347.8f, -54f, 311.2f)),
            StoryStage.Sector4, new[] { flashlight, wardKey, basementKey }, save4Flags, graph, gate, false);

        string[] save5Flags = save4Flags.Concat(new[]
        {
            StoryProgressFlags.KillerDiary3Read, StoryProgressFlags.MasterKeyAcquired
        }).ToArray();
        CheckpointDefinition save5 = ConfigureCheckpoint(root, "Save5", morgue2,
            GetSpawn(morgue2, "Save5_MorgueRoom2_MasterKeyPoint", new Vector3(270f, -54f, 384.5f)),
            StoryStage.Sector5Ready, new[] { flashlight, wardKey, basementKey, masterKey }, save5Flags, graph, gate, false);

        return new[] { save1, save2, save3, save4, save5 };
    }

    /// <summary>체크포인트 오브젝트의 공통 필드를 직렬화한다.</summary>
    private static CheckpointDefinition ConfigureCheckpoint(
        Transform root, string id, Room room, Transform point, StoryStage stage, ItemDefinition[] items,
        string[] flags, RoomGraph graph, Doorway gate, bool gateLocked)
    {
        Transform child = GetOrCreateChild(root, id);
        CheckpointDefinition checkpoint = child.GetComponent<CheckpointDefinition>() ?? child.gameObject.AddComponent<CheckpointDefinition>();
        SerializedObject serialized = new SerializedObject(checkpoint);
        serialized.FindProperty("checkpointId").stringValue = id;
        serialized.FindProperty("respawnRoom").objectReferenceValue = room;
        serialized.FindProperty("respawnPoint").objectReferenceValue = point;
        serialized.FindProperty("storyStage").enumValueIndex = (int)stage;
        serialized.FindProperty("roomGraph").objectReferenceValue = graph;
        SetArray(serialized.FindProperty("inventoryItems"), items.Cast<UnityEngine.Object>());
        SetStrings(serialized.FindProperty("progressFlags"), flags);
        SetArray(serialized.FindProperty("completedStates"), Array.Empty<UnityEngine.Object>());
        SetArray(serialized.FindProperty("enemyStates"), Array.Empty<UnityEngine.Object>());
        SerializedProperty doors = serialized.FindProperty("doorwayStates");
        doors.arraySize = 1;
        doors.GetArrayElementAtIndex(0).FindPropertyRelative("doorway").objectReferenceValue = gate;
        doors.GetArrayElementAtIndex(0).FindPropertyRelative("locked").boolValue = gateLocked;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return checkpoint;
    }

    /// <summary>Save1을 초기 체크포인트로 지정한다.</summary>
    private static void ConfigureCheckpointService(CheckpointDefinition save1)
    {
        CheckpointService service = UnityEngine.Object.FindFirstObjectByType<CheckpointService>();
        if (service == null) throw new InvalidOperationException("CheckpointService를 찾지 못했습니다.");
        SetObject(service, "initialCheckpoint", save1);
    }

    /// <summary>Room 진입 및 마스터키 대화 완료 사건을 각 체크포인트 활성화 규칙에 연결한다.</summary>
    private static void ConfigureResolver(CheckpointDefinition[] saves)
    {
        CheckpointResolver resolver = UnityEngine.Object.FindFirstObjectByType<CheckpointResolver>();
        if (resolver == null) throw new InvalidOperationException("CheckpointResolver를 찾지 못했습니다.");

        SerializedObject serialized = new SerializedObject(resolver);
        SerializedProperty rules = serialized.FindProperty("rules");
        rules.arraySize = 4;
        ConfigureRule(rules.GetArrayElementAtIndex(0), StoryEventType.RoomEntered, "CenterHallway_A", StoryProgressFlags.WardHallKeyAcquired, StoryProgressFlags.CenterHallFirstEntered, saves[1]);
        ConfigureRule(rules.GetArrayElementAtIndex(1), StoryEventType.RoomEntered, "CenterHallway_B", StoryProgressFlags.Sector2HideEncounterCompleted, StoryProgressFlags.CenterHallSecondEntered, saves[2]);
        ConfigureRule(rules.GetArrayElementAtIndex(2), StoryEventType.RoomEntered, "S4_BasementCorridor", StoryProgressFlags.BasementKeyAcquired, StoryProgressFlags.BasementEntered, saves[3]);
        ConfigureRule(rules.GetArrayElementAtIndex(3), StoryEventType.PuzzleCompleted, StoryCheckpointEventIds.MasterKeyDialogueCompleted, StoryProgressFlags.MasterKeyAcquired, string.Empty, saves[4]);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>단일 StoryCheckpointRule의 사건 조건과 결과를 설정한다.</summary>
    private static void ConfigureRule(SerializedProperty rule, StoryEventType type, string subject, string required, string flag, CheckpointDefinition checkpoint)
    {
        rule.FindPropertyRelative("eventType").enumValueIndex = (int)type;
        rule.FindPropertyRelative("subjectId").stringValue = subject;
        rule.FindPropertyRelative("requiredFlagId").stringValue = required;
        rule.FindPropertyRelative("flagId").stringValue = flag;
        rule.FindPropertyRelative("checkpoint").objectReferenceValue = checkpoint;
    }

    /// <summary>스토리 아이템 정의 에셋을 생성하거나 기존 에셋을 갱신한다.</summary>
    private static ItemDefinition GetOrCreateItem(string fileName, string itemId, string displayName, string description)
    {
        const string folder = "Assets/Data/Items";
        if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Data", "Items");
        string path = $"{folder}/{fileName}.asset";
        ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(item, path);
        }

        SerializedObject serialized = new SerializedObject(item);
        serialized.FindProperty("itemId").stringValue = itemId;
        serialized.FindProperty("displayName").stringValue = displayName;
        serialized.FindProperty("description").stringValue = description;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    /// <summary>요청 위치 주변 NavMesh의 안전 지점에 Room 자식 Spawn Transform을 둔다.</summary>
    private static Transform GetSpawn(Room room, string name, Vector3 requested)
    {
        Transform point = room.transform.Find(name) ?? new GameObject(name).transform;
        point.SetParent(room.transform, true);
        Vector3 position = NavMesh.SamplePosition(requested, out NavMeshHit hit, 20f, NavMesh.AllAreas) ? hit.position : requested;
        point.SetPositionAndRotation(position, Quaternion.identity);
        return point;
    }

    /// <summary>기존 초기 체크포인트 위치가 StartBedroom에 있으면 그 위치를 재사용한다.</summary>
    private static Vector3 FindExistingInitialPoint(Room startRoom, Vector3 fallback)
    {
        CheckpointDefinition initial = UnityEngine.Object.FindObjectsByType<CheckpointDefinition>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(checkpoint => checkpoint.RespawnRoom == startRoom && checkpoint.RespawnPoint != null);
        return initial != null ? initial.RespawnPoint.position : fallback;
    }

    /// <summary>직렬화된 Room 배열을 중복 없이 설정한다.</summary>
    private static void SetRoomArray(Room room, IEnumerable<Room> values)
    {
        SerializedObject serialized = new SerializedObject(room);
        SetArray(serialized.FindProperty("adjacentRooms"), values.Where(value => value != null).Distinct().Cast<UnityEngine.Object>());
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>지정 이름의 자식 Transform을 반환하거나 새로 만든다.</summary>
    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        var childObject = new GameObject(name);
        childObject.transform.SetParent(parent, false);
        return childObject.transform;
    }

    /// <summary>대상 컴포넌트의 단일 직렬화 필드를 설정한다.</summary>
    private static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>대상 컴포넌트의 문자열 직렬화 필드를 설정한다.</summary>
    private static void SetString(UnityEngine.Object target, string propertyName, string value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>문자열 직렬화 배열을 설정한다.</summary>
    private static void SetStrings(SerializedProperty property, IReadOnlyList<string> values)
    {
        property.arraySize = values.Count;
        for (int index = 0; index < values.Count; index++) property.GetArrayElementAtIndex(index).stringValue = values[index];
    }

    /// <summary>Unity Object 직렬화 배열을 설정한다.</summary>
    private static void SetArray(SerializedProperty property, IEnumerable<UnityEngine.Object> values)
    {
        UnityEngine.Object[] array = values.ToArray();
        property.arraySize = array.Length;
        for (int index = 0; index < array.Length; index++) property.GetArrayElementAtIndex(index).objectReferenceValue = array[index];
    }
}
#endif
