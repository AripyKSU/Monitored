#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Dialogue;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 39번 Sector1 기능 테스트를 번호별로 실행한다.
/// 상호작용 자체는 플레이어 입력 의존성이 있으므로 구성·진행 상태는 자동 검사하고 화면·조작 결과는 수동 항목으로 분리한다.
/// </summary>
public static class HospitalSector1FunctionalTest
{
    /// <summary>Sector1의 18개 테스트 항목을 문서 순서대로 실행한다.</summary>
    public static IEnumerator Run(HospitalSectorTestContext context)
    {
        Room startRoom = context.FindRoom("S1_StartBedroom");
        Room hallway = context.FindRoom("S1_Hallway");
        Room flashRoom = context.FindRoom("S1_FlashBedroom");
        Room centerHallway = context.FindRoom("CenterHallway_A");
        Target introTarget = context.FindTarget("S1_IntroRadioTarget");
        Target flashlightTarget = context.FindTarget("S1_FlashlightPickupTarget");
        Target wardKeyTarget = FindTargetByName(
            context,
            "Story_WardHallKeyDrawerTarget",
            "WardHallKeyDrawerTarget",
            "S1_WardHallKeyTarget",
            "WardHallKeyTarget");

        context.CheckRoom("Start 방", startRoom, "S1_StartBedroom", 1);
        context.Check(context.Session != null && context.Session.CurrentStoryStage == StoryStage.Sector1,
            "1. 시작 StoryStage가 Sector1입니다.",
            $"1. 시작 StoryStage가 Sector1이 아닙니다: {context.Session?.CurrentStoryStage}");

        // Intro Target은 DialogueTarget이어야 하며, 시작 방의 나머지 Target은 flag 게이트를 가져야 한다.
        context.Check(introTarget != null && introTarget.Action is DialogueTarget,
            "2. Intro Target이 DialogueTarget으로 구성됐습니다.",
            "2. Intro Target이 없거나 DialogueTarget이 아닙니다.");
        if (startRoom != null)
        {
            Target[] startTargets = startRoom.GetComponentsInChildren<Target>(true);
            context.Check(startTargets.Length > 0 && startTargets.Where(target => target != introTarget)
                .All(target => target.GetComponent<StoryFlagTarget>() != null || target.GetComponent<DoorTarget>() != null),
                "2. Intro 외 시작 Target에 진행 게이트가 연결됐습니다.",
                "2. Intro 외 시작 Target 중 진행 게이트가 없는 항목이 있습니다.");
        }

        context.Manual(3, "Intro Target을 실제로 완료하고 대화 종료 시점을 확인한다.");
        context.Check(introTarget != null && introTarget.GetComponent<DialogueTarget>() != null,
            "4. 방 진입 후 사용할 Intro Target 참조가 존재합니다.",
            "4. Intro 완료 후 사용할 Target 참조가 없습니다.");
        context.CheckRoom("복도", hallway, "S1_Hallway", 5);

        Doorway sectorDoor = FindDoorway(context, "SlideDoor_To_Sector1", hallway, centerHallway);
        context.Check(sectorDoor != null,
            "6. Sector1 복도 끝 Doorway가 존재합니다.",
            "6. Sector1 복도 끝 Doorway를 찾지 못했습니다.");
        context.Manual(6, "복도 끝 문 연출이 최초 1회만 재생되는지 확인한다.");

        context.Check(flashlightTarget != null && flashlightTarget.Action is ItemPickupTarget,
            "7. Flashlight 획득 Target이 ItemPickupTarget입니다.",
            "7. Flashlight 획득 Target이 없거나 ItemPickupTarget이 아닙니다.");
        context.CheckRoom("NursingRoom 후보 방", FindNursingRoom(), "S1_NursingRoom", 8);
        context.Check(FindNursingController() != null,
            "8. NursingRoomStoryController가 존재합니다.",
            "8. NursingRoomStoryController를 찾지 못했습니다.");
        context.Manual(9, "NursingRoom에서 Cancel을 선택해 강제 퇴실되는지 확인한다.");
        context.Manual(10, "NursingRoom 재진입 후 손전등 선택 UI가 다시 표시되는지 확인한다.");
        context.Manual(11, "Flashlight 선택 완료를 실제 플레이로 확인한다.");

        NursingRoomFlashlightView flashlightView = FindNursingRoomFlashlightView();
        context.Check(flashlightView != null,
            "12. NursingRoom Spot Light View가 존재합니다.",
            "12. NursingRoom Spot Light View를 찾지 못했습니다.");
        context.Check(context.Session != null &&
                      context.Session.HasStoryFlag(StoryProgressFlags.NursingRoomFlashlightActivated)
                      ? flashlightView != null
                      : true,
            "12. Flashlight 진행 상태와 Spot Light View 참조가 연결됩니다.",
            "12. Flashlight 진행 상태와 Spot Light View 연결이 없습니다.");
        context.Manual(13, "NursingRoom 퇴실 시 Spot Light가 OFF가 되는지 확인한다.");
        context.Manual(14, "NursingRoom 재진입 시 Spot Light가 자동 ON 되는지 확인한다.");

        context.Check(wardKeyTarget != null,
            "15. 병실 복도 열쇠 획득 Target이 존재합니다.",
            "15. 병실 복도 열쇠 획득 Target을 찾지 못했습니다.");
        context.CheckRoom("CenterHallway_A", centerHallway, "CenterHallway_A", 16);
        context.CheckCheckpoint("Save2", StoryStage.Sector2, "CenterHallway_A", 17);
        context.Manual(18, "Save2 진행 후 Restart하여 위치·아이템·flag·조명 복원을 확인한다.");

        yield return null;
    }

