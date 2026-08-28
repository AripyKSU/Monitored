#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Dialogue;
using HauntedHouse.Enemies;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 39번 Sector3 기능 테스트를 번호별로 실행한다.
/// Patrol·CCTV·힌트·체크포인트의 구조와 런타임 상태를 자동 검사하고 이동 체감은 수동 항목으로 남긴다.
/// </summary>
public static class HospitalSector3FunctionalTest
{
    /// <summary>Sector3의 12개 테스트 항목을 문서 순서대로 실행한다.</summary>
    public static IEnumerator Run(HospitalSectorTestContext context)
    {
        Room diningRoom = context.FindRoom("S3_DiningRoom");
        Room kitchen = context.FindRoom("S3_Kitchen");
        Room hallwayB = context.FindRoom("S3_HallwayB");
        Room basement = context.FindRoom("S4_Stairwell");
        EnemyController enemy = UnityEngine.Object.FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
        PatrolRoute route = context.FindNamed<PatrolRoute>("Sector3_KillerPatrolRoute");
        RoomGraph graph = UnityEngine.Object.FindFirstObjectByType<RoomGraph>(FindObjectsInactive.Include);
        Target exitTarget = FindExitTarget(context, hallwayB);
        Target freezerTarget = context.FindTarget("S3_BasementKeyFreezerTarget");
        Target hintTarget = context.FindTarget("Story_FreezerHintBoard");
        CctvPosePatrol cctv = UnityEngine.Object.FindFirstObjectByType<CctvPosePatrol>(FindObjectsInactive.Include);

        string routeError = string.Empty;
        bool routeIsValid = route != null && graph != null && route.TryValidateConfiguration(graph, out routeError);
        context.Check(routeIsValid,
            "1. Sector3 Enemy Patrol Route와 RoomGraph 구성이 유효합니다.",
            $"1. Sector3 Patrol Route 구성이 유효하지 않습니다: {routeError}");
        context.Check(enemy != null,
            "1. Sector3 Patrol 대상 Enemy가 존재합니다.",
            "1. Sector3 Patrol 대상 Enemy를 찾지 못했습니다.");
        context.Manual(1, "Enemy가 Patrol Route의 모든 지점을 한 바퀴 순환하는지 실제 이동으로 확인한다.");

        context.Check(exitTarget != null,
            "2. Sector3 Exit Door Target이 존재합니다.",
            "2. Sector3 Exit Door Target을 찾지 못했습니다.");
        context.Check(exitTarget == null || exitTarget.Action is ExitDoorHintTarget,
            "2. Exit Door Target이 ExitDoorHintTarget으로 구성됐습니다.",
            "2. Exit Door Target 행동이 ExitDoorHintTarget이 아닙니다.");
        context.Check(context.Session == null ||
                      context.Session.HasStoryFlag(StoryProgressFlags.ExitDoorHintShown) || exitTarget != null,
            "3. Exit Door 최초 Story 판정에 필요한 flag 또는 Target이 존재합니다.",
            "3. Exit Door 최초 Story 판정 대상을 찾지 못했습니다.");
        context.Manual(2, "Exit Door 최초 클릭 시 Story가 표시되는지 확인한다.");
        context.Manual(3, "Exit Door를 재클릭했을 때 Story가 반복되지 않는지 확인한다.");

        context.Check(freezerTarget != null,
            "4. Freezer Target이 존재합니다.",
            "4. Freezer Target을 찾지 못했습니다.");
        if (freezerTarget != null)
        {
            context.Check(freezerTarget.GetComponent<StoryFlagTarget>() != null ||
                          freezerTarget.GetComponent<ItemPickupTarget>() != null,
                "4. Freezer Target에 진행 상태 또는 아이템 행동이 연결됐습니다.",
                "4. Freezer Target에 진행 상태 게이트가 없습니다.");
        }

        context.Check(hintTarget != null,
            "5. HintBoard Target이 존재합니다.",
            "5. HintBoard Target을 찾지 못했습니다.");
        context.Check(hintTarget == null || hintTarget.GetComponent<MessageTarget>() != null ||
                      hintTarget.GetComponent<DialogueTarget>() != null,
            "5. HintBoard가 Message 또는 Dialogue 행동으로 구성됐습니다.",
            "5. HintBoard 행동 컴포넌트를 찾지 못했습니다.");
        context.Manual(5, "HintBoard를 읽어 Freezer가 활성화되는지 확인한다.");
        context.Check(context.Session == null || !context.Session.HasStoryFlag(StoryProgressFlags.FreezerHintRead) ||
                      (freezerTarget != null && freezerTarget.IsSelectable),
            "6. Freezer 활성 상태가 FreezerHintRead flag를 반영합니다.",
            "6. Freezer 활성 상태가 FreezerHintRead flag와 다릅니다.");
        context.Check(freezerTarget == null || freezerTarget.GetComponent<ItemPickupTarget>() != null,
            "7. Freezer에 Basement Key 획득 행동이 연결됐습니다.",
            "7. Freezer에 ItemPickupTarget이 연결되지 않았습니다.");

        context.CheckRoom("DiningRoom", diningRoom, "S3_DiningRoom", 1);
        context.CheckRoom("Kitchen", kitchen, "S3_Kitchen", 1);
        context.CheckRoom("ServiceHallway", hallwayB, "S3_HallwayB", 8);
        context.Check(cctv != null && cctv.ServiceHallwayRoom == hallwayB &&
                      cctv.KitchenRoom == kitchen && cctv.CenterHallwayBRoom == context.FindRoom("CenterHallway_B"),
            "8. ServiceHallway CCTV A/B Room 참조가 올바릅니다.",
            "8. ServiceHallway CCTV Room 참조가 누락되었거나 순서가 다릅니다.");
        if (cctv != null)
        {
            context.Check(cctv.MoveDuration > 0f && cctv.PoseA.Position != cctv.PoseB.Position,
                "9. CCTV 두 Pose와 보간 시간이 설정됐습니다.",
                "9. CCTV Pose 또는 보간 시간이 설정되지 않았습니다.");
        }
        context.Manual(9, "CCTV Position/Rotation이 A/B Pose 사이에서 보간되는지 확인한다.");

        context.CheckRoom("Stairwell", basement, "S4_Stairwell", 10);
        context.Manual(10, "Stairwell을 통과해 다음 Sector로 이동하는지 확인한다.");
        context.CheckCheckpoint("Save4", StoryStage.Sector4, "S4_BasementCorridor", 11);
        context.Manual(12, "Save4에서 Restart 후 위치·키·flag·Enemy 상태를 확인한다.");

        yield return null;
    }

    /// <summary>ServiceHallway 또는 지정 계층에서 Exit Door Hint Target을 찾는다.</summary>
    private static Target FindExitTarget(HospitalSectorTestContext context, Room hallway)
    {
        if (hallway != null)
        {
            Target local = hallway.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => target != null && target.GetComponent<ExitDoorHintTarget>() != null);
            if (local != null)
            {
                return local;
            }
        }

        return UnityEngine.Object.FindObjectsByType<Target>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(target => target != null && target.GetComponent<ExitDoorHintTarget>() != null);
    }
}

/// <summary>Sector3 기능 테스트를 예약하는 전용 Editor 메뉴다.</summary>
public static class HospitalSector3FunctionalTestMenu
{
    /// <summary>CoreSandbox에서 Sector3 테스트를 시작한다.</summary>
    [MenuItem("Haunted House/Verify/39 Functional Tests/Sector 3")]
    public static void RunInPlayMode()
    {
        HospitalSectorTestHarness.Schedule(HospitalSectorId.Sector3);
    }
}
#endif
