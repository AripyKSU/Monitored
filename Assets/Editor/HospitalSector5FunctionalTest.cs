#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 39번 Sector5 기능 테스트를 번호별로 실행한다.
/// 추격 Controller·엔딩 분기·Credits 참조는 자동 검사하고 실제 이동과 화면 연출은 수동 단계로 분리한다.
/// </summary>
public static class HospitalSector5FunctionalTest
{
    /// <summary>Sector5의 18개 테스트 항목을 문서 순서대로 실행한다.</summary>
    public static IEnumerator Run(HospitalSectorTestContext context)
    {
        Sector5FinalChaseController chase = UnityEngine.Object.FindFirstObjectByType<Sector5FinalChaseController>(FindObjectsInactive.Include);
        Sector5EntranceController entrance = UnityEngine.Object.FindFirstObjectByType<Sector5EntranceController>(FindObjectsInactive.Include);
        EndingController ending = UnityEngine.Object.FindFirstObjectByType<EndingController>(FindObjectsInactive.Include);
        EndingCreditsView credits = UnityEngine.Object.FindFirstObjectByType<EndingCreditsView>(FindObjectsInactive.Include);
        EndingTargetAction[] endingTargets = UnityEngine.Object.FindObjectsByType<EndingTargetAction>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        context.Check(chase != null,
            "1. Sector5 Final Chase Controller가 존재합니다.",
            "1. Sector5 Final Chase Controller를 찾지 못했습니다.");
        if (chase != null)
        {
            bool chaseIsValid = chase.TryValidateConfiguration(out string chaseError);
            context.Check(chaseIsValid,
                "1. Stairwell 이동·Enemy·Dialogue 참조가 유효합니다.",
                $"1. Final Chase 구성이 유효하지 않습니다: {chaseError}");
            context.Check(!chase.IsRunning,
                "2. 실제 Stairwell 도착 전 Final Chase가 실행 중이 아닙니다.",
                "2. 실제 Stairwell 도착 전 Final Chase가 실행 중입니다.");
        }

        context.CheckRoom("Sector5 Stairwell", context.FindRoom("S4_Stairwell"), "S4_Stairwell", 3);
        context.Manual(1, "Stairwell 이동 명령을 실행하고 실제 도착 전 Chase가 시작되지 않는지 확인한다.");
        context.Manual(3, "Stairwell 도착 후 Pause 연출이 시작되는지 확인한다.");
        context.Manual(4, "Pause 중 Killer가 등장하는지 확인한다.");
        context.Manual(5, "[도망쳐] Dialogue가 표시되는지 확인한다.");
        context.Manual(6, "Dialogue 종료 순간 AI와 Player가 동시에 활성화되는지 확인한다.");
        context.Manual(7, "다른 Room으로 이동해도 Enemy가 계속 추적하는지 확인한다.");
        context.Manual(8, "Hide Target 선택 시 숨기 차단 연출을 확인한다.");
        context.Manual(9, "지금은 숨을 수 없다. 메시지가 출력되는지 확인한다.");

        context.Check(entrance != null,
            "11. Sector5 Entrance Controller가 존재합니다.",
            "11. Sector5 Entrance Controller를 찾지 못했습니다.");
        if (entrance != null)
        {
            context.CheckRoom("Entrance Room", entrance.EntranceRoom, "S5_DoorArea", 11);
            context.Check(entrance.EntranceDialogue != null && !entrance.EntranceDialogue.PauseGame &&
                          entrance.EntranceDialogue.Lines != null && entrance.EntranceDialogue.Lines.Count > 0 &&
                          entrance.EntranceDialogue.Lines[0].Text == "[왼쪽 방]",
                "12. Entrance 안내가 Non-Pause '[왼쪽 방]' Dialogue입니다.",
                "12. Entrance 안내 Dialogue 설정이 계획과 다릅니다.");
        }
        context.Manual(11, "Entrance 도착 시 EntranceReached flag와 안내 Dialogue를 확인한다.");
        context.Manual(12, "[왼쪽 방] 안내 중 게임이 Pause되지 않는지 확인한다.");

        string endingError = string.Empty;
        bool endingIsValid = ending != null && ending.TryValidateConfiguration(out endingError);
        context.Check(endingIsValid,
            "13. EndingController의 공용 Ending 참조가 유효합니다.",
            $"13. EndingController 구성이 유효하지 않습니다: {endingError}");
        context.Check(endingTargets.Any(target => target != null && target.EndingType == EndingType.Bad),
            "13. MonitoringRoom Bad Ending Target이 존재합니다.",
            "13. MonitoringRoom Bad Ending Target을 찾지 못했습니다.");
        context.Check(endingTargets.Any(target => target != null && target.EndingType == EndingType.Happy),
            "15. Hidden Exit Happy Ending Target이 존재합니다.",
            "15. Hidden Exit Happy Ending Target을 찾지 못했습니다.");
        context.Manual(13, "MonitoringRoom에 진입해 Bad Ending을 확인한다.");
        context.Manual(14, "재실행 후 Hidden Exit를 선택해 Happy Ending을 확인한다.");

        context.Check(ending != null,
            "16. Timeline 연결 여부와 무관하게 EndingController가 존재합니다.",
            "16. Timeline 미연결 검증을 위한 EndingController가 없습니다.");
        context.Manual(16, "Timeline 참조를 연결하지 않은 상태에서 fallback Ending 진행을 확인한다.");
        context.Check(credits != null,
            "17. EndingCreditsView가 존재합니다.",
            "17. EndingCreditsView를 찾지 못했습니다.");
        context.Manual(17, "Credits 표시와 타이틀 복귀 버튼을 확인한다.");
        context.Check(Application.CanStreamedLevelBeLoaded("MainMenu"),
            "18. MainMenu 씬이 Build Settings에 등록되어 있습니다.",
            "18. MainMenu 씬이 Build Settings에 등록되지 않았습니다.");
        context.Manual(18, "Credits에서 타이틀 복귀를 실행해 MainMenu 로드 결과를 확인한다.");

        yield return null;
    }
}

/// <summary>Sector5 기능 테스트를 예약하는 전용 Editor 메뉴다.</summary>
public static class HospitalSector5FunctionalTestMenu
{
    /// <summary>CoreSandbox에서 Sector5 테스트를 시작한다.</summary>
    [MenuItem("Haunted House/Verify/39 Functional Tests/Sector 5")]
    public static void RunInPlayMode()
    {
        HospitalSectorTestHarness.Schedule(HospitalSectorId.Sector5);
    }
}
#endif