    /// <summary>두 가지 명명 규칙을 순서대로 사용해 기존 씬 Target을 찾는다.</summary>
    private static Target FindTargetByName(HospitalSectorTestContext context, params string[] names)
    {
        foreach (string name in names)
        {
            Target target = context.FindTarget(name);
            if (target != null)
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>두 Room 사이의 지정 이름 Doorway를 찾는다.</summary>
    private static Doorway FindDoorway(HospitalSectorTestContext context, string name, Room first, Room second)
    {
        return UnityEngine.Object.FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(doorway => doorway != null && doorway.name == name &&
                ((doorway.RoomA == first && doorway.RoomB == second) ||
                 (doorway.RoomA == second && doorway.RoomB == first)));
    }

    /// <summary>NursingRoom Controller가 붙은 Room을 찾아 반환한다.</summary>
    private static Room FindNursingRoom()
    {
        NursingRoomStoryController controller = FindNursingController();
        return controller != null ? controller.GetComponent<Room>() : null;
    }

    /// <summary>NursingRoom 진행 Controller를 비활성 오브젝트까지 포함해 찾는다.</summary>
    private static NursingRoomStoryController FindNursingController()
    {
        return UnityEngine.Object.FindFirstObjectByType<NursingRoomStoryController>(FindObjectsInactive.Include);
    }

    /// <summary>NursingRoom의 Light View를 찾는다.</summary>
    private static NursingRoomFlashlightView FindNursingRoomFlashlightView()
    {
        return UnityEngine.Object.FindFirstObjectByType<NursingRoomFlashlightView>(FindObjectsInactive.Include);
    }
}

/// <summary>Sector1 기능 테스트를 예약하는 전용 Editor 메뉴다.</summary>
public static class HospitalSector1FunctionalTestMenu
{
    /// <summary>CoreSandbox에서 Sector1 테스트를 시작한다.</summary>
    [MenuItem("Haunted House/Verify/39 Functional Tests/Sector 1")]
    public static void RunInPlayMode()
    {
        HospitalSectorTestHarness.Schedule(HospitalSectorId.Sector1);
    }
}
#endif
