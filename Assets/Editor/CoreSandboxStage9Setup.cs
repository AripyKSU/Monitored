using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.Interaction;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using HauntedHouse.Threat;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox에 9단계 체크포인트·스토리 이벤트 흐름을 구성하는 Editor 설정 도구다.
/// GameSession에는 진행 흐름만 남기고 플레이어·적 시스템을 별도 오브젝트로 분리한다.
/// </summary>
public static class CoreSandboxStage9Setup
{
    private const string ScenePath = "Assets/_3DStealthGame/Scenes/CoreSandbox.unity";
    private const string InitialCheckpointId = "Initial_Room_A";
    private const string RoomBCheckpointId = "Room_B_Key_Door";
    private const string RoomBEnteredFlag = "room_b_entered";

    /// <summary>CoreSandbox의 시스템 분리와 체크포인트 시나리오를 구성하고 씬을 저장한다.</summary>
    [MenuItem("Haunted House/Setup CoreSandbox Stage 9")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogError("CoreSandbox Stage 9 설정은 Play Mode에서 실행할 수 없습니다.");
            return;
        }

        Scene scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            GameLog.LogError($"활성 씬이 '{ScenePath}'가 아닙니다.");
            return;
        }

        GameObject gameSessionObject = RequireObject("GameSession");
        GameObject playerObject = RequireObject("Player");
        GameObject targetUiObject = RequireObject("Target UI");
        Room roomA = RequireRoom("Room_A");
        Room roomB = RequireRoom("Room_B");
        RoomGraph roomGraph = UnityEngine.Object.FindFirstObjectByType<RoomGraph>();
        EnemyController enemy = UnityEngine.Object.FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
        PatrolRoute initialRoute = UnityEngine.Object.FindObjectsByType<PatrolRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(route => route.name == "Route1")
            ?? UnityEngine.Object.FindFirstObjectByType<PatrolRoute>(FindObjectsInactive.Include);
        Player player = playerObject.GetComponent<Player>();

        if (roomGraph == null || enemy == null || initialRoute == null || player == null)
        {
            throw new InvalidOperationException("RoomGraph, Enemy, PatrolRoute 또는 Player가 씬에 없습니다.");
        }

        GameObject playerSystemsObject = GetOrCreateRoot("PlayerSystems");
        GameObject enemySystemsObject = GetOrCreateRoot("EnemySystems");
        GameObject worldSystemsObject = GetOrCreateRoot("WorldSystems");

        // 기존 GameSession에 있던 시스템을 새 책임별 오브젝트로 복사한 뒤 원본을 제거한다.
        PlayerInventory inventory = MoveComponent<PlayerInventory>(gameSessionObject, playerSystemsObject);
        TargetCommandRunner commandRunner = MoveComponent<TargetCommandRunner>(gameSessionObject, playerSystemsObject);
        StealthHideSystem hideSystem = MoveComponent<StealthHideSystem>(gameSessionObject, playerSystemsObject);
        PlayerCaptureDeathSystem captureSystem = MoveComponent<PlayerCaptureDeathSystem>(gameSessionObject, playerSystemsObject);
        EnemyChaseSystem chaseSystem = MoveComponent<EnemyChaseSystem>(gameSessionObject, enemySystemsObject);
        DangerAlertSystem alertSystem = MoveComponent<DangerAlertSystem>(gameSessionObject, enemySystemsObject);
        CameraDirector cameraDirector = MoveComponent<CameraDirector>(gameSessionObject, worldSystemsObject);

        GameSession gameSession = GetOrAdd<GameSession>(gameSessionObject);
        Component pauseService = GetOrAddByTypeName(gameSessionObject, "HauntedHouse.Core.GamePauseService");
        CheckpointService checkpointService = GetOrAdd<CheckpointService>(gameSessionObject);
        CheckpointResolver checkpointResolver = GetOrAdd<CheckpointResolver>(gameSessionObject);
        RespawnCoordinator respawnCoordinator = GetOrAdd<RespawnCoordinator>(gameSessionObject);

        ConfigurePlayer(player, pauseService);
        ConfigureContactPhysics(playerObject, enemy);
        ConfigureSession(gameSession, player, playerRoomTracker: player.GetComponent<PlayerRoomTracker>(), inventory, commandRunner, captureSystem);
        ConfigurePlayerSystems(player, player.GetComponent<PlayerRoomTracker>(), inventory, commandRunner, hideSystem, chaseSystem, captureSystem, cameraDirector, alertSystem, playerSystemsObject, enemySystemsObject);
        ConfigureEnemy(enemy, roomGraph, gameSession);
        ConfigureWorldSystems(cameraDirector, player.GetComponent<PlayerRoomTracker>());
        ConfigureCommandRunner(commandRunner, player, player.GetComponent<PlayerRoomTracker>(), hideSystem, gameSession);
        ConfigureDangerAlert(alertSystem, player.GetComponent<PlayerRoomTracker>(), new[] { enemy }, gameSession);
        ConfigureHideSystem(hideSystem, player, player.GetComponent<PlayerRoomTracker>(), chaseSystem);
        ConfigureChaseSystem(chaseSystem, player, player.GetComponent<PlayerRoomTracker>(), hideSystem, captureSystem, new[] { enemy }, gameSession);
        ConfigureCaptureSystem(captureSystem, hideSystem, chaseSystem);
        ConfigureInteractionUi(inventory, pauseService, gameSession, cameraDirector, player.GetComponent<PlayerRoomTracker>());

        Transform initialRespawnPoint = GetOrCreateRespawnPoint(roomA, "RespawnPoint_Initial", player.transform.position);
        Transform roomBRespawnPoint = GetOrCreateRespawnPoint(roomB, "RespawnPoint_RoomB", roomB.transform.position + Vector3.up * 0.1f);
        PatrolRoute roomBRoute = DuplicateRoomBRoute(initialRoute);

        CheckpointDefinition initialCheckpoint = GetOrCreateCheckpoint(gameSessionObject, InitialCheckpointId);
        CheckpointDefinition roomBCheckpoint = GetOrCreateCheckpoint(gameSessionObject, RoomBCheckpointId);
        ConfigureCheckpoint(initialCheckpoint, roomA, initialRespawnPoint, roomGraph, Array.Empty<ItemDefinition>(), Array.Empty<string>(), enemy, initialRoute, initialRoute.StartIndex, Array.Empty<MonoBehaviour>());

        ItemPickupTarget keyPickup = UnityEngine.Object.FindFirstObjectByType<ItemPickupTarget>(FindObjectsInactive.Include);
        ConfigureCheckpoint(roomBCheckpoint, roomB, roomBRespawnPoint, roomGraph, Array.Empty<ItemDefinition>(), new[] { RoomBEnteredFlag }, enemy, roomBRoute, 0, keyPickup != null ? new MonoBehaviour[] { keyPickup } : Array.Empty<MonoBehaviour>());

        ConfigureCheckpointService(checkpointService, gameSession, initialCheckpoint, respawnCoordinator);
        ConfigureRespawnCoordinator(respawnCoordinator, gameSession, player, player.GetComponent<PlayerRoomTracker>(), cameraDirector, inventory, commandRunner, hideSystem, captureSystem, chaseSystem, new[] { enemy }, FindCompletedStateParticipants());
        ConfigureResolver(checkpointResolver, gameSession, checkpointService, roomBCheckpoint);

        RestartPanelView restartView = GetOrAdd<RestartPanelView>(targetUiObject);
        ConfigureObjectReference(restartView, "gameSession", gameSession);
        ConfigureObjectReference(restartView, "checkpointService", checkpointService);
        ConfigureObjectReference(restartView, "captureDeathSystem", captureSystem);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (ValidateScene(out string report))
        {
            GameLog.Log(report);
        }
        else
        {
            GameLog.LogError(report);
        }
    }

    /// <summary>Stage 9의 필수 시스템과 체크포인트 참조를 검사한다.</summary>
    [MenuItem("Haunted House/Validate CoreSandbox Stage 9")]
    public static void Validate()
    {
        if (ValidateScene(out string report))
        {
            GameLog.Log(report);
        }
        else
        {
            GameLog.LogError(report);
        }
    }

    /// <summary>현재 씬의 Stage 9 구성이 실행 가능한지 검사한다.</summary>
    /// <param name="report">검사 결과 메시지.</param>
    /// <returns>모든 검사를 통과했으면 true.</returns>
    public static bool ValidateScene(out string report)
    {
        var problems = new List<string>();
        GameSession gameSession = UnityEngine.Object.FindFirstObjectByType<GameSession>();
        CheckpointService checkpointService = UnityEngine.Object.FindFirstObjectByType<CheckpointService>();
        CheckpointResolver resolver = UnityEngine.Object.FindFirstObjectByType<CheckpointResolver>();
        RespawnCoordinator coordinator = UnityEngine.Object.FindFirstObjectByType<RespawnCoordinator>();
        CheckpointDefinition[] checkpoints = UnityEngine.Object.FindObjectsByType<CheckpointDefinition>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (gameSession == null) problems.Add("GameSession이 없습니다.");
        if (checkpointService == null) problems.Add("CheckpointService가 없습니다.");
        if (resolver == null) problems.Add("CheckpointResolver가 없습니다.");
        if (coordinator == null) problems.Add("RespawnCoordinator가 없습니다.");
        if (checkpoints.Length < 2) problems.Add("초기·Room B 체크포인트가 모두 구성되지 않았습니다.");

        foreach (CheckpointDefinition checkpoint in checkpoints)
        {
            if (!checkpoint.TryValidateConfiguration(out string error))
            {
                problems.Add(error);
            }
        }

        if (GameObject.Find("PlayerSystems") == null) problems.Add("PlayerSystems 오브젝트가 없습니다.");
        if (GameObject.Find("EnemySystems") == null) problems.Add("EnemySystems 오브젝트가 없습니다.");

        if (problems.Count > 0)
        {
            report = "CoreSandbox Stage 9 검증 실패:\n- " + string.Join("\n- ", problems);
            return false;
        }

        report = "CoreSandbox Stage 9 검증 성공: 진행 흐름 분리, 초기 복원, Room B 체크포인트와 Restart 복원 참조가 연결됐습니다.";
        return true;
    }

    /// <summary>플레이어 이동이 복원 중에도 같은 PauseService를 사용하도록 연결한다.</summary>
    private static void ConfigurePlayer(Player player, UnityEngine.Object pauseService)
    {
        ConfigureObjectReference(player, "pauseService", pauseService);
    }

    /// <summary>GameSession이 진행 이벤트 발행에 필요한 외부 시스템만 참조하게 한다.</summary>
    private static void ConfigureSession(GameSession session, Player player, PlayerRoomTracker playerRoomTracker, PlayerInventory inventory, TargetCommandRunner commandRunner, PlayerCaptureDeathSystem captureSystem)
    {
        ConfigureObjectReference(session, "playerRoomTracker", playerRoomTracker);
        ConfigureObjectReference(session, "playerInventory", inventory);
        ConfigureObjectReference(session, "targetCommandRunner", commandRunner);
        ConfigureObjectReference(session, "captureDeathSystem", captureSystem);
    }

    /// <summary>플레이어·적 시스템의 GameSession 참조와 상호 참조를 연결한다.</summary>
    private static void ConfigurePlayerSystems(Player player, PlayerRoomTracker tracker, PlayerInventory inventory, TargetCommandRunner commandRunner, StealthHideSystem hideSystem, EnemyChaseSystem chaseSystem, PlayerCaptureDeathSystem captureSystem, CameraDirector cameraDirector, DangerAlertSystem alertSystem, GameObject playerSystemsObject, GameObject enemySystemsObject)
    {
        ConfigureObjectReference(hideSystem, "targetPresenters", UnityEngine.Object.FindObjectsByType<TargetPresenter>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        ConfigureObjectReference(commandRunner, "stealthHideSystem", hideSystem);
    }

    /// <summary>적이 복원 전에는 초기 상태를 사용하고 복원 후에는 체크포인트 상태를 사용하도록 연결한다.</summary>
    private static void ConfigureEnemy(EnemyController enemy, RoomGraph roomGraph, GameSession gameSession)
    {
        ConfigureObjectReference(enemy, "roomGraph", roomGraph);
        ConfigureObjectReference(enemy, "gameSession", gameSession);
    }

    /// <summary>카메라가 복원 후 플레이어 방을 기준으로 전환하도록 연결한다.</summary>
    private static void ConfigureWorldSystems(CameraDirector cameraDirector, PlayerRoomTracker tracker)
    {
        ConfigureObjectReference(cameraDirector, "playerRoomTracker", tracker);
    }

    /// <summary>Target 명령을 GameSession의 Playing 단계와 연결한다.</summary>
    private static void ConfigureCommandRunner(TargetCommandRunner runner, Player player, PlayerRoomTracker tracker, StealthHideSystem hideSystem, GameSession session)
    {
        ConfigureObjectReference(runner, "player", player);
        ConfigureObjectReference(runner, "playerRoomTracker", tracker);
        ConfigureObjectReference(runner, "stealthHideSystem", hideSystem);
        ConfigureObjectReference(runner, "gameSession", session);
    }

    /// <summary>경보 시스템이 복원 중에는 계산하지 않고 플레이 중에만 적을 관찰하게 한다.</summary>
    private static void ConfigureDangerAlert(DangerAlertSystem alert, PlayerRoomTracker tracker, EnemyController[] enemies, GameSession session)
    {
        ConfigureObjectReference(alert, "playerRoomTracker", tracker);
        ConfigureObjectReference(alert, "enemies", enemies);
        ConfigureObjectReference(alert, "gameSession", session);
    }

    /// <summary>은신 시스템이 플레이어와 적 추격 시스템을 사용하도록 연결한다.</summary>
    private static void ConfigureHideSystem(StealthHideSystem hide, Player player, PlayerRoomTracker tracker, EnemyChaseSystem chase)
    {
        ConfigureObjectReference(hide, "player", player);
        ConfigureObjectReference(hide, "playerRoomTracker", tracker);
        ConfigureObjectReference(hide, "enemyChaseSystem", chase);
    }

    /// <summary>추격 시스템이 복원 단계와 포획 시스템을 관찰하도록 연결한다.</summary>
    private static void ConfigureChaseSystem(EnemyChaseSystem chase, Player player, PlayerRoomTracker tracker, StealthHideSystem hide, PlayerCaptureDeathSystem capture, EnemyController[] enemies, GameSession session)
    {
        ConfigureObjectReference(chase, "player", player);
        ConfigureObjectReference(chase, "playerRoomTracker", tracker);
        ConfigureObjectReference(chase, "stealthHideSystem", hide);
        ConfigureObjectReference(chase, "captureDeathSystem", capture);
        ConfigureObjectReference(chase, "enemies", enemies);
        ConfigureObjectReference(chase, "gameSession", session);
    }

    /// <summary>포획 시스템이 은신과 추격 상태를 이용하도록 연결한다.</summary>
    private static void ConfigureCaptureSystem(PlayerCaptureDeathSystem capture, StealthHideSystem hide, EnemyChaseSystem chase)
    {
        ConfigureObjectReference(capture, "stealthHideSystem", hide);
        ConfigureObjectReference(capture, "enemyChaseSystem", chase);
    }

    /// <summary>인벤토리와 아이템 사용 Target이 이동된 PlayerInventory를 사용하도록 갱신한다.</summary>
    private static void ConfigureInteractionUi(PlayerInventory inventory, UnityEngine.Object pauseService, GameSession gameSession, CameraDirector cameraDirector, PlayerRoomTracker playerRoomTracker)
    {
        foreach (ItemUseTarget itemUseTarget in UnityEngine.Object.FindObjectsByType<ItemUseTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ConfigureObjectReference(itemUseTarget, "inventory", inventory);
            ConfigureObjectReference(itemUseTarget, "pauseService", pauseService);
        }

        foreach (InventoryPanel inventoryPanel in UnityEngine.Object.FindObjectsByType<InventoryPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ConfigureObjectReference(inventoryPanel, "inventory", inventory);
            ConfigureObjectReference(inventoryPanel, "pauseService", pauseService);
            ConfigureObjectReference(inventoryPanel, "gameSession", gameSession);
        }

        foreach (ItemSelectionPanel selectionPanel in UnityEngine.Object.FindObjectsByType<ItemSelectionPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ConfigureObjectReference(selectionPanel, "gameSession", gameSession);
        }

        foreach (DoorTarget doorTarget in UnityEngine.Object.FindObjectsByType<DoorTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ConfigureObjectReference(doorTarget, "cameraDirector", cameraDirector);
            ConfigureObjectReference(doorTarget, "playerRoomTracker", playerRoomTracker);
        }
    }

    /// <summary>체크포인트 서비스를 초기 체크포인트와 복원 조정기에 연결한다.</summary>
    private static void ConfigureCheckpointService(CheckpointService service, GameSession session, CheckpointDefinition initialCheckpoint, RespawnCoordinator coordinator)
    {
        ConfigureObjectReference(service, "gameSession", session);
        ConfigureObjectReference(service, "initialCheckpoint", initialCheckpoint);
        ConfigureObjectReference(service, "respawnCoordinator", coordinator);
    }

    /// <summary>복원 조정기에 복원 대상 시스템과 씬의 완료 상태 참여자를 연결한다.</summary>
    private static void ConfigureRespawnCoordinator(RespawnCoordinator coordinator, GameSession session, Player player, PlayerRoomTracker tracker, CameraDirector camera, PlayerInventory inventory, TargetCommandRunner runner, StealthHideSystem hide, PlayerCaptureDeathSystem capture, EnemyChaseSystem chase, EnemyController[] enemies, MonoBehaviour[] completedParticipants)
    {
        ConfigureObjectReference(coordinator, "gameSession", session);
        ConfigureObjectReference(coordinator, "player", player);
        ConfigureObjectReference(coordinator, "playerRoomTracker", tracker);
        ConfigureObjectReference(coordinator, "cameraDirector", camera);
        ConfigureObjectReference(coordinator, "playerInventory", inventory);
        ConfigureObjectReference(coordinator, "targetCommandRunner", runner);
        ConfigureObjectReference(coordinator, "stealthHideSystem", hide);
        ConfigureObjectReference(coordinator, "captureDeathSystem", capture);
        ConfigureObjectReference(coordinator, "enemyChaseSystem", chase);
        ConfigureObjectReference(coordinator, "enemies", enemies);
        ConfigureObjectReference(coordinator, "completedStateParticipants", completedParticipants);
    }

    /// <summary>Room B 진입 이벤트를 Room B 체크포인트 활성화 규칙으로 연결한다.</summary>
    private static void ConfigureResolver(CheckpointResolver resolver, GameSession session, CheckpointService service, CheckpointDefinition roomBCheckpoint)
    {
        ConfigureObjectReference(resolver, "gameSession", session);
        ConfigureObjectReference(resolver, "checkpointService", service);

        SerializedObject serializedObject = new SerializedObject(resolver);
        SerializedProperty rules = serializedObject.FindProperty("rules");
        rules.arraySize = 1;
        SerializedProperty rule = rules.GetArrayElementAtIndex(0);
        rule.FindPropertyRelative("eventType").enumValueIndex = (int)StoryEventType.RoomEntered;
        rule.FindPropertyRelative("subjectId").stringValue = "Room_B";
        rule.FindPropertyRelative("requiredFlagId").stringValue = string.Empty;
        rule.FindPropertyRelative("flagId").stringValue = RoomBEnteredFlag;
        rule.FindPropertyRelative("checkpoint").objectReferenceValue = roomBCheckpoint;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(resolver);
    }

    /// <summary>체크포인트 하나의 플레이어·적·스토리 복원 데이터를 직렬화한다.</summary>
    private static void ConfigureCheckpoint(CheckpointDefinition checkpoint, Room room, Transform respawnPoint, RoomGraph roomGraph, ItemDefinition[] items, string[] flags, EnemyController enemy, PatrolRoute route, int routeStartIndex, MonoBehaviour[] completedParticipants)
    {
        ConfigureObjectReference(checkpoint, "respawnRoom", room);
        ConfigureObjectReference(checkpoint, "respawnPoint", respawnPoint);
        ConfigureObjectReference(checkpoint, "roomGraph", roomGraph);
        ConfigureObjectArray(checkpoint, "inventoryItems", items);
        ConfigureStringArray(checkpoint, "progressFlags", flags);

        SerializedObject serializedObject = new SerializedObject(checkpoint);
        SerializedProperty completedStates = serializedObject.FindProperty("completedStates");
        completedStates.arraySize = completedParticipants.Length;
        for (int index = 0; index < completedParticipants.Length; index++)
        {
            SerializedProperty state = completedStates.GetArrayElementAtIndex(index);
            state.FindPropertyRelative("participant").objectReferenceValue = completedParticipants[index];
            state.FindPropertyRelative("completed").boolValue = true;
        }

        SerializedProperty enemyStates = serializedObject.FindProperty("enemyStates");
        enemyStates.arraySize = 1;
        SerializedProperty enemyState = enemyStates.GetArrayElementAtIndex(0);
        enemyState.FindPropertyRelative("enemy").objectReferenceValue = enemy;
        enemyState.FindPropertyRelative("active").boolValue = true;
        enemyState.FindPropertyRelative("patrolRoute").objectReferenceValue = route;
        enemyState.FindPropertyRelative("routeStartIndex").intValue = routeStartIndex;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();

        ConfigureString(checkpoint, "checkpointId", checkpoint.CheckpointId);
        EditorUtility.SetDirty(checkpoint);
    }

    /// <summary>체크포인트 자식 오브젝트를 가져오거나 생성한다.</summary>
    private static CheckpointDefinition GetOrCreateCheckpoint(GameObject gameSessionObject, string checkpointId)
    {
        Transform root = gameSessionObject.transform.Find("Checkpoints");
        if (root == null)
        {
            root = new GameObject("Checkpoints").transform;
            root.SetParent(gameSessionObject.transform, false);
        }

        Transform checkpointTransform = root.Find(checkpointId);
        if (checkpointTransform == null)
        {
            checkpointTransform = new GameObject(checkpointId).transform;
            checkpointTransform.SetParent(root, false);
        }

        CheckpointDefinition checkpoint = GetOrAdd<CheckpointDefinition>(checkpointTransform.gameObject);
        ConfigureString(checkpoint, "checkpointId", checkpointId);
        return checkpoint;
    }

    /// <summary>기존 순찰 경로를 복제해 Room B 체크포인트 전용 경로를 만든다.</summary>
    private static PatrolRoute DuplicateRoomBRoute(PatrolRoute source)
    {
        Transform parent = source.transform.parent;
        Transform existing = parent != null ? parent.Find("Route_RoomB_Checkpoint") : null;
        GameObject routeObject;
        if (existing == null)
        {
            routeObject = UnityEngine.Object.Instantiate(source.gameObject, parent);
            routeObject.name = "Route_RoomB_Checkpoint";
            Undo.RegisterCreatedObjectUndo(routeObject, "Create Room B checkpoint route");
        }
        else
        {
            routeObject = existing.gameObject;
        }

        PatrolRoute route = routeObject.GetComponent<PatrolRoute>();
        ConfigureInt(route, "startIndex", 0);
        return route;
    }

    /// <summary>방 아래에 NavMesh 복원 위치를 생성하거나 기존 위치를 재사용한다.</summary>
    private static Transform GetOrCreateRespawnPoint(Room room, string pointName, Vector3 worldPosition)
    {
        Transform point = room.transform.Find(pointName);
        if (point == null)
        {
            point = new GameObject(pointName).transform;
            point.SetParent(room.transform, true);
        }

        point.position = worldPosition;
        point.rotation = Quaternion.identity;
        EditorUtility.SetDirty(point.gameObject);
        return point;
    }

    /// <summary>순찰 경로 자식에서 이름이 같은 순찰 지점을 찾는다.</summary>
    private static PatrolPoint FindPatrolPoint(PatrolRoute route, string pointName)
    {
        PatrolPoint point = null;
        for (int index = 0; index < route.Count; index++)
        {
            PatrolPoint candidate = route.GetPoint(index);
            if (candidate != null && candidate.name == pointName)
            {
                point = candidate;
                break;
            }
        }

        if (point == null)
        {
            throw new InvalidOperationException($"PatrolRoute '{route.name}'에 '{pointName}'이 없습니다.");
        }

        return point;
    }

    /// <summary>씬의 완료 상태 계약 구현자를 체크포인트 복원 참여자로 수집한다.</summary>
    private static MonoBehaviour[] FindCompletedStateParticipants()
    {
        return UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(component => component is ICompletedState)
            .ToArray();
    }

    /// <summary>이름으로 방을 찾고 없으면 예외를 발생시킨다.</summary>
    private static Room RequireRoom(string roomId)
    {
        Room room = UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.RoomId == roomId);
        if (room == null)
        {
            throw new InvalidOperationException($"Room '{roomId}'가 없습니다.");
        }

        return room;
    }

    /// <summary>씬 루트 시스템 오브젝트를 가져오거나 생성한다.</summary>
    private static GameObject GetOrCreateRoot(string objectName)
    {
        GameObject existing = GameObject.Find(objectName);
        if (existing != null)
        {
            return existing;
        }

        GameObject created = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(created, $"Create {objectName}");
        return created;
    }

    /// <summary>씬 계층의 필수 오브젝트를 찾는다.</summary>
    private static GameObject RequireObject(string path)
    {
        GameObject gameObject = GameObject.Find(path);
        if (gameObject == null)
        {
            throw new InvalidOperationException($"씬 오브젝트 '{path}'를 찾을 수 없습니다.");
        }

        return gameObject;
    }

    /// <summary>기존 컴포넌트를 새 책임 오브젝트로 복사하고 원본을 제거한다.</summary>
    private static T MoveComponent<T>(GameObject source, GameObject destination) where T : Component
    {
        T original = source.GetComponent<T>();
        T moved = destination.GetComponent<T>();
        if (moved == null)
        {
            moved = Undo.AddComponent<T>(destination);
        }

        if (original != null && original != moved)
        {
            EditorUtility.CopySerialized(original, moved);
            Undo.DestroyObjectImmediate(original);
        }

        return moved;
    }

    /// <summary>컴포넌트가 없을 때만 추가한다.</summary>
    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        return gameObject.GetComponent<T>() ?? Undo.AddComponent<T>(gameObject);
    }

    /// <summary>
    /// 플레이어와 적이 물리 접촉 이벤트를 받을 수 있도록 Rigidbody와 충돌체를 구성한다.
    /// NavMeshAgent가 이동을 담당하므로 두 Rigidbody는 물리 힘에 의해 움직이지 않게 한다.
    /// </summary>
    private static void ConfigureContactPhysics(GameObject playerObject, EnemyController enemy)
    {
        // 플레이어의 시각 모델에 있는 자식 Collider도 부모 Rigidbody에 포함되도록 Player 루트에 붙인다.
        Rigidbody playerBody = GetOrAdd<Rigidbody>(playerObject);
        ConfigureKinematicContactBody(playerBody);

        // 적은 NavMeshAgent로 이동하지만 Trigger 접촉을 감지해야 하므로 Rigidbody를 명시적으로 구성한다.
        Rigidbody enemyBody = GetOrAdd<Rigidbody>(enemy.gameObject);
        ConfigureKinematicContactBody(enemyBody);

        // 적의 충돌체는 물리적으로 밀어내는 용도가 아니라 플레이어 접촉 감지용으로 사용한다.
        Collider enemyCollider = enemy.GetComponent<Collider>();
        if (enemyCollider != null)
        {
            enemyCollider.isTrigger = true;
        }
    }

    /// <summary>NavMesh 이동과 충돌 이벤트 감지를 함께 사용할 수 있는 Rigidbody 설정을 적용한다.</summary>
    private static void ConfigureKinematicContactBody(Rigidbody body)
    {
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = true;
        body.constraints = RigidbodyConstraints.FreezeAll;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    /// <summary>에디터 어셈블리에서 런타임 타입 참조가 불필요한 컴포넌트를 이름으로 보완한다.</summary>
    private static Component GetOrAddByTypeName(GameObject gameObject, string typeName)
    {
        Type componentType = Type.GetType($"{typeName}, Assembly-CSharp");
        if (componentType == null)
        {
            throw new InvalidOperationException($"런타임 컴포넌트 타입을 찾을 수 없습니다: {typeName}");
        }

        Component component = gameObject.GetComponent(componentType);
        if (component != null)
        {
            return component;
        }

        return Undo.AddComponent(gameObject, componentType);
    }

    /// <summary>SerializedObject로 단일 오브젝트 참조를 저장한다.</summary>
    private static void ConfigureObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 필드를 찾을 수 없습니다.");
        }

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    /// <summary>SerializedObject로 오브젝트 배열 참조를 저장한다.</summary>
    private static void ConfigureObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object[] values)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || !property.isArray)
        {
            throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 배열 필드를 찾을 수 없습니다.");
        }

        property.arraySize = values?.Length ?? 0;
        for (int index = 0; index < property.arraySize; index++)
        {
            property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    /// <summary>SerializedObject로 오브젝트 배열 참조를 저장한다.</summary>
    private static void ConfigureObjectArray(UnityEngine.Object target, string propertyName, UnityEngine.Object[] values)
    {
        ConfigureObjectReference(target, propertyName, values);
    }

    /// <summary>SerializedObject로 문자열 배열을 저장한다.</summary>
    private static void ConfigureStringArray(UnityEngine.Object target, string propertyName, string[] values)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        property.arraySize = values?.Length ?? 0;
        for (int index = 0; index < property.arraySize; index++)
        {
            property.GetArrayElementAtIndex(index).stringValue = values[index];
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    /// <summary>SerializedObject로 문자열을 저장한다.</summary>
    private static void ConfigureString(UnityEngine.Object target, string propertyName, string value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        serializedObject.FindProperty(propertyName).stringValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    /// <summary>SerializedObject로 정수를 저장한다.</summary>
    private static void ConfigureInt(UnityEngine.Object target, string propertyName, int value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        serializedObject.FindProperty(propertyName).intValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

}
