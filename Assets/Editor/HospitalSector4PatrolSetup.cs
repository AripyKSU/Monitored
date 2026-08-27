#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Enemies;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 Sector4 살인마 Patrol Route와 Save4·Save5 복원 참조를 구성하는 Editor 도구다.
/// 기존 EnemyController, RoomGraph, CheckpointDefinition을 재사용하고 Sector4 전용 참조만 추가한다.
/// </summary>
public static class HospitalSector4PatrolSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string RouteRootName = "Sector4_KillerPatrolRoute";
    private const string RouteName = "Sector4_KillerPatrolRoute";
    private const string ActivatorName = "Sector4PatrolActivator";
    private const string Save4Id = "Save4";
    private const string Save5Id = "Save5";
    private const float PatrolWaitDuration = 6f;

    private static readonly string[] RouteRoomIds =
    {
        "S4_MorgueRoom2",
        "S4_MorgueRoom1",
        "S4_BasementCorridor",
        "S4_Workshop",
        "S4_BasementCorridor",
        "S4_ControlRoom",
        "S4_BasementCorridor",
        "S4_Storage",
        "S4_BasementCorridor"
    };

    private static readonly string[] PointNames =
    {
        "Patrol_S4_MorgueRoom2_Start",
        "Patrol_S4_MorgueRoom1",
        "Patrol_S4_BasementCorridor_ToWorkshop",
        "Patrol_S4_Workshop",
        "Patrol_S4_BasementCorridor_ToControlRoom",
        "Patrol_S4_ControlRoom",
        "Patrol_S4_BasementCorridor_ToStorage",
        "Patrol_S4_Storage",
        "Patrol_S4_BasementCorridor_ToMorgueRoom2"
    };

    private static readonly string[] AnchorDestinationRoomIds =
    {
        "S4_MorgueRoom1",
        "S4_BasementCorridor",
        "S4_Workshop",
        "S4_BasementCorridor",
        "S4_ControlRoom",
        "S4_BasementCorridor",
        "S4_Storage",
        "S4_BasementCorridor",
        "S4_MorgueRoom2"
    };

    private static readonly (string Name, string First, string Second)[] RequiredDoorways =
    {
        ("Door_V3_MorgueRoom2", "S4_BasementCorridor", "S4_MorgueRoom2"),
        ("SlideDoor_MorgueRoom1", "S4_BasementCorridor", "S4_MorgueRoom1"),
        ("SlideDoor_Workshop", "S4_BasementCorridor", "S4_Workshop"),
        ("SlideDoor_ControlRoom", "S4_BasementCorridor", "S4_ControlRoom"),
        ("Door_V3_Storage", "S4_BasementCorridor", "S4_Storage")
    };

    /// <summary>Sector4 전용 Patrol Route, 활성화 어댑터, Save4·Save5 적 상태를 멱등적으로 구성한다.</summary>
    [MenuItem("Haunted House/Setup/Hospital Sector4 Killer Patrol")]
    public static void Configure()
    {
        if (!EnsureEditMode() || !EnsureScene())
        {
            return;
        }

        Room[] rooms = FindRooms(RouteRoomIds);
        RoomGraph graph = UnityEngine.Object.FindFirstObjectByType<RoomGraph>();
        EnemyController enemy = FindEnemy();
        GameSession gameSession = UnityEngine.Object.FindFirstObjectByType<GameSession>();
        CheckpointDefinition save4 = FindCheckpoint(Save4Id);
        CheckpointDefinition save5 = FindCheckpoint(Save5Id);

        if (rooms.Any(room => room == null) || graph == null || enemy == null ||
            gameSession == null || save4 == null || save5 == null)
        {
            throw new InvalidOperationException(
                "Sector4 Patrol 구성에 필요한 Room, RoomGraph, Enemy, GameSession, Save4, Save5 참조를 찾지 못했습니다.");
        }

        ValidateRequiredDoorways(rooms, throwOnError: true);
        if (!graph.BakeFromScene(out string bakeError))
        {
            throw new InvalidOperationException($"Sector4 Patrol 구성 전 RoomGraph Bake에 실패했습니다: {bakeError}");
        }

        PatrolRoute route = GetOrCreateRoute(rooms, graph, out PatrolPoint[] points);
        // Sector3 Setup에서 이미 연결했더라도, 공용 Enemy가 이 그래프를 사용하도록
        // Sector4 Setup에서도 같은 RoomGraph 참조만 명시적으로 보완한다.
        SetObject(enemy, "roomGraph", graph);
        ConfigureCheckpoint(save4, enemy, route);
        ConfigureCheckpoint(save5, enemy, route);
        Sector4PatrolActivator activator = ConfigureActivator(enemy, gameSession, route);

        EditorUtility.SetDirty(route);
        EditorUtility.SetDirty(save4);
        EditorUtility.SetDirty(save5);
        EditorUtility.SetDirty(activator);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        // 다른 작업자가 열어 둔 씬까지 저장하지 않도록 현재 활성 씬만 저장한다.
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        if (!ValidateConfiguration(out string report))
        {
            throw new InvalidOperationException(report);
        }

        GameLog.Log(report, route);
    }

    /// <summary>Sector4 Room·Doorway·Route·Save4·Save5·Activator 설정을 읽기 전용으로 검사한다.</summary>
    [MenuItem("Haunted House/Validate/Hospital Sector4 Killer Patrol")]
    public static void Validate()
    {
        if (!EnsureEditMode() || !EnsureScene())
        {
            return;
        }

        if (ValidateConfiguration(out string report))
        {
            GameLog.Log(report);
        }
        else
        {
            GameLog.LogError(report);
        }
    }

    /// <summary>계획 문서의 Sector4 Patrol 계약을 현재 씬 데이터로 검증한다.</summary>
    /// <param name="report">검증 결과 요약.</param>
    /// <returns>필수 검사를 모두 통과했으면 true.</returns>
    public static bool ValidateConfiguration(out string report)
    {
        var errors = new List<string>();
        Room[] rooms = FindRooms(RouteRoomIds, errors);
        RoomGraph graph = UnityEngine.Object.FindFirstObjectByType<RoomGraph>();
        EnemyController enemy = FindEnemy(errors);
        GameSession gameSession = UnityEngine.Object.FindFirstObjectByType<GameSession>();
        CheckpointDefinition save4 = FindCheckpoint(Save4Id, errors);
        CheckpointDefinition save5 = FindCheckpoint(Save5Id, errors);
        PatrolRoute route = UnityEngine.Object.FindObjectsByType<PatrolRoute>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.gameObject.name == RouteName);
        Sector4PatrolActivator activator = UnityEngine.Object.FindFirstObjectByType<Sector4PatrolActivator>(
            FindObjectsInactive.Include);

        if (graph == null)
        {
            errors.Add("RoomGraph가 없습니다.");
        }

        if (route == null)
        {
            errors.Add($"'{RouteName}' PatrolRoute가 없습니다.");
        }
        else
        {
            if (graph != null && !route.TryValidateConfiguration(graph, out string routeError))
            {
                errors.Add(routeError);
            }

            if (route.Count != RouteRoomIds.Length)
            {
                errors.Add($"Sector4 PatrolRoute 지점 수가 {RouteRoomIds.Length}개가 아닙니다: {route.Count}");
            }
            else
            {
                ValidateRoutePoints(route, errors);
            }
        }

        ValidateRequiredDoorways(rooms, errors);

        if (enemy == null)
        {
            errors.Add("Sector4 Patrol 대상 EnemyController가 없습니다.");
        }
        else if (graph != null)
        {
            SerializedObject enemySerialized = new SerializedObject(enemy);
            Check(errors, enemySerialized.FindProperty("roomGraph")?.objectReferenceValue == graph,
                "EnemyController의 RoomGraph 참조가 현재 RoomGraph와 다릅니다.");
        }

        ValidateCheckpoint(save4, Save4Id, enemy, route, errors, requireSector4Stage: true);
        ValidateCheckpoint(save5, Save5Id, enemy, route, errors, requireSector4Stage: false);

        if (gameSession == null)
        {
            errors.Add("GameSession을 찾지 못했습니다.");
        }

        if (activator == null)
        {
            errors.Add($"'{ActivatorName}' 컴포넌트가 Enemy에 없습니다.");
        }
        else
        {
            Check(errors, activator.Enemy == enemy, "Sector4PatrolActivator의 Enemy 참조가 다릅니다.");
            Check(errors, activator.PatrolRoute == route, "Sector4PatrolActivator의 PatrolRoute 참조가 다릅니다.");
            Check(errors, activator.GameSession == gameSession, "Sector4PatrolActivator의 GameSession 참조가 다릅니다.");
        }

        if (errors.Count > 0)
        {
            report = "Sector4 Killer Patrol 검증 실패:\n- " + string.Join("\n- ", errors);
            return false;
        }

        report =
            "Sector4 Killer Patrol 검증 성공: MorgueRoom2 → MorgueRoom1 → BasementCorridor → Workshop → " +
            "BasementCorridor → ControlRoom → BasementCorridor → Storage → BasementCorridor 반복 경로와 " +
            "6초 대기·Save4/Save5 복원 참조가 연결됐습니다.";
        return true;
    }

    /// <summary>각 지점의 Room, 이름, 6초 대기, NavMesh 위치와 반복 복도 기준점을 검사한다.</summary>
    private static void ValidateRoutePoints(PatrolRoute route, List<string> errors)
    {
        var corridorPositions = new List<Vector3>();
        for (int index = 0; index < RouteRoomIds.Length; index++)
        {
            PatrolPoint point = route.GetPoint(index);
            Check(errors, point != null, $"PatrolRoute {index}번 지점 참조가 비어 있습니다.");
            if (point == null)
            {
                continue;
            }

            Check(errors, point.name == PointNames[index],
                $"PatrolRoute {index}번 지점 이름이 '{PointNames[index]}'가 아닙니다: '{point.name}'");
            Check(errors, point.Room != null && point.Room.RoomId == RouteRoomIds[index],
                $"PatrolRoute {index}번 지점 Room이 '{RouteRoomIds[index]}'가 아닙니다: '{point.Room?.RoomId ?? "null"}'");
            Check(errors, Mathf.Abs(point.WaitDuration - PatrolWaitDuration) <= 0.01f,
                $"PatrolRoute {index}번 지점 대기 시간이 6초가 아닙니다: {point.WaitDuration:0.##}");
            Check(errors, NavMesh.SamplePosition(point.Position, out _, 1.5f, NavMesh.AllAreas),
                $"PatrolPoint '{point.name}'이 NavMesh 근처에 없습니다: {point.Position}");

            if (RouteRoomIds[index] == "S4_BasementCorridor")
            {
                corridorPositions.Add(point.Position);
            }
        }

        for (int left = 0; left < corridorPositions.Count; left++)
        {
            for (int right = left + 1; right < corridorPositions.Count; right++)
            {
                Check(errors, Vector3.Distance(corridorPositions[left], corridorPositions[right]) > 0.1f,
                    "BasementCorridor 반복 지점이 같은 위치를 공유합니다.");
            }
        }
    }

    /// <summary>Save4·Save5가 같은 Sector4 Route를 활성 상태·0번 시작점으로 복원하는지 검사한다.</summary>
    private static void ValidateCheckpoint(
        CheckpointDefinition checkpoint,
        string checkpointId,
        EnemyController enemy,
        PatrolRoute route,
        List<string> errors,
        bool requireSector4Stage)
    {
        if (checkpoint == null)
        {
            return;
        }

        Check(errors, checkpoint.TryValidateConfiguration(out string checkpointError),
            $"{checkpointId} 설정이 유효하지 않습니다. {checkpointError}");
        Check(errors, checkpoint.EnemyStates != null && checkpoint.EnemyStates.Length == 1,
            $"{checkpointId} EnemyStates가 정확히 1개가 아닙니다.");
        if (checkpoint.EnemyStates == null || checkpoint.EnemyStates.Length != 1)
        {
            return;
        }

        CheckpointEnemyState state = checkpoint.EnemyStates[0];
        Check(errors, state.Enemy == enemy, $"{checkpointId} EnemyState의 Enemy 참조가 Sector4 대상과 다릅니다.");
        Check(errors, state.Active, $"{checkpointId} EnemyState가 활성 상태가 아닙니다.");
        Check(errors, state.PatrolRoute == route, $"{checkpointId} EnemyState의 PatrolRoute 참조가 다릅니다.");
        Check(errors, state.RouteStartIndex == 0, $"{checkpointId} EnemyState의 시작 인덱스가 0이 아닙니다.");

        if (requireSector4Stage)
        {
            Check(errors, checkpoint.StoryStage == StoryStage.Sector4,
                "Save4 StoryStage가 Sector4가 아닙니다.");
            Check(errors, checkpoint.RespawnRoom != null && checkpoint.RespawnRoom.RoomId == "S4_BasementCorridor",
                "Save4 RespawnRoom이 S4_BasementCorridor가 아닙니다.");
        }
    }

    /// <summary>Sector4 순찰이 사용할 다섯 개의 기존 분기 Doorway를 이름과 양쪽 Room으로 검사한다.</summary>
    private static void ValidateRequiredDoorways(Room[] rooms, bool throwOnError)
    {
        var errors = new List<string>();
        ValidateRequiredDoorways(rooms, errors);
        if (throwOnError && errors.Count > 0)
        {
            throw new InvalidOperationException("Sector4 Patrol 분기 문 구성이 유효하지 않습니다:\n- " +
                                                string.Join("\n- ", errors));
        }
    }

    /// <summary>순찰 분기 Doorway 설정을 오류 목록에 추가한다.</summary>
    private static void ValidateRequiredDoorways(Room[] rooms, List<string> errors)
    {
        Doorway[] doorways = UnityEngine.Object.FindObjectsByType<Doorway>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach ((string name, string first, string second) required in RequiredDoorways)
        {
            Room firstRoom = rooms.FirstOrDefault(room => room != null && room.RoomId == required.first);
            Room secondRoom = rooms.FirstOrDefault(room => room != null && room.RoomId == required.second);
            Doorway[] candidates = doorways.Where(doorway => doorway != null && doorway.name == required.name &&
                    Connects(doorway, firstRoom, secondRoom)).ToArray();
            Check(errors, candidates.Length == 1,
                $"'{required.name}' Doorway가 {required.first} ↔ {required.second} 연결으로 정확히 하나 존재해야 합니다: {candidates.Length}");
            if (candidates.Length == 1)
            {
                Check(errors, candidates[0].TryValidateConfiguration(out string doorwayError), doorwayError);
            }
        }
    }

    /// <summary>정해진 Room 순서로 Sector4 순찰 지점에 사용할 Room을 찾는다.</summary>
    private static Room[] FindRooms(string[] roomIds)
    {
        return FindRooms(roomIds, new List<string>());
    }

    /// <summary>Room을 찾고 누락 항목을 검증 오류에 기록한다.</summary>
    private static Room[] FindRooms(string[] roomIds, List<string> errors)
    {
        Room[] allRooms = UnityEngine.Object.FindObjectsByType<Room>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var result = new Room[roomIds.Length];
        for (int index = 0; index < roomIds.Length; index++)
        {
            result[index] = allRooms.FirstOrDefault(room => room != null && room.RoomId == roomIds[index]);
            if (result[index] == null)
            {
                errors.Add($"Room '{roomIds[index]}'가 없습니다.");
            }
        }

        return result;
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
            errors?.Add("EnemyController가 없습니다.");
        }

        return enemy;
    }

    /// <summary>CheckpointId로 체크포인트를 찾고 누락 시 오류를 기록한다.</summary>
    private static CheckpointDefinition FindCheckpoint(string checkpointId, List<string> errors = null)
    {
        CheckpointDefinition checkpoint = UnityEngine.Object.FindObjectsByType<CheckpointDefinition>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.CheckpointId == checkpointId);
        if (checkpoint == null)
        {
            errors?.Add($"CheckpointDefinition '{checkpointId}'가 없습니다.");
        }

        return checkpoint;
    }

    /// <summary>Sector4 Route 오브젝트와 9개 PatrolPoint를 기존 참조를 보존하며 만들거나 재사용한다.</summary>
    private static PatrolRoute GetOrCreateRoute(Room[] rooms, RoomGraph graph, out PatrolPoint[] points)
    {
        PatrolRoute route = UnityEngine.Object.FindObjectsByType<PatrolRoute>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.gameObject.name == RouteName);

        GameObject rootObject = route != null ? route.gameObject : GameObject.Find(RouteRootName);
        if (rootObject == null)
        {
            rootObject = new GameObject(RouteRootName);
            Undo.RegisterCreatedObjectUndo(rootObject, "Create Sector4 patrol root");
            SceneManager.MoveGameObjectToScene(rootObject, SceneManager.GetActiveScene());
        }

        route = rootObject.GetComponent<PatrolRoute>() ?? rootObject.AddComponent<PatrolRoute>();
        Undo.RecordObject(route, "Configure Sector4 patrol route");
        Doorway[] doorways = UnityEngine.Object.FindObjectsByType<Doorway>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        points = new PatrolPoint[RouteRoomIds.Length];

        for (int index = 0; index < RouteRoomIds.Length; index++)
        {
            Transform pointTransform = rootObject.transform.Find(PointNames[index]);
            if (pointTransform == null)
            {
                pointTransform = new GameObject(PointNames[index]).transform;
                pointTransform.SetParent(rootObject.transform, false);
                Undo.RegisterCreatedObjectUndo(pointTransform.gameObject, "Create Sector4 patrol point");
            }

            PatrolPoint point = pointTransform.GetComponent<PatrolPoint>() ??
                                pointTransform.gameObject.AddComponent<PatrolPoint>();
            Room sourceRoom = rooms[index];
            Room destinationRoom = rooms.FirstOrDefault(room => room != null && room.RoomId == AnchorDestinationRoomIds[index]);
            Transform anchor = ResolveAnchor(sourceRoom, destinationRoom, graph, doorways);
            Vector3 pointPosition = SampleNavMeshPosition(anchor.position, PointNames[index]);
            pointTransform.SetPositionAndRotation(pointPosition, anchor.rotation);
            SetObject(point, "room", sourceRoom);
            SetFloat(point, "waitDuration", PatrolWaitDuration);
            points[index] = point;
            EditorUtility.SetDirty(point);
        }

        SetObjectArray(route, "points", points);
        SetInt(route, "startIndex", 0);
        EditorUtility.SetDirty(route.gameObject);
        RemoveDuplicateRoutes(route);
        return route;
    }

    /// <summary>동일 이름의 Sector4 전용 중복 Route만 Undo 가능한 방식으로 정리한다.</summary>
    private static void RemoveDuplicateRoutes(PatrolRoute canonicalRoute)
    {
        PatrolRoute[] routes = UnityEngine.Object.FindObjectsByType<PatrolRoute>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (PatrolRoute route in routes)
        {
            if (route != null && route != canonicalRoute && route.gameObject.name == RouteName)
            {
                Undo.DestroyObjectImmediate(route.gameObject);
            }
        }
    }

    /// <summary>현재 방에서 다음 방으로 향하는 직접 Doorway 접근점을 찾고, 없으면 Bake된 BFS 첫 문을 사용한다.</summary>
    private static Transform ResolveAnchor(Room sourceRoom, Room destinationRoom, RoomGraph graph, Doorway[] doorways)
    {
        foreach (Doorway doorway in doorways.OrderBy(candidate => candidate != null ? candidate.name : string.Empty))
        {
            if (doorway == null || !Connects(doorway, sourceRoom, destinationRoom))
            {
                continue;
            }

            if (doorway.TryGetTraversal(sourceRoom, out DoorTraversal traversal) && traversal.EntryPoint != null)
            {
                return traversal.EntryPoint;
            }
        }

        if (graph != null && graph.TryFindPath(sourceRoom, destinationRoom, out List<RoomGraphStep> path) &&
            path.Count > 0)
        {
            RoomGraphStep firstStep = path[0];
            if (firstStep.Doorway != null &&
                firstStep.Doorway.TryGetTraversal(sourceRoom, out DoorTraversal traversal) &&
                traversal.EntryPoint != null)
            {
                return traversal.EntryPoint;
            }
        }

        throw new InvalidOperationException(
            $"Sector4 Patrol 지점 기준점을 찾지 못했습니다: '{sourceRoom?.RoomId ?? "null"}' → " +
            $"'{destinationRoom?.RoomId ?? "null"}'. 분기 Doorway 접근점을 확인하세요.");
    }

    /// <summary>Doorway 접근점의 가장 가까운 NavMesh 위치를 순찰 지점으로 고정한다.</summary>
    private static Vector3 SampleNavMeshPosition(Vector3 sourcePosition, string pointName)
    {
        // DoorTarget 접근점은 문 연출용 높이로 배치된 경우가 있어, 적의 실제 Agent가
        // 설 수 있는 바닥 NavMesh를 기준으로 수직 위치만 보정한다. X·Z 방향은 문 앞
        // 접근점을 유지해 문 통과 경로가 불필요하게 바뀌지 않도록 한다.
        if (NavMesh.SamplePosition(sourcePosition, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        throw new InvalidOperationException(
            $"PatrolPoint '{pointName}' 기준점이 NavMesh에서 3m 안에 없습니다: {sourcePosition}");
    }

    /// <summary>Doorway가 두 지정 Room을 연결하는지 검사한다.</summary>
    private static bool Connects(Doorway doorway, Room first, Room second)
    {
        return doorway != null && first != null && second != null &&
               ((doorway.RoomA == first && doorway.RoomB == second) ||
                (doorway.RoomA == second && doorway.RoomB == first));
    }

    /// <summary>Save4·Save5 적 상태를 Sector4 Route 첫 지점에서 활성화하도록 설정한다.</summary>
    private static void ConfigureCheckpoint(CheckpointDefinition checkpoint, EnemyController enemy, PatrolRoute route)
    {
        SerializedObject serialized = new SerializedObject(checkpoint);
        SerializedProperty states = serialized.FindProperty("enemyStates");
        states.arraySize = 1;
        SerializedProperty state = states.GetArrayElementAtIndex(0);
        state.FindPropertyRelative("enemy").objectReferenceValue = enemy;
        state.FindPropertyRelative("active").boolValue = true;
        state.FindPropertyRelative("patrolRoute").objectReferenceValue = route;
        state.FindPropertyRelative("routeStartIndex").intValue = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Enemy에 Sector4 진행 이벤트를 감시하는 Patrol 활성화 어댑터를 연결한다.</summary>
    private static Sector4PatrolActivator ConfigureActivator(
        EnemyController enemy, GameSession gameSession, PatrolRoute route)
    {
        Sector4PatrolActivator activator = enemy.GetComponent<Sector4PatrolActivator>() ??
                                            enemy.gameObject.AddComponent<Sector4PatrolActivator>();
        SetObject(activator, "gameSession", gameSession);
        SetObject(activator, "enemy", enemy);
        SetObject(activator, "patrolRoute", route);
        EditorUtility.SetDirty(activator);
        return activator;
    }

    /// <summary>Doorway가 지정된 두 Room을 방향과 무관하게 연결하는지 검사한다.</summary>
    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition && !string.IsNullOrWhiteSpace(message))
        {
            errors.Add(message);
        }
    }

    /// <summary>현재 작업이 Edit Mode인지 확인한다.</summary>
    private static bool EnsureEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Sector4 Patrol 설정·검증은 Play Mode가 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return false;
        }

        return true;
    }

    /// <summary>현재 활성 씬이 병원 CoreSandbox인지 확인한다.</summary>
    private static bool EnsureScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath)
        {
            return true;
        }

        GameLog.LogWarning(
            $"Sector4 Patrol 설정은 '{ScenePath}' 씬에서 실행해야 합니다. 현재 씬: {SceneManager.GetActiveScene().path}");
        return false;
    }

    /// <summary>SerializedObject의 Object 필드를 명시적으로 설정한다.</summary>
    private static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>SerializedObject의 정수 필드를 설정한다.</summary>
    private static void SetInt(UnityEngine.Object target, string propertyName, int value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>SerializedObject의 실수 필드를 설정한다.</summary>
    private static void SetFloat(UnityEngine.Object target, string propertyName, float value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>SerializedObject의 Object 배열 필드를 순서대로 설정한다.</summary>
    private static void SetObjectArray(UnityEngine.Object target, string propertyName, UnityEngine.Object[] values)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty array = serialized.FindProperty(propertyName);
        array.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++)
        {
            array.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
