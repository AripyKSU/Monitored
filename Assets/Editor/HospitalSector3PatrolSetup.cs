#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 Sector3 살인마 Patrol Route와 Save3 Enemy 상태를 구성하는 Editor 도구다.
/// 씬의 기존 Enemy AI·RoomGraph·CheckpointDefinition을 재사용하고 전용 참조만 추가한다.
/// </summary>
public static class HospitalSector3PatrolSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    // PatrolRoute는 Component.name이 GameObject 이름을 그대로 노출하므로 두 이름을 동일하게 유지한다.
    private const string RouteRootName = "Sector3_KillerPatrolRoute";
    private const string RouteName = "Sector3_KillerPatrolRoute";
    private const string ActivatorName = "Sector3PatrolActivator";
    private const string Save3Id = "Save3";

    private static readonly string[] RouteRoomIds =
    {
        "S3_DiningRoom",
        "S3_Kitchen",
        "S3_HallwayB",
        "CenterHallway_B"
    };

    /// <summary>현재 CoreSandbox에 Sector3 Patrol Route와 Save3 연결을 멱등적으로 구성한다.</summary>
    [MenuItem("Haunted House/Setup/Hospital Sector3 Killer Patrol")]
    public static void Configure()
    {
        if (!EnsureEditMode() || !EnsureScene())
        {
            return;
        }

        Room[] rooms = FindRooms(RouteRoomIds);
        EnemyController enemy = FindEnemy();
        RoomGraph graph = UnityEngine.Object.FindFirstObjectByType<RoomGraph>();
        CheckpointDefinition save3 = FindCheckpoint(Save3Id);
        GameSession gameSession = UnityEngine.Object.FindFirstObjectByType<GameSession>();

        if (graph == null || enemy == null || save3 == null || gameSession == null)
        {
            throw new InvalidOperationException(
                "Sector3 Patrol 구성에 필요한 RoomGraph, Enemy, Save3 또는 GameSession을 찾지 못했습니다.");
        }

        // 중앙 복도 분할 이후에도 기존 문 설정 도구가 마지막으로 저장한 남쪽 Room 참조가
        // 남아 있을 수 있으므로, Sector3 방향 문만 B 구역 소유로 보정한 뒤 그래프를 Bake한다.
        EnsureSector3DoorOwnership(rooms);

        // 다른 Sector 작업이 Room·Doorway 참조를 바꾼 뒤에도 순찰 경로가 최신 그래프를
        // 사용해야 하므로, Setup 실행 시 현재 씬 기준으로 항상 한 번 다시 Bake한다.
        if (!graph.BakeFromScene(out string bakeError))
        {
            throw new InvalidOperationException($"Sector3 Patrol 구성 전 RoomGraph Bake에 실패했습니다: {bakeError}");
        }

        PatrolRoute route = GetOrCreateRoute(rooms, save3, graph, out PatrolPoint[] points);
        ConfigureEnemy(enemy, graph, route);
        ConfigureSave3(save3, enemy, route);
        ConfigureActivator(enemy, gameSession, route);

        EditorUtility.SetDirty(route);
        EditorUtility.SetDirty(save3);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        if (!ValidateConfiguration(out string report))
        {
            throw new InvalidOperationException(report);
        }

        GameLog.Log(report, route);
    }

    /// <summary>Sector3 Patrol의 Room·Route·Save3 참조를 저장 전 검증한다.</summary>
    [MenuItem("Haunted House/Validate/Hospital Sector3 Killer Patrol")]
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

    /// <summary>구성 요소를 읽어 계획 문서의 Sector3 Patrol 계약을 검사한다.</summary>
    /// <param name="report">검사 결과 요약.</param>
    /// <returns>필수 검사를 모두 통과했으면 true.</returns>
    public static bool ValidateConfiguration(out string report)
    {
        var errors = new List<string>();
        Room[] rooms = FindRooms(RouteRoomIds, errors);
        RoomGraph graph = UnityEngine.Object.FindFirstObjectByType<RoomGraph>();
        EnemyController enemy = FindEnemy(errors);
        PatrolRoute route = UnityEngine.Object.FindObjectsByType<PatrolRoute>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.gameObject.name == RouteName);
        CheckpointDefinition save3 = FindCheckpoint(Save3Id, errors);
        Sector3PatrolActivator activator = UnityEngine.Object.FindObjectsByType<Sector3PatrolActivator>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null);

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
                errors.Add($"PatrolRoute 지점 수가 {RouteRoomIds.Length}개가 아닙니다: {route.Count}");
            }
            else
            {
                for (int index = 0; index < RouteRoomIds.Length; index++)
                {
                    PatrolPoint point = route.GetPoint(index);
                    if (point == null || point.Room == null || point.Room.RoomId != RouteRoomIds[index])
                    {
                        errors.Add(
                            $"PatrolRoute {index}번 지점의 Room이 '{RouteRoomIds[index]}'가 아닙니다: " +
                            $"'{point?.Room?.RoomId ?? "null"}'");
                    }

                    if (point != null && !NavMesh.SamplePosition(point.Position, out _, 1.5f, NavMesh.AllAreas))
                    {
                        errors.Add($"PatrolPoint '{point.name}'이 NavMesh 근처에 없습니다: {point.Position}");
                    }
                }
            }
        }

        if (enemy == null)
        {
            errors.Add("Sector3 Patrol 대상 EnemyController가 없습니다.");
        }

        ValidateSector3DoorOwnership(rooms, errors);

        if (save3 == null)
        {
            errors.Add("Save3 CheckpointDefinition이 없습니다.");
        }
        else
        {
            if (!save3.TryValidateConfiguration(out string saveError))
            {
                errors.Add(saveError);
            }

            if (save3.EnemyStates == null || save3.EnemyStates.Length != 1)
            {
                errors.Add("Save3 EnemyStates가 정확히 1개가 아닙니다.");
            }
            else
            {
                CheckpointEnemyState state = save3.EnemyStates[0];
                if (state.Enemy != enemy) errors.Add("Save3 EnemyState의 Enemy 참조가 Sector3 대상과 다릅니다.");
                if (!state.Active) errors.Add("Save3 EnemyState가 활성 상태가 아닙니다.");
                if (state.PatrolRoute != route) errors.Add("Save3 EnemyState의 PatrolRoute 참조가 다릅니다.");
                if (state.RouteStartIndex != 0) errors.Add("Save3 EnemyState의 시작 인덱스가 0이 아닙니다.");
            }
        }

        if (activator == null)
        {
            errors.Add($"'{ActivatorName}' 컴포넌트가 없습니다.");
        }
        else
        {
            if (activator.Enemy != enemy) errors.Add("Sector3PatrolActivator의 Enemy 참조가 다릅니다.");
            if (activator.PatrolRoute != route) errors.Add("Sector3PatrolActivator의 PatrolRoute 참조가 다릅니다.");
            if (activator.GameSession == null) errors.Add("Sector3PatrolActivator의 GameSession 참조가 없습니다.");
        }

        if (errors.Count > 0)
        {
            report = "Sector3 Killer Patrol 검증 실패:\n- " + string.Join("\n- ", errors);
            return false;
        }

        report =
            "Sector3 Killer Patrol 검증 성공: DiningRoom → Kitchen → ServiceHallway → CenterHallway_B " +
            "경로와 Save3 복원·정상 진입 활성화 참조가 연결됐습니다.";
        return true;
    }

    /// <summary>지정한 RoomId 순서로 방을 찾는다.</summary>
    private static Room[] FindRooms(string[] roomIds)
    {
        return FindRooms(roomIds, new List<string>());
    }

    /// <summary>Room을 찾고 누락 항목을 검증 오류에 추가한다.</summary>
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
    private static EnemyController FindEnemy()
    {
        return FindEnemy(new List<string>());
    }

    /// <summary>Enemy를 찾고 없을 때 검증 오류를 기록한다.</summary>
    private static EnemyController FindEnemy(List<string> errors)
    {
        EnemyController[] enemies = UnityEngine.Object.FindObjectsByType<EnemyController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        EnemyController enemy = enemies.FirstOrDefault(candidate => candidate != null && candidate.name == "Enemy") ??
                                enemies.FirstOrDefault();
        if (enemy == null)
        {
            errors.Add("EnemyController가 없습니다.");
        }

        return enemy;
    }

    /// <summary>체크포인트 ID로 정의를 찾고 없을 때 검증 오류를 기록한다.</summary>
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

    /// <summary>Sector3 Patrol Route 오브젝트와 네 개의 순찰 지점을 만들거나 재사용한다.</summary>
    private static PatrolRoute GetOrCreateRoute(
        Room[] rooms, CheckpointDefinition save3, RoomGraph graph, out PatrolPoint[] points)
    {
        // 이전 Setup 실행에서 Save3가 이미 참조한 Route를 우선 재사용해 중복 Route가
        // 생겨도 체크포인트의 기존 참조를 보존한다.
        PatrolRoute route = save3?.EnemyStates?.FirstOrDefault()?.PatrolRoute;
        if (route == null || route.gameObject.name != RouteName)
        {
            route = UnityEngine.Object.FindObjectsByType<PatrolRoute>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate != null && candidate.gameObject.name == RouteName);
        }

        GameObject rootObject = route != null ? route.gameObject : GameObject.Find(RouteRootName);
        if (rootObject == null)
        {
            rootObject = new GameObject(RouteRootName);
            Undo.RegisterCreatedObjectUndo(rootObject, "Create Sector3 patrol root");
            SceneManager.MoveGameObjectToScene(rootObject, SceneManager.GetActiveScene());
        }

        route = rootObject.GetComponent<PatrolRoute>() ?? rootObject.AddComponent<PatrolRoute>();
        Undo.RecordObject(route, "Configure Sector3 patrol route");

        Doorway[] doorways = UnityEngine.Object.FindObjectsByType<Doorway>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        points = new PatrolPoint[rooms.Length];
        for (int index = 0; index < rooms.Length; index++)
        {
            string pointName = $"Patrol_{RouteRoomIds[index]}";
            Transform pointTransform = rootObject.transform.Find(pointName);
            if (pointTransform == null)
            {
                pointTransform = new GameObject(pointName).transform;
                pointTransform.SetParent(rootObject.transform, false);
                Undo.RegisterCreatedObjectUndo(pointTransform.gameObject, "Create Sector3 patrol point");
            }

            PatrolPoint point = pointTransform.GetComponent<PatrolPoint>() ?? pointTransform.gameObject.AddComponent<PatrolPoint>();
            Transform anchor = ResolveAnchor(
                rooms[index], rooms[(index + 1) % rooms.Length], graph, doorways);
            pointTransform.SetPositionAndRotation(anchor.position, anchor.rotation);
            SetObject(point, "room", rooms[index]);
            SetFloat(point, "waitDuration", 1.5f);
            points[index] = point;
            EditorUtility.SetDirty(point);
        }

        SetObjectArray(route, "points", points);
        SetInt(route, "startIndex", 0);
        EditorUtility.SetDirty(route.gameObject);
        RemoveDuplicateRoutes(route);
        return route;
    }

    /// <summary>동일 Setup을 여러 번 실행해 생긴 전용 중복 Route 오브젝트를 제거한다.</summary>
    private static void RemoveDuplicateRoutes(PatrolRoute canonicalRoute)
    {
        PatrolRoute[] duplicates = UnityEngine.Object.FindObjectsByType<PatrolRoute>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (PatrolRoute duplicate in duplicates)
        {
            if (duplicate == null || duplicate == canonicalRoute || duplicate.gameObject.name != RouteName)
            {
                continue;
            }

            // 이 이름의 Route는 본 Setup이 만든 전용 오브젝트이므로, 참조를 canonicalRoute로
            // 통일한 뒤 Undo 가능한 방식으로만 정리한다.
            Undo.DestroyObjectImmediate(duplicate.gameObject);
        }
    }

    /// <summary>
    /// CenterHallway_B와 Sector3 Room을 잇는 기존 Doorway의 중앙 복도 쪽 Room 참조를 보정한다.
    /// 동일 이름의 Kitchen↔ServiceHallway 문은 건드리지 않도록 양쪽 Room을 함께 확인한다.
    /// </summary>
    private static void EnsureSector3DoorOwnership(Room[] rooms)
    {
        Room centerA = UnityEngine.Object.FindObjectsByType<Room>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && room.RoomId == "CenterHallway_A");
        Room centerB = rooms.FirstOrDefault(room => room != null && room.RoomId == "CenterHallway_B");
        Room diningRoom = rooms.FirstOrDefault(room => room != null && room.RoomId == "S3_DiningRoom");
        Room serviceHallway = rooms.FirstOrDefault(room => room != null && room.RoomId == "S3_HallwayB");
        if (centerA == null || centerB == null || diningRoom == null || serviceHallway == null)
        {
            return;
        }

        ReassignCenterRoomDoor(
            "SlideDoor_To_ServiceHallway", centerA, centerB, serviceHallway);
        ReassignCenterRoomDoor(
            "SlideDoor_To_DiningRoom_From_Center", centerA, centerB, diningRoom);
    }

    /// <summary>지정한 중앙 복도↔Sector3 Doorway의 중앙 쪽 Room과 Target 소유 방을 B로 바꾼다.</summary>
    private static void ReassignCenterRoomDoor(
        string doorwayName, Room centerA, Room centerB, Room sectorRoom)
    {
        Doorway doorway = UnityEngine.Object.FindObjectsByType<Doorway>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.name == doorwayName &&
                ((candidate.RoomA == centerA && candidate.RoomB == sectorRoom) ||
                 (candidate.RoomA == sectorRoom && candidate.RoomB == centerA)));
        if (doorway == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(doorway);
        if (doorway.RoomA == centerA)
        {
            serialized.FindProperty("roomA").objectReferenceValue = centerB;
        }
        else if (doorway.RoomB == centerA)
        {
            serialized.FindProperty("roomB").objectReferenceValue = centerB;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        Undo.RecordObject(doorway, "Assign Sector3 doorway to CenterHallway_B");

        // 플레이어가 CenterHallway_B에서 이 Target을 선택할 수 있도록 같은 방향 Target의
        // 소유 방도 함께 갱신한다. Doorway의 실제 방향별 접근점은 그대로 재사용한다.
        foreach (Target target in doorway.GetComponentsInChildren<Target>(true))
        {
            if (target != null && target.OwnerRoom == centerA)
            {
                SetObject(target, "ownerRoom", centerB);
                EditorUtility.SetDirty(target);
            }
        }

        EditorUtility.SetDirty(doorway.gameObject);
    }

    /// <summary>Sector3 순찰에 필요한 두 중앙 복도 문이 CenterHallway_B에 연결됐는지 검사한다.</summary>
    private static void ValidateSector3DoorOwnership(Room[] rooms, List<string> errors)
    {
        Room centerB = rooms.FirstOrDefault(room => room != null && room.RoomId == "CenterHallway_B");
        Room diningRoom = rooms.FirstOrDefault(room => room != null && room.RoomId == "S3_DiningRoom");
        Room serviceHallway = rooms.FirstOrDefault(room => room != null && room.RoomId == "S3_HallwayB");
        CheckDoorOwnership(
            errors,
            "SlideDoor_To_ServiceHallway",
            centerB,
            serviceHallway,
            "CenterHallway_B ↔ S3_HallwayB 연결 문");
        CheckDoorOwnership(
            errors,
            "SlideDoor_To_DiningRoom_From_Center",
            centerB,
            diningRoom,
            "CenterHallway_B ↔ S3_DiningRoom 연결 문");
    }

    /// <summary>이름과 양쪽 Room이 일치하는 Doorway를 찾아 오류를 기록한다.</summary>
    private static void CheckDoorOwnership(
        List<string> errors, string doorwayName, Room first, Room second, string label)
    {
        bool exists = first != null && second != null &&
                      UnityEngine.Object.FindObjectsByType<Doorway>(
                              FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .Any(candidate => candidate != null && candidate.name == doorwayName &&
                              ((candidate.RoomA == first && candidate.RoomB == second) ||
                               (candidate.RoomA == second && candidate.RoomB == first)));
        if (!exists)
        {
            errors.Add($"{label}이 CenterHallway_B 기준으로 연결되지 않았습니다.");
        }
    }

    /// <summary>현재 방에서 다음 방으로 향하는 Doorway 접근 지점을 순찰 기준점으로 선택한다.</summary>
    private static Transform ResolveAnchor(
        Room sourceRoom, Room destinationRoom, RoomGraph graph, Doorway[] doorways)
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

        // 다음 방과 직접 연결되지 않은 구간은 RoomGraph BFS의 첫 Doorway 접근점을
        // 사용한다. 긴 복도의 반대편 끝에 순찰점을 두지 않아 EnemyDoorTraversal의
        // 공통 이동 제한시간을 넘기지 않고 곧바로 다음 문 통과를 시작할 수 있다.
        if (graph != null && graph.TryFindPath(sourceRoom, destinationRoom, out List<RoomGraphStep> path) &&
            path.Count > 0)
        {
            RoomGraphStep firstStep = path[0];
            if (firstStep.SourceRoom == sourceRoom &&
                firstStep.Doorway != null &&
                firstStep.Doorway.TryGetTraversal(sourceRoom, out DoorTraversal traversal) &&
                traversal.EntryPoint != null)
            {
                return traversal.EntryPoint;
            }
        }

        if (sourceRoom.PlayerSpawnPoint != null)
        {
            return sourceRoom.PlayerSpawnPoint;
        }

        foreach (Doorway doorway in doorways.OrderBy(candidate => candidate != null ? candidate.name : string.Empty))
        {
            if (doorway == null || (doorway.RoomA != sourceRoom && doorway.RoomB != sourceRoom))
            {
                continue;
            }

            if (doorway.TryGetTraversal(sourceRoom, out DoorTraversal traversal) && traversal.EntryPoint != null)
            {
                return traversal.EntryPoint;
            }
        }

        // 명시적인 문 접근점이 없는 임시 Room은 그래프 기준점을 사용하되, Room 루트는 이동하지 않는다.
        return CreateFallbackAnchor(sourceRoom);
    }

    /// <summary>두 방이 하나의 Doorway 양쪽에 연결됐는지 검사한다.</summary>
    private static bool Connects(Doorway doorway, Room first, Room second)
    {
        return doorway != null && first != null && second != null &&
               ((doorway.RoomA == first && doorway.RoomB == second) ||
                (doorway.RoomA == second && doorway.RoomB == first));
    }

    /// <summary>Doorway와 SpawnPoint가 모두 없는 방에만 임시 기준 Transform을 생성한다.</summary>
    private static Transform CreateFallbackAnchor(Room room)
    {
        string anchorName = "Sector3PatrolAnchor";
        Transform anchor = room.transform.Find(anchorName);
        if (anchor == null)
        {
            anchor = new GameObject(anchorName).transform;
            anchor.SetParent(room.transform, false);
            Undo.RegisterCreatedObjectUndo(anchor.gameObject, "Create Sector3 patrol fallback anchor");
        }

        anchor.position = room.GraphPosition;
        anchor.rotation = room.RoomCamera != null ? room.RoomCamera.transform.rotation : Quaternion.identity;
        EditorUtility.SetDirty(anchor.gameObject);
        return anchor;
    }

    /// <summary>Enemy에 RoomGraph와 Sector3 Route를 연결하고 기본 자동 시작은 끈다.</summary>
    private static void ConfigureEnemy(EnemyController enemy, RoomGraph graph, PatrolRoute route)
    {
        SetObject(enemy, "roomGraph", graph);
        SetObject(enemy, "initialRoute", route);
        SetBool(enemy, "initiallyActive", false);
        EnemyDoorTraversal doorTraversal = enemy.GetComponent<EnemyDoorTraversal>();
        if (doorTraversal != null)
        {
            // Sector3의 CenterHallway_A 논리 구간은 실제 Doorway 사이 거리가 길어
            // 기존 공통 30초 제한으로는 유효한 NavMesh 경로도 중간에 실패할 수 있다.
            // 이동 시스템 자체는 재사용하고 이 씬의 적 인스턴스 설정만 여유 있게 조정한다.
            SetFloat(doorTraversal, "movementTimeout", 120f);
        }
        enemy.gameObject.SetActive(true);
        EditorUtility.SetDirty(enemy.gameObject);
    }

    /// <summary>Save3 EnemyState를 Sector3 Route 첫 지점에서 활성화하도록 설정한다.</summary>
    private static void ConfigureSave3(CheckpointDefinition save3, EnemyController enemy, PatrolRoute route)
    {
        SerializedObject serialized = new SerializedObject(save3);
        SerializedProperty states = serialized.FindProperty("enemyStates");
        states.arraySize = 1;
        SerializedProperty state = states.GetArrayElementAtIndex(0);
        state.FindPropertyRelative("enemy").objectReferenceValue = enemy;
        state.FindPropertyRelative("active").boolValue = true;
        state.FindPropertyRelative("patrolRoute").objectReferenceValue = route;
        state.FindPropertyRelative("routeStartIndex").intValue = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Enemy GameObject에 정상 Sector3 진입용 Patrol 활성화 어댑터를 연결한다.</summary>
    private static void ConfigureActivator(EnemyController enemy, GameSession gameSession, PatrolRoute route)
    {
        Sector3PatrolActivator activator = enemy.GetComponent<Sector3PatrolActivator>() ??
                                            enemy.gameObject.AddComponent<Sector3PatrolActivator>();
        SetObject(activator, "gameSession", gameSession);
        SetObject(activator, "enemy", enemy);
        SetObject(activator, "patrolRoute", route);
        EditorUtility.SetDirty(activator);
    }

    /// <summary>현재 작업이 Edit Mode인지 확인한다.</summary>
    private static bool EnsureEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Sector3 Patrol 설정·검증은 Play Mode가 끝난 뒤 Edit Mode에서 실행해야 합니다.");
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
            $"Sector3 Patrol 설정은 '{ScenePath}' 씬에서 실행해야 합니다. " +
            $"현재 씬: {SceneManager.GetActiveScene().path}");
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

    /// <summary>SerializedObject의 bool 필드를 설정한다.</summary>
    private static void SetBool(UnityEngine.Object target, string propertyName, bool value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).boolValue = value;
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
