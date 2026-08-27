#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Core;
using HauntedHouse.Dialogue;
using HauntedHouse.Interaction;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 병원 Sector 1의 Target, 문 진입 연출, 대화 에셋과 체크포인트 완료 상태를 구성한다.
/// 전역 Target 검색으로 활성 상태를 결정하지 않고 각 Room과 Doorway의 자식 계층만 직접 설정한다.
/// </summary>
public static class HospitalSector1Setup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string SetupMenuPath = "Haunted House/Setup/Hospital Sector1";
    private const string ValidateMenuPath = "Haunted House/Validate/Hospital Sector1";
    private const string DialogueFolder = "Assets/Data/Dialogue";

    /// <summary>
    /// 현재 CoreSandbox에 Sector 1의 Target과 첫 방 진입 연출을 생성하거나 갱신한다.
    /// </summary>
    [MenuItem(SetupMenuPath)]
    public static void Configure()
    {
        EnsureScene();

        DialogueService dialogueService = FindRequired<DialogueService>("DialogueService");
        GamePauseService pauseService = FindRequired<GamePauseService>("GamePauseService");
        GameSession gameSession = FindRequired<GameSession>("GameSession");
        PlayerInventory inventory = FindRequired<PlayerInventory>("PlayerInventory");
        PlayerRoomTracker tracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        CameraDirector cameraDirector = FindRequired<CameraDirector>("CameraDirector");
        MessagePanel messagePanel = FindRequired<MessagePanel>("MessagePanel");
        ItemSelectionPanel selectionPanel = FindRequired<ItemSelectionPanel>("ItemSelectionPanel");
        TargetMarkerPool markerPool = FindRequired<TargetMarkerPool>("TargetMarkerPool");
        Camera worldCamera = Camera.main != null
            ? Camera.main
            : UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);

        if (worldCamera == null)
        {
            throw new InvalidOperationException("TargetPresenter가 사용할 Camera를 찾지 못했습니다.");
        }

        Room startRoom = FindRoom("S1_StartBedroom");
        Room hallway = FindRoom("S1_Hallway");
        Room flashRoom = FindRoom("S1_FlashBedroom");
        Room diaryRoom = FindRoom("S1_DiaryBedroom");
        Room centerA = FindRoom("CenterHallway_A");
        Doorway sector1Door = FindDoorway("SlideDoor_To_Sector1", centerA, hallway);

        ItemDefinition flashlight = FindItem("Assets/Data/Items/Flashlight.asset", "flashlight");
        ItemDefinition wardKey = FindItem("Assets/Data/Items/WardHallKey.asset", "ward_hall_key");

        DialogueSequence introDialogue = GetOrCreateDialogue(
            "S1_IntroRadioDialogue",
            "[게임 시작 무전기 대화]");
        DialogueSequence scareDialogue = GetOrCreateDialogue(
            "S1_HallwayDoorScareDialogue",
            "[복도 끝 문이 닫히는 것을 본 주인공]");
        DialogueSequence diaryDialogue = GetOrCreateDialogue(
            "S1_Diary1Dialogue",
            "[일기장 1번]");

        Target introTarget = ConfigureDialogueTarget(
            startRoom,
            "S1_IntroRadioTarget",
            GetStartTargetPosition(startRoom),
            introDialogue,
            dialogueService,
            tracker,
            markerPool,
            worldCamera);
        ConfigureStartBedroomTargets(
            startRoom,
            hallway,
            gameSession,
            messagePanel,
            tracker,
            markerPool,
            worldCamera,
            introTarget);

        ItemPickupTarget flashlightPickup = ConfigureFlashlightTargets(
            flashRoom,
            flashlight,
            inventory,
            messagePanel,
            tracker,
            markerPool,
            worldCamera);
        ConfigureDiaryTarget(
            diaryRoom,
            diaryDialogue,
            dialogueService,
            tracker,
            markerPool,
            worldCamera);

        ConfigureSector1Door(
            sector1Door,
            wardKey,
            inventory,
            pauseService,
            selectionPanel,
            messagePanel);
        ConfigureHallwayScare(
            hallway,
            sector1Door,
            scareDialogue,
            dialogueService,
            tracker,
            gameSession);
        ConfigureCheckpointCompletedStates(
            introTarget,
            flashlightPickup.GetComponent<Target>());

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameLog.Log("Hospital Sector 1 Target과 진입 연출 구성을 완료했습니다.");
    }

    /// <summary>
    /// Sector 1에 필요한 Target, 대화, 잠금 문과 체크포인트 연결을 읽기 전용으로 검사한다.
    /// </summary>
    [MenuItem(ValidateMenuPath)]
    public static void Validate()
    {
        EnsureScene();

        List<string> errors = new();
        Check(errors, FindRoomOrNull("S1_StartBedroom") != null, "S1_StartBedroom Room이 없습니다.");
        Check(errors, FindRoomOrNull("S1_Hallway") != null, "S1_Hallway Room이 없습니다.");
        Check(errors, FindRoomOrNull("S1_FlashBedroom") != null, "S1_FlashBedroom Room이 없습니다.");
        Check(errors, FindRoomOrNull("S1_DiaryBedroom") != null, "S1_DiaryBedroom Room이 없습니다.");

        Room startRoom = FindRoomOrNull("S1_StartBedroom");
        Room flashRoom = FindRoomOrNull("S1_FlashBedroom");
        Room diaryRoom = FindRoomOrNull("S1_DiaryBedroom");
        Target intro = FindTarget(startRoom, "S1_IntroRadioTarget");
        Check(errors, intro != null, "S1_IntroRadioTarget을 찾지 못했습니다.");
        Check(errors, intro != null && intro.Action is DialogueTarget, "S1_IntroRadioTarget의 행동이 DialogueTarget이 아닙니다.");
        CheckOwner(errors, intro, startRoom, "S1_IntroRadioTarget");

        Target startDrawerLeft = FindTarget(startRoom, "S1_StartDrawerLeftTarget");
        Target startDrawerRight = FindTarget(startRoom, "S1_StartDrawerRightTarget");
        Target flashlightPickup = FindTarget(flashRoom, "S1_FlashlightPickupTarget");
        Target diaryTarget = FindTarget(diaryRoom, "S1_Diary1Target");
        Check(errors, startDrawerLeft != null, "왼쪽 StartBedroom 서랍 Target이 없습니다.");
        Check(errors, startDrawerRight != null, "오른쪽 StartBedroom 서랍 Target이 없습니다.");
        Check(errors, flashlightPickup != null, "손전등 획득 Target이 없습니다.");
        Check(errors, diaryTarget != null, "일기장 1번 Target이 없습니다.");
        CheckOwner(errors, startDrawerLeft, startRoom, "S1_StartDrawerLeftTarget");
        CheckOwner(errors, startDrawerRight, startRoom, "S1_StartDrawerRightTarget");
        CheckOwner(errors, flashlightPickup, flashRoom, "S1_FlashlightPickupTarget");
        CheckOwner(errors, diaryTarget, diaryRoom, "S1_Diary1Target");
        CheckRoomTargetOwners(errors, startRoom, startRoom, "StartBedroom Sector1Targets");
        CheckRoomTargetOwners(errors, flashRoom, flashRoom, "FlashBedroom Sector1Targets");
        CheckRoomTargetOwners(errors, diaryRoom, diaryRoom, "DiaryBedroom Sector1Targets");

        Room hallway = FindRoomOrNull("S1_Hallway");
        Room centerA = FindRoomOrNull("CenterHallway_A");
        Doorway sector1Door = hallway != null && centerA != null
            ? FindDoorwayOrNull("SlideDoor_To_Sector1", centerA, hallway)
            : null;
        Check(errors, sector1Door != null, "SlideDoor_To_Sector1을 찾지 못했습니다.");
        Check(errors, sector1Door != null && sector1Door.IsLocked, "Sector 1 중앙 복도 문이 잠겨 있지 않습니다.");
        if (sector1Door != null)
        {
            Target centerTarget = sector1Door.GetComponentsInChildren<DoorTarget>(true)
                .Select(component => component.GetComponent<Target>())
                .FirstOrDefault(target => target != null && target.OwnerRoom == centerA);
            Target hallwayTarget = sector1Door.GetComponentsInChildren<DoorTarget>(true)
                .Select(component => component.GetComponent<Target>())
                .FirstOrDefault(target => target != null && target.OwnerRoom == hallway);
            Check(errors, centerTarget != null, "SlideDoor_To_Sector1의 CenterHallway_A 방향 Target 소유 방이 잘못됐습니다.");
            Check(errors, hallwayTarget != null, "SlideDoor_To_Sector1의 S1_Hallway 방향 Target 소유 방이 잘못됐습니다.");
        }

        RoomEntryEvent entryEvent = hallway != null
            ? hallway.GetComponentsInChildren<RoomEntryEvent>(true)
                .FirstOrDefault(component => component.name == "S1_HallwayScareEntry")
            : null;
        Check(errors, entryEvent != null, "S1_HallwayScareEntry가 없습니다.");
        Check(errors, entryEvent != null && entryEvent.OnEntered != null && entryEvent.OnEntered.GetPersistentEventCount() > 0,
            "S1_HallwayScareEntry의 OnEntered UnityEvent가 비어 있습니다.");
        ValidateCheckpointDefinitions(errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Hospital Sector 1 검증 실패:\n- " + string.Join("\n- ", errors));
        }

        GameLog.Log("Hospital Sector 1 구성이 유효합니다.");
    }

    /// <summary>현재 씬이 설정 대상인지 확인한다.</summary>
    private static void EnsureScene()
    {
        if (EditorApplication.isPlaying)
        {
            throw new InvalidOperationException("Play 모드에서는 Hospital Sector 1 설정을 실행할 수 없습니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException("Hospital Sector 1 설정은 CoreSandbox 씬에서만 실행할 수 있습니다.");
        }
    }

    /// <summary>필수 씬 컴포넌트를 찾아 없으면 설정을 중단한다.</summary>
    private static T FindRequired<T>(string label) where T : Component
    {
        T component = UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        return component != null
            ? component
            : throw new InvalidOperationException($"{label} 컴포넌트를 찾지 못했습니다.");
    }

    /// <summary>RoomId로 직접 Room을 찾는다.</summary>
    private static Room FindRoom(string roomId)
    {
        return FindRoomOrNull(roomId)
            ?? throw new InvalidOperationException($"Room '{roomId}'을 찾지 못했습니다.");
    }

    /// <summary>RoomId로 Room을 찾되 없으면 null을 반환한다.</summary>
    private static Room FindRoomOrNull(string roomId)
    {
        return UnityEngine.Object.FindObjectsByType<Room>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
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
        return UnityEngine.Object.FindObjectsByType<Doorway>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .FirstOrDefault(doorway =>
                doorway.name == doorwayName &&
                ((doorway.RoomA == first && doorway.RoomB == second) ||
                 (doorway.RoomA == second && doorway.RoomB == first)));
    }

    /// <summary>ItemId와 에셋 경로를 함께 사용해 진행 아이템을 찾는다.</summary>
    private static ItemDefinition FindItem(string assetPath, string itemId)
    {
        ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
        if (item == null || !string.Equals(item.ItemId, itemId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"아이템 에셋 '{assetPath}' 또는 ItemId '{itemId}'가 없습니다.");
        }

        return item;
    }

    /// <summary>StartBedroom의 기존 부활 위치를 사용해 플레이어 시작 지점에 Target을 배치한다.</summary>
    private static Vector3 GetStartTargetPosition(Room startRoom)
    {
        if (startRoom.PlayerSpawnPoint != null)
        {
            return startRoom.PlayerSpawnPoint.position;
        }

        Transform checkpointPoint = startRoom.transform.Find("Save1_StartBedroomPoint");
        if (checkpointPoint != null)
        {
            return checkpointPoint.position;
        }

        return new Vector3(10f, 1f, 195f);
    }

    /// <summary>StartBedroom의 두 서랍과 기존 출구를 Intro flag 이후에 해금한다.</summary>
    private static void ConfigureStartBedroomTargets(
        Room startRoom,
        Room hallway,
        GameSession gameSession,
        MessagePanel messagePanel,
        PlayerRoomTracker tracker,
        TargetMarkerPool markerPool,
        Camera worldCamera,
        Target introTarget)
    {
        Transform root = GetOrCreateChild(startRoom.transform, "Sector1Targets");
        ConfigureMessageTarget(
            startRoom,
            root,
            "S1_StartDrawerLeftTarget",
            FindProp(startRoom, "StartWard_BedTable_1"),
            "아무것도 발견되지 않았다.",
            "S1_StartDrawerLeft",
            gameSession,
            StoryProgressFlags.IntroDialogueCompleted,
            messagePanel,
            tracker,
            markerPool,
            worldCamera);
        ConfigureMessageTarget(
            startRoom,
            root,
            "S1_StartDrawerRightTarget",
            FindProp(startRoom, "StartWard_BedTable_4"),
            "아무것도 발견되지 않았다.",
            "S1_StartDrawerRight",
            gameSession,
            StoryProgressFlags.IntroDialogueCompleted,
            messagePanel,
            tracker,
            markerPool,
            worldCamera);

        Doorway startDoor = FindDoorway("SlideDoor", startRoom, hallway);
        Target exitTarget = startDoor.GetComponentsInChildren<Target>(true)
            .FirstOrDefault(target => target.OwnerRoom == startRoom);
        if (exitTarget == null)
        {
            throw new InvalidOperationException("StartBedroom 출구 DoorTarget을 찾지 못했습니다.");
        }

        StoryFlagTarget flagTarget = GetOrAdd<StoryFlagTarget>(exitTarget.gameObject);
        SetString(flagTarget, "requiredFlagId", StoryProgressFlags.IntroDialogueCompleted);
        SetObject(flagTarget, "gameSession", gameSession);
        SetObject(flagTarget, "target", exitTarget);
        exitTarget.SetSelectable(false);
        EditorUtility.SetDirty(exitTarget.gameObject);
    }

    /// <summary>FlashBedroom의 옷장 조사와 손전등 서랍 획득 Target을 구성한다.</summary>
    private static ItemPickupTarget ConfigureFlashlightTargets(
        Room flashRoom,
        ItemDefinition flashlight,
        PlayerInventory inventory,
        MessagePanel messagePanel,
        PlayerRoomTracker tracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Transform root = GetOrCreateChild(flashRoom.transform, "Sector1Targets");
        ConfigureMessageTarget(
            flashRoom,
            root,
            "S1_FlashBedroomWardrobeTarget",
            FindProp(flashRoom, "FlashWard_Locker_6"),
            "아무것도 발견되지 않았다.",
            "S1_FlashBedroomWardrobe",
            null,
            string.Empty,
            messagePanel,
            tracker,
            markerPool,
            worldCamera);

        Transform targetTransform = GetOrCreateChild(root, "S1_FlashlightPickupTarget");
        Target target = ConfigureTargetShell(
            flashRoom,
            targetTransform,
            FindProp(flashRoom, "FlashWard_BedTable_1"),
            tracker,
            markerPool,
            worldCamera);
        ItemPickupTarget pickup = GetOrAdd<ItemPickupTarget>(targetTransform.gameObject);
        SetObject(pickup, "item", flashlight);
        SetObject(pickup, "inventory", inventory);
        SetObject(pickup, "messagePanel", messagePanel);
        SetString(pickup, "pickupMessage", "손전등을 획득했습니다.");
        SetString(pickup, "completionId", "S1_FlashlightPickupTarget");

        GameObject visual = GetOrCreateFlashlightVisual(targetTransform);
        SetObject(pickup, "worldObject", visual);
        SetObject(target, "actionComponent", pickup);
        target.SetSelectable(true);
        EditorUtility.SetDirty(targetTransform.gameObject);
        return pickup;
    }

    /// <summary>문서 5번 placeholder 대화를 재생하는 DialogueTarget을 구성한다.</summary>
    private static Target ConfigureDialogueTarget(
        Room ownerRoom,
        string targetName,
        Vector3 position,
        DialogueSequence sequence,
        DialogueService dialogueService,
        PlayerRoomTracker tracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Transform root = GetOrCreateChild(ownerRoom.transform, "Sector1Targets");
        Transform targetTransform = GetOrCreateChild(root, targetName);
        targetTransform.position = position;

        Target target = ConfigureTargetShell(
            ownerRoom,
            targetTransform,
            targetTransform,
            tracker,
            markerPool,
            worldCamera);
        DialogueTarget dialogueTarget = GetOrAdd<DialogueTarget>(target.gameObject);
        SetObject(dialogueTarget, "dialogueService", dialogueService);
        SetObject(dialogueTarget, "sequence", sequence);
        SetString(dialogueTarget, "completionId", targetName);
        SetObject(target, "actionComponent", dialogueTarget);
        target.SetSelectable(true);
        EditorUtility.SetDirty(target.gameObject);
        return target;
    }

    /// <summary>DiaryBedroom의 서랍에 일기장 1번 placeholder 대화 Target을 구성한다.</summary>
    private static void ConfigureDiaryTarget(
        Room diaryRoom,
        DialogueSequence diaryDialogue,
        DialogueService dialogueService,
        PlayerRoomTracker tracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Transform root = GetOrCreateChild(diaryRoom.transform, "Sector1Targets");
        Target target = ConfigureTargetShell(
            diaryRoom,
            GetOrCreateChild(root, "S1_Diary1Target"),
            FindProp(diaryRoom, "DiaryWard_BedTable_4"),
            tracker,
            markerPool,
            worldCamera);
        DialogueTarget dialogueTarget = GetOrAdd<DialogueTarget>(target.gameObject);
        SetObject(dialogueTarget, "dialogueService", dialogueService);
        SetObject(dialogueTarget, "sequence", diaryDialogue);
        SetString(dialogueTarget, "completionId", "S1_Diary1Target");
        SetObject(target, "actionComponent", dialogueTarget);
        target.SetSelectable(true);
        EditorUtility.SetDirty(target.gameObject);
    }

    /// <summary>특정 Room의 새 상호작용 Target을 만들고 공용 Presenter·접근 지점을 연결한다.</summary>
    private static Target ConfigureTargetShell(
        Room ownerRoom,
        Transform targetTransform,
        Transform source,
        PlayerRoomTracker tracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        if (source == null)
        {
            throw new InvalidOperationException($"Target '{targetTransform.name}'의 배치 기준 오브젝트를 찾지 못했습니다.");
        }

        targetTransform.position = source.position;
        BoxCollider clickArea = GetOrAdd<BoxCollider>(targetTransform.gameObject);
        clickArea.isTrigger = true;
        clickArea.center = Vector3.up;
        clickArea.size = new Vector3(2f, 2f, 2f);

        Transform approachPoint = GetOrCreateChild(targetTransform, "ApproachPoint");
        approachPoint.position = SampleNavMesh(source.position, ownerRoom.transform.position);
        Vector3 facingDirection = source.position - approachPoint.position;
        approachPoint.rotation = facingDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(facingDirection, Vector3.up)
            : source.rotation;

        Transform uiAnchor = GetOrCreateChild(targetTransform, "UIAnchor");
        uiAnchor.position = source.position + Vector3.up * 1.6f;

        Target target = GetOrAdd<Target>(targetTransform.gameObject);
        SetObject(target, "approachPoint", approachPoint);
        SetObject(target, "ownerRoom", ownerRoom);
        SetBool(target, "selectable", true);

        TargetPresenter presenter = GetOrAdd<TargetPresenter>(targetTransform.gameObject);
        SetObject(presenter, "target", target);
        SetObject(presenter, "clickArea", clickArea);
        SetObject(presenter, "uiAnchor", uiAnchor);
        SetObject(presenter, "playerRoomTracker", tracker);
        SetObject(presenter, "worldCamera", worldCamera);
        SetObject(presenter, "markerPool", markerPool);

        EditorUtility.SetDirty(targetTransform.gameObject);
        return target;
    }

    /// <summary>공용 MessagePanel을 사용하는 조사 Target을 구성하고 필요하면 Story flag로 잠근다.</summary>
    private static void ConfigureMessageTarget(
        Room ownerRoom,
        Transform root,
        string targetName,
        Transform source,
        string message,
        string completionId,
        GameSession gameSession,
        string requiredFlagId,
        MessagePanel messagePanel,
        PlayerRoomTracker tracker,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        Target target = ConfigureTargetShell(
            ownerRoom,
            GetOrCreateChild(root, targetName),
            source,
            tracker,
            markerPool,
            worldCamera);
        MessageTarget messageTarget = GetOrAdd<MessageTarget>(target.gameObject);
        SetObject(messageTarget, "messagePanel", messagePanel);
        SetString(messageTarget, "message", message);
        SetString(messageTarget, "completionId", completionId);
        SetObject(target, "actionComponent", messageTarget);

        if (gameSession != null && !string.IsNullOrWhiteSpace(requiredFlagId))
        {
            StoryFlagTarget flagTarget = GetOrAdd<StoryFlagTarget>(target.gameObject);
            SetString(flagTarget, "requiredFlagId", requiredFlagId);
            SetObject(flagTarget, "gameSession", gameSession);
            SetObject(flagTarget, "target", target);
            target.SetSelectable(false);
        }

        EditorUtility.SetDirty(target.gameObject);
    }

    /// <summary>손전등 프리팹을 별도 자식으로 둬서 획득 후 가구 전체가 숨지 않도록 한다.</summary>
    private static GameObject GetOrCreateFlashlightVisual(Transform targetTransform)
    {
        Transform existing = targetTransform.Find("FlashlightVisual");
        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            return existing.gameObject;
        }

        const string prefabPath = "Assets/Free-FlashLight/Prefabs/FlashLight.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            throw new InvalidOperationException($"손전등 프리팹을 찾지 못했습니다: {prefabPath}");
        }

        GameObject visual = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (visual == null)
        {
            throw new InvalidOperationException("손전등 프리팹 인스턴스를 생성하지 못했습니다.");
        }

        visual.name = "FlashlightVisual";
        visual.transform.SetParent(targetTransform, false);
        visual.transform.localPosition = Vector3.up * 0.15f;
        visual.transform.localRotation = Quaternion.identity;
        visual.SetActive(true);
        Undo.RegisterCreatedObjectUndo(visual, "Create Sector1 flashlight visual");
        return visual;
    }

    /// <summary>Sector 1 중앙 복도 문을 잠그고 양쪽 DoorTarget에 기존 ItemSelectionPanel 흐름을 연결한다.</summary>
    private static void ConfigureSector1Door(
        Doorway sector1Door,
        ItemDefinition wardKey,
        PlayerInventory inventory,
        GamePauseService pauseService,
        ItemSelectionPanel selectionPanel,
        MessagePanel messagePanel)
    {
        SetBool(sector1Door, "locked", true);
        sector1Door.SetLocked(true);

        DoorController controller = sector1Door.GetComponentInChildren<DoorController>(true);
        if (controller == null)
        {
            throw new InvalidOperationException("SlideDoor_To_Sector1의 DoorController를 찾지 못했습니다.");
        }

        // 첫 Hallway 진입 시 닫히는 장면이 보이도록 씬 시작 포즈를 열린 상태로 둔다.
        SetBool(controller, "initiallyOpen", true);

        DoorTarget[] doorTargets = sector1Door.GetComponentsInChildren<DoorTarget>(true);
        if (doorTargets.Length < 2)
        {
            throw new InvalidOperationException("SlideDoor_To_Sector1의 양방향 DoorTarget을 모두 찾지 못했습니다.");
        }

        foreach (DoorTarget doorTarget in doorTargets)
        {
            ItemUseTarget itemUseTarget = GetOrAdd<ItemUseTarget>(doorTarget.gameObject);
            SetObject(itemUseTarget, "requiredItem", wardKey);
            SetBool(itemUseTarget, "consumeItemOnSuccess", false);
            SetObject(itemUseTarget, "effectComponent", doorTarget);
            SetObject(itemUseTarget, "inventory", inventory);
            SetObject(itemUseTarget, "pauseService", pauseService);
            SetObject(itemUseTarget, "selectionPanel", selectionPanel);
            SetObject(itemUseTarget, "messagePanel", messagePanel);
            SetString(itemUseTarget, "successMessage", string.Empty);
            SetString(itemUseTarget, "failureMessage", "이 아이템으로는 문을 열 수 없다.");

            SetObject(doorTarget, "lockedItemUseTarget", itemUseTarget);
            SetObject(doorTarget, "lockedMessagePanel", messagePanel);
            SetString(doorTarget, "lockedMessage", "잠겨있다.");
            EditorUtility.SetDirty(doorTarget.gameObject);
        }
    }

    /// <summary>Hallway 진입 시 중앙 복도 방향 문 닫힘과 placeholder 대화를 한 번만 실행하도록 연결한다.</summary>
    private static void ConfigureHallwayScare(
        Room hallway,
        Doorway sector1Door,
        DialogueSequence scareDialogue,
        DialogueService dialogueService,
        PlayerRoomTracker tracker,
        GameSession gameSession)
    {
        Transform root = GetOrCreateChild(hallway.transform, "Sector1StoryEvents");
        Transform eventTransform = GetOrCreateChild(root, "S1_HallwayScareEntry");
        RoomEntryEvent entryEvent = GetOrAdd<RoomEntryEvent>(eventTransform.gameObject);
        SetObject(entryEvent, "targetRoom", hallway);
        SetString(entryEvent, "completionFlagId", StoryProgressFlags.StartHallDoorScareSeen);
        SetBool(entryEvent, "markCompletionAfterEvent", true);
        SetObject(entryEvent, "playerRoomTracker", tracker);
        SetObject(entryEvent, "gameSession", gameSession);

        RoomEntryScareSequence sequence = GetOrAdd<RoomEntryScareSequence>(eventTransform.gameObject);
        SetObject(sequence, "scareDoor", sector1Door);
        SetObject(sequence, "dialogueService", dialogueService);
        SetObject(sequence, "sequence", scareDialogue);
        SetObject(sequence, "completionEvent", entryEvent);

        ClearPersistentListeners(entryEvent);
        UnityEventTools.AddPersistentListener(entryEvent.OnEntered, sequence.Play);
        EditorUtility.SetDirty(eventTransform.gameObject);
    }

    /// <summary>RoomEntryEvent의 직렬화된 UnityEvent 호출 목록을 비워 중복 진입 연출을 방지한다.</summary>
    private static void ClearPersistentListeners(RoomEntryEvent entryEvent)
    {
        SerializedObject serialized = new SerializedObject(entryEvent);
        SerializedProperty calls = serialized.FindProperty("onEntered.m_PersistentCalls.m_Calls");
        if (calls == null)
        {
            throw new InvalidOperationException("RoomEntryEvent의 onEntered PersistentCall 목록을 찾지 못했습니다.");
        }

        calls.arraySize = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Save2 이후 체크포인트에서 Intro 대화와 손전등 획득 상태를 유지하도록 등록한다.</summary>
    private static void ConfigureCheckpointCompletedStates(params Target[] targets)
    {
        Transform checkpointRoot = FindRequired<GameSession>("GameSession").transform.Find("HospitalCheckpoints");
        if (checkpointRoot == null)
        {
            throw new InvalidOperationException("HospitalCheckpoints가 없습니다. 먼저 Checkpoint Definitions 설정을 실행해야 합니다.");
        }

        // 체크포인트에는 Target 컨테이너가 아니라 실제 완료 상태를 소유한 컴포넌트만 저장한다.
        // 이렇게 하면 대화·아이템·조사 등 행동 유형이 달라도 ICompletedState 계약만으로 복원 대상을 찾을 수 있다.
        MonoBehaviour[] participants = targets
            .Where(target => target != null)
            .Select(FindCompletedStateParticipant)
            .Distinct()
            .ToArray();

        for (int index = 2; index <= 5; index++)
        {
            Transform saveTransform = checkpointRoot.Find($"Save{index}");
            if (saveTransform == null)
            {
                throw new InvalidOperationException($"Save{index} 체크포인트가 없습니다.");
            }

            CheckpointDefinition checkpoint = saveTransform.GetComponent<CheckpointDefinition>();
            if (checkpoint == null)
            {
                throw new InvalidOperationException($"Save{index}에 CheckpointDefinition이 없습니다.");
            }

            AddCompletedState(checkpoint, participants);
        }
    }

    /// <summary>Target에서 체크포인트 복원을 담당할 ICompletedState 구현 컴포넌트를 찾는다.</summary>
    private static MonoBehaviour FindCompletedStateParticipant(Target target)
    {
        if (target == null)
        {
            throw new InvalidOperationException("완료 상태 참가자를 찾을 Target이 null입니다.");
        }

        MonoBehaviour[] candidates = target.GetComponents<MonoBehaviour>()
            .Where(component => component is ICompletedState)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"Target '{target.name}'에 ICompletedState를 구현한 완료 상태 컴포넌트가 없습니다.");
        }

        if (candidates.Length > 1)
        {
            throw new InvalidOperationException(
                $"Target '{target.name}'에 ICompletedState 구현 컴포넌트가 여러 개라 참가자를 결정할 수 없습니다.");
        }

        return candidates[0];
    }

    /// <summary>체크포인트 완료 상태 배열에서 유효한 참가자만 유지하고 중복 없이 추가한다.</summary>
    private static void AddCompletedState(
        CheckpointDefinition checkpoint,
        params MonoBehaviour[] participants)
    {
        SerializedObject serialized = new SerializedObject(checkpoint);
        SerializedProperty states = serialized.FindProperty("completedStates");
        List<UnityEngine.Object> existing = new();

        for (int index = 0; index < states.arraySize; index++)
        {
            UnityEngine.Object participant = states
                .GetArrayElementAtIndex(index)
                .FindPropertyRelative("participant")
                .objectReferenceValue;
            // 이전 버전이 잘못 저장한 Target 같은 비계약 컴포넌트는 재구성 시 제거한다.
            if (participant is ICompletedState && !existing.Contains(participant))
            {
                existing.Add(participant);
            }
        }

        foreach (MonoBehaviour participant in participants)
        {
            if (participant != null && !existing.Contains(participant))
            {
                existing.Add(participant);
            }
        }

        states.arraySize = existing.Count;
        for (int index = 0; index < existing.Count; index++)
        {
            SerializedProperty element = states.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("participant").objectReferenceValue = existing[index];
            element.FindPropertyRelative("completed").boolValue = true;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(checkpoint);
    }

    /// <summary>Sector1 진행에 사용하는 Save2~Save5 체크포인트의 복원 정의를 검사한다.</summary>
    private static void ValidateCheckpointDefinitions(List<string> errors)
    {
        GameSession gameSession = UnityEngine.Object.FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);
        Transform checkpointRoot = gameSession != null
            ? gameSession.transform.Find("HospitalCheckpoints")
            : null;
        Check(errors, checkpointRoot != null, "GameSession/HospitalCheckpoints가 없습니다.");
        if (checkpointRoot == null)
        {
            return;
        }

        for (int index = 2; index <= 5; index++)
        {
            CheckpointDefinition checkpoint = checkpointRoot.Find($"Save{index}")?.GetComponent<CheckpointDefinition>();
            Check(errors, checkpoint != null, $"Save{index} 체크포인트 정의가 없습니다.");
            if (checkpoint != null && !checkpoint.TryValidateConfiguration(out string error))
            {
                errors.Add($"Save{index} 체크포인트 검증 실패: {error}");
            }
        }
    }

    /// <summary>이름으로 기존 가구 자식 Transform을 찾는다. Target은 검색하지 않는다.</summary>
    private static Transform FindProp(Room room, string propName)
    {
        Transform prop = room.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == propName);
        return prop != null
            ? prop
            : throw new InvalidOperationException($"Room '{room.RoomId}'에서 가구 '{propName}'을 찾지 못했습니다.");
    }

    /// <summary>기대한 Room의 자식 계층에서만 Target을 이름으로 확인한다.</summary>
    private static Target FindTarget(Room room, string targetName)
    {
        if (room == null)
        {
            return null;
        }

        return room.GetComponentsInChildren<Target>(true)
            .FirstOrDefault(candidate => candidate.name == targetName);
    }

    /// <summary>대화 에셋을 만들고 문서 5번 placeholder를 Pause 대화 한 줄로 저장한다.</summary>
    private static DialogueSequence GetOrCreateDialogue(string fileName, string text)
    {
        EnsureFolder("Assets/Data", "Dialogue");
        string path = $"{DialogueFolder}/{fileName}.asset";
        DialogueSequence dialogue = AssetDatabase.LoadAssetAtPath<DialogueSequence>(path);
        if (dialogue == null)
        {
            dialogue = ScriptableObject.CreateInstance<DialogueSequence>();
            AssetDatabase.CreateAsset(dialogue, path);
        }

        SerializedObject serialized = new SerializedObject(dialogue);
        serialized.FindProperty("pauseGame").boolValue = true;
        serialized.FindProperty("autoAdvanceDelay").floatValue = 0f;
        SerializedProperty lines = serialized.FindProperty("lines");
        lines.arraySize = 1;
        SerializedProperty line = lines.GetArrayElementAtIndex(0);
        line.FindPropertyRelative("speaker").enumValueIndex = (int)DialogueSpeaker.Radio;
        line.FindPropertyRelative("text").stringValue = text;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(dialogue);
        return dialogue;
    }

    /// <summary>Data 하위 에셋 폴더를 없으면 생성한다.</summary>
    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent))
        {
            AssetDatabase.CreateFolder("Assets", "Data");
        }

        string folder = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    /// <summary>요청 위치에서 방 기준점 방향으로 NavMesh 접근 위치를 샘플링한다.</summary>
    private static Vector3 SampleNavMesh(Vector3 requested, Vector3 fallback)
    {
        if (NavMesh.SamplePosition(requested, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        if (NavMesh.SamplePosition(fallback, out hit, 20f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return requested;
    }

    /// <summary>자식 Transform을 찾거나 새로 만든다.</summary>
    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(childObject, "Create Hospital Sector1 child");
        return childObject.transform;
    }

    /// <summary>컴포넌트를 찾거나 추가한다.</summary>
    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    /// <summary>직렬화된 Unity Object 참조를 안전하게 저장한다.</summary>
    private static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.name}에 직렬화 필드 '{propertyName}'가 없습니다.");
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>직렬화된 문자열 필드를 저장한다.</summary>
    private static void SetString(UnityEngine.Object target, string propertyName, string value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.name}에 문자열 필드 '{propertyName}'가 없습니다.");
        }

        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>직렬화된 bool 필드를 저장한다.</summary>
    private static void SetBool(UnityEngine.Object target, string propertyName, bool value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.name}에 bool 필드 '{propertyName}'가 없습니다.");
        }

        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>검증 오류 목록에 조건이 false일 때만 메시지를 추가한다.</summary>
    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }

    /// <summary>특정 Target이 지정한 Room을 소유하는지 검증한다.</summary>
    private static void CheckOwner(List<string> errors, Target target, Room expectedRoom, string label)
    {
        Check(
            errors,
            target != null && expectedRoom != null && target.OwnerRoom == expectedRoom,
            $"{label}의 OwnerRoom이 기대한 Room '{expectedRoom?.RoomId ?? "NULL"}'이 아닙니다.");
    }

    /// <summary>지정 Room의 Sector1Targets 아래 모든 Target이 같은 Room을 소유하는지 검사한다.</summary>
    private static void CheckRoomTargetOwners(List<string> errors, Room room, Room expectedRoom, string label)
    {
        if (room == null)
        {
            return;
        }

        Transform root = room.transform.Find("Sector1Targets");
        if (root == null)
        {
            return;
        }

        foreach (Target target in root.GetComponentsInChildren<Target>(true))
        {
            CheckOwner(errors, target, expectedRoom, $"{label}/{target.name}");
        }
    }
}
#endif
