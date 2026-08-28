#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 39번 Sector2 기능 테스트를 번호별로 실행한다.
/// 발전기·철창·강제 숨기 Controller의 계약과 현재 복원 상태는 자동 검사하고 실제 연출은 수동 단계로 보고한다.
/// </summary>
public static class HospitalSector2FunctionalTest
{
    /// <summary>Sector2의 17개 테스트 항목을 문서 순서대로 실행한다.</summary>
    public static IEnumerator Run(HospitalSectorTestContext context)
    {
        Room storage = context.FindRoom("S2_Storage");
        Room machineRoom = context.FindRoom("S2_MachineRoom");
        Room centerB = context.FindRoom("CenterHallway_B");
        Room centerA = context.FindRoom("CenterHallway_A");
        Target fuseTarget = context.FindTarget("S2_Storage_TargetB_Fuse");
        Target machineTarget = context.FindTarget("S2_MachineRoom_Tank_1");
        ItemUseTarget generatorUseTarget = context.FindNamed<ItemUseTarget>("Generator_Boiler");
        GateController gate = UnityEngine.Object.FindFirstObjectByType<GateController>(FindObjectsInactive.Include);
        GeneratorPowerController generator = UnityEngine.Object.FindFirstObjectByType<GeneratorPowerController>(FindObjectsInactive.Include);
        Sector2HideEncounterController encounter = UnityEngine.Object.FindFirstObjectByType<Sector2HideEncounterController>(FindObjectsInactive.Include);
        RoomEntryEvent gateDiscoveryEntry = centerA != null
            ? centerA.GetComponentsInChildren<RoomEntryEvent>(true)
                .FirstOrDefault(entry => entry != null && entry.name == "S2_CenterHallwayGateDiscoveryEntry")
            : null;

        context.CheckRoom("Storage", storage, "S2_Storage", 1);
        context.Check(gateDiscoveryEntry != null && gateDiscoveryEntry.OnEntered != null &&
                      gateDiscoveryEntry.OnEntered.GetPersistentEventCount() > 0,
            "1. Gate 발견 Story 진입 이벤트가 연결됐습니다.",
            "1. Gate 발견 Story 진입 이벤트를 찾지 못했습니다.");

        context.Check(fuseTarget != null && fuseTarget.GetComponent<ItemPickupTarget>() != null,
            "2. Storage Fuse Target이 ItemPickupTarget입니다.",
            "2. Storage Fuse Target이 없거나 ItemPickupTarget이 아닙니다.");
        context.Check(machineTarget != null && machineTarget.Action is MachineRoomTarget,
            "3. MachineRoom 장치가 MachineRoomTarget으로 구성됐습니다.",
            "3. MachineRoom 장치 Target이 없거나 MachineRoomTarget이 아닙니다.");
        context.Check(generatorUseTarget != null && generatorUseTarget.GetComponent<Target>() != null &&
                      generator != null,
            "3. 발전기 Generator_Boiler에 ItemUseTarget과 GeneratorPowerController가 존재합니다.",
            "3. 발전기 Generator_Boiler의 ItemUseTarget 또는 GeneratorPowerController가 없습니다.");
        context.Manual(3, "Fuse 설치 전 Machine을 선택해 실패 분기와 메시지를 확인한다.");

        string itemUseError = string.Empty;
        bool itemUseIsValid = generatorUseTarget != null &&
                              generatorUseTarget.TryValidateConfiguration(out itemUseError);
        context.Check(itemUseIsValid,
            "4. Fuse 설치용 Item Use 경로가 연결됐습니다.",
            $"4. Fuse 설치용 Item Use 경로가 유효하지 않습니다: {itemUseError}");
        context.Check(generator != null && generator.IsPowered ==
                      (context.Session != null && context.Session.HasStoryFlag(StoryProgressFlags.GeneratorPowered)),
            "5. GeneratorPowerController가 GeneratorPowered flag와 동기화됩니다.",
            "5. GeneratorPowerController와 GeneratorPowered flag가 불일치합니다.");

        if (gate != null)
        {
            bool gateIsValid = gate.TryValidateConfiguration(out string gateError);
            context.Check(gateIsValid,
                "6. GateController의 Doorway·Root 구성이 유효합니다.",
                $"6. GateController 구성이 유효하지 않습니다: {gateError}");
            context.Check(gate.IsActivated == (context.Session != null &&
                                               context.Session.HasStoryFlag(StoryProgressFlags.GateActivated)),
                "6. Gate SFX/Shake 이후 반영될 GateActivated 상태가 동기화됩니다.",
                "6. GateActivated flag와 GateController 상태가 다릅니다.");
        }
        else
        {
            context.Fail("6. GateController를 찾지 못했습니다.");
        }

        context.CheckRoom("MachineRoom", machineRoom, "S2_MachineRoom", 7);
        context.Check(encounter != null,
            "8. Sector2 강제 숨기 Encounter Controller가 존재합니다.",
            "8. Sector2 강제 숨기 Encounter Controller를 찾지 못했습니다.");
        if (encounter != null)
        {
            bool encounterIsValid = encounter.TryValidateConfiguration(out string encounterError);
            context.Check(encounterIsValid,
                "8. Killer 등장 ScriptedSequence 참조가 유효합니다.",
                $"8. Killer 등장 ScriptedSequence 구성이 유효하지 않습니다: {encounterError}");
            context.Check(encounter.ShouldStartEncounter ==
                          (context.Session != null && context.Session.HasStoryFlag(StoryProgressFlags.GateActivated) &&
                           !context.Session.HasStoryFlag(StoryProgressFlags.Sector2HideEncounterCompleted)),
                "9. 강제 숨기 이벤트 시작 조건이 Gate/완료 flag와 일치합니다.",
                "9. 강제 숨기 이벤트 시작 조건이 예상과 다릅니다.");
        }

        context.Check(context.RoomTracker != null,
            "10. PlayerRoomTracker 참조가 존재합니다.",
            "10. PlayerRoomTracker를 찾지 못했습니다.");
        context.Manual(9, "MachineRoom을 나가 Killer 등장 및 자동 복귀 연출을 실제로 확인한다.");
        context.Manual(10, "자동 복귀 직후 PlayerRoomTracker.CurrentRoom이 MachineRoom인지 확인한다.");
        context.Manual(11, "Hide Target을 선택해 은신 성공 분기를 확인한다.");
        context.Manual(12, "Killer 퇴장 후 Encounter 완료와 재실행 차단을 확인한다.");

        context.CheckRoom("CenterHallway_B", centerB, "CenterHallway_B", 13);
        context.CheckCheckpoint("Save3", StoryStage.Sector3, "CenterHallway_B", 14);
        context.Manual(15, "Save3에서 Restart를 실행해 복원 과정을 확인한다.");
        context.Check(context.Session == null || !context.Session.HasStoryFlag(StoryProgressFlags.Sector2HideEncounterCompleted) ||
                      encounter == null || encounter.StateName == "Completed" || encounter.StateName == "Idle",
            "16. 완료된 Hide Event가 Restart 직후 재생되지 않는 상태입니다.",
            "16. Restart 후 Hide Event 상태가 재생 가능한 상태로 남아 있습니다.");

        EnemyController enemy = UnityEngine.Object.FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
        context.Check(enemy != null,
            "17. Sector3 Patrol 대상 Enemy가 존재합니다.",
            "17. Sector3 Patrol 대상 Enemy를 찾지 못했습니다.");
        if (enemy != null && context.Session != null && context.Session.CurrentStoryStage == StoryStage.Sector3)
        {
            context.Check(enemy.Mode == EnemyMode.Patrol || enemy.Mode == EnemyMode.Inactive,
                "17. Sector3 진입 상태에서 Enemy가 Patrol 계약을 사용합니다.",
                $"17. Sector3 진입 후 Enemy Mode가 예상과 다릅니다: {enemy.Mode}");
        }

        yield return null;
    }
}

/// <summary>Sector2 기능 테스트를 예약하는 전용 Editor 메뉴다.</summary>
public static class HospitalSector2FunctionalTestMenu
{
    /// <summary>CoreSandbox에서 Sector2 테스트를 시작한다.</summary>
    [MenuItem("Haunted House/Verify/39 Functional Tests/Sector 2")]
    public static void RunInPlayMode()
    {
        HospitalSectorTestHarness.Schedule(HospitalSectorId.Sector2);
    }
}
#endif
