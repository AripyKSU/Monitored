#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 39번 Sector4 기능 테스트를 번호별로 실행한다.
/// Patrol·일기·Morgue B·Master Key·Save5 선행 상태를 자동 검사하고 실제 조사는 수동 단계로 분리한다.
/// </summary>
public static class HospitalSector4FunctionalTest
{
    /// <summary>Sector4의 11개 테스트 항목을 문서 순서대로 실행한다.</summary>
    public static IEnumerator Run(HospitalSectorTestContext context)
    {
        Room basement = context.FindRoom("S4_BasementCorridor");
        Room morgue = context.FindRoom("S4_MorgueRoom2");
        EnemyController enemy = UnityEngine.Object.FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
        PatrolRoute route = context.FindNamed<PatrolRoute>("Sector4_KillerPatrolRoute");
        Sector4PatrolActivator activator = UnityEngine.Object.FindFirstObjectByType<Sector4PatrolActivator>(FindObjectsInactive.Include);
        Target diaryTarget = context.FindTarget("Workshop_RestraintBed");
        Target morgueTarget = context.FindTarget("MorgueBoxB");
        MorgueBoxBTarget morgueBox = UnityEngine.Object.FindFirstObjectByType<MorgueBoxBTarget>(FindObjectsInactive.Include);

        context.Check(activator != null && activator.Enemy != null && activator.PatrolRoute != null,
            "1. Sector4 Patrol Activator가 Enemy와 Route를 참조합니다.",
            "1. Sector4 Patrol Activator 또는 참조가 없습니다.");
        context.Check(route != null && route.Count > 0,
            "1. Sector4 Killer Patrol Route에 지점이 있습니다.",
            "1. Sector4 Killer Patrol Route가 비어 있습니다.");
        if (route != null)
        {
            RoomGraph graph = UnityEngine.Object.FindFirstObjectByType<RoomGraph>(FindObjectsInactive.Include);
            bool routeIsValid = route.TryValidateConfiguration(graph, out string routeError);
            context.Check(routeIsValid,
                "1. Sector4 Patrol Route와 RoomGraph 구성이 유효합니다.",
                $"1. Sector4 Patrol Route 구성이 유효하지 않습니다: {routeError}");
            bool allSixSeconds = Enumerable.Range(0, route.Count)
                .Select(index => route.GetPoint(index))
                .All(point => point != null && Math.Abs(point.WaitDuration - 6f) <= 0.05f);
            context.Check(allSixSeconds,
                "2. 모든 Sector4 Patrol Point의 대기 시간이 6초입니다.",
                "2. Sector4 Patrol Point 중 대기 시간이 6초가 아닌 지점이 있습니다.");
        }
        context.Check(enemy != null,
            "1. Sector4 대상 Enemy가 존재합니다.",
            "1. Sector4 대상 Enemy를 찾지 못했습니다.");
        context.Manual(1, "Enemy가 Sector4 Patrol Route를 실제로 순환하는지 확인한다.");
        context.Manual(2, "각 Patrol Point에서 약 6초 대기하는지 실제 시간으로 확인한다.");

        context.Check(diaryTarget != null,
            "3. Diary3 조사 Target이 존재합니다.",
            "3. Diary3 조사 Target을 찾지 못했습니다.");
        context.Check(morgueTarget != null && morgueBox != null && morgueTarget.Action is MorgueBoxBTarget,
            "3. Morgue B Target이 MorgueBoxBTarget으로 구성됐습니다.",
            "3. Morgue B Target이 없거나 MorgueBoxBTarget이 아닙니다.");
        context.Manual(3, "Diary3을 읽기 전 Morgue B에서 기본 조사 메시지만 나오는지 확인한다.");
        context.Manual(4, "Diary3을 읽고 KillerDiary3Read flag가 기록되는지 확인한다.");
        string morgueError = string.Empty;
        bool morgueIsValid = morgueBox != null && morgueBox.TryValidateConfiguration(out morgueError);
        context.Check(morgueIsValid,
            "5. Diary3 이후 Morgue B의 Master Key 구성 참조가 유효합니다.",
            $"5. Morgue B 구성이 유효하지 않습니다: {morgueError}");
        context.Manual(5, "Diary3 이후 Morgue B 재조사 시 두 조사 메시지와 분기를 확인한다.");
        context.Check(context.Session == null || !context.Session.HasStoryFlag(StoryProgressFlags.MasterKeyAcquired) ||
                      (morgueBox != null && morgueBox.IsCompleted),
            "6. MasterKeyAcquired flag와 Morgue B 완료 상태가 동기화됩니다.",
            "6. MasterKeyAcquired flag와 Morgue B 완료 상태가 불일치합니다.");
        context.Manual(6, "Master Key 획득 직후 Inventory와 무전 대화를 확인한다.");
        context.Manual(7, "Master Key 획득 Story가 한 번만 실행되는지 확인한다.");

        context.CheckRoom("Sector4 시작", basement, "S4_BasementCorridor", 1);
        context.CheckCheckpoint("Save5", StoryStage.Sector5Ready, "S4_MorgueRoom2", 8);
        context.Manual(9, "Save5에서 Restart 후 Master Key·flag·Room 복원을 확인한다.");
        context.Check(context.Session == null || !context.Session.HasStoryFlag(StoryProgressFlags.FinalChaseStarted),
            "11. Restart 직후 FinalChaseStarted가 꺼져 있습니다.",
            "11. Restart 직후 FinalChaseStarted가 자동으로 켜졌습니다.");
        context.Manual(10, "Restart 후 Master Key가 Inventory에 유지되는지 확인한다.");

        yield return null;
    }
}

/// <summary>Sector4 기능 테스트를 예약하는 전용 Editor 메뉴다.</summary>
public static class HospitalSector4FunctionalTestMenu
{
    /// <summary>CoreSandbox에서 Sector4 테스트를 시작한다.</summary>
    [MenuItem("Haunted House/Verify/39 Functional Tests/Sector 4")]
    public static void RunInPlayMode()
    {
        HospitalSectorTestHarness.Schedule(HospitalSectorId.Sector4);
    }
}
#endif
