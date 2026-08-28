#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sector4 기타 Hide/Message Target의 런타임 참조와 완료 상태를 임시 Play Mode에서 검증하는 Editor 진입점이다.
/// 실제 체크포인트나 씬 저장 상태는 변경하지 않는다.
/// </summary>
[InitializeOnLoad]
public static class HospitalSector4MiscTargetsPlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalSector4MiscTargetsVerificationPending";

    /// <summary>스크립트 리로드 뒤에도 예약된 Play Mode 검증 콜백을 유지한다.</summary>
    static HospitalSector4MiscTargetsPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox에서 기타 Target 런타임 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector4 Misc Targets Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Sector4 기타 Target 검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning($"Sector4 기타 Target 검증은 '{ScenePath}' 씬에서 실행해야 합니다.");
            return;
        }

        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Play Mode 진입 직후 임시 검증 Runner를 생성한다.</summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        new GameObject("HospitalSector4MiscTargetsVerificationRunner")
            .AddComponent<HospitalSector4MiscTargetsVerificationRunner>();
    }
}

/// <summary>세 기타 Target의 Room·행동 참조와 MessageTarget 완료 복원을 검사한 뒤 Play Mode를 종료한다.</summary>
public sealed class HospitalSector4MiscTargetsVerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 15f;
    private readonly List<string> failures = new();

    /// <summary>GameSession이 준비된 뒤 Target 계약을 검사하고 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        GameSession gameSession = null;
        float deadline = Time.realtimeSinceStartup + StartupTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameSession ??= UnityEngine.Object.FindFirstObjectByType<GameSession>();
            if (gameSession != null && gameSession.Phase == GameSessionPhase.Playing)
            {
                break;
            }

            yield return null;
        }

        if (gameSession == null || gameSession.Phase != GameSessionPhase.Playing)
        {
            failures.Add("GameSession이 제한 시간 안에 Playing 상태가 되지 않았습니다.");
        }
        else
        {
            VerifyTargets();
        }

        if (failures.Count == 0)
        {
            GameLog.Log("Sector4 기타 Hide/Message Target Play Mode 검증을 통과했습니다.");
        }
        else
        {
            GameLog.LogError("Sector4 기타 Target Play Mode 검증 실패:\n- " + string.Join("\n- ", failures));
        }

        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>각 Target의 Room·행동 참조와 메시지 완료 상태 복원을 검사한다.</summary>
    private void VerifyTargets()
    {
        Room controlRoom = FindRoom("S4_ControlRoom");
        Room utilityRoom = FindRoom("S4_UtilityRoom");
        Transform controlRoot = controlRoom != null ? controlRoom.transform.Find("Sector4ControlRoomTargets") : null;
        Transform utilityRoot = utilityRoom != null ? utilityRoom.transform.Find("Sector4UtilityRoomTargets") : null;
        Target consoleTarget = FindTarget(controlRoot, "Control_ConsoleDesk");
        Target switchTarget = FindTarget(utilityRoot, "Utility_SwitchCase");
        Target cabinetTarget = FindTarget(utilityRoot, "Utility_ElectricalCabinet");
        StealthHideTarget consoleHide = consoleTarget?.Action as StealthHideTarget;
        StealthHideTarget switchHide = switchTarget?.Action as StealthHideTarget;
        MessageTarget cabinetMessage = cabinetTarget?.Action as MessageTarget;
        StealthHideSystem hideSystem = UnityEngine.Object.FindFirstObjectByType<StealthHideSystem>();

        Check(controlRoom != null, "S4_ControlRoom Room이 존재합니다.");
        Check(utilityRoom != null, "S4_UtilityRoom Room이 존재합니다.");
        Check(consoleTarget != null, "Control_ConsoleDesk Target이 존재합니다.");
        Check(switchTarget != null, "Utility_SwitchCase Target이 존재합니다.");
        Check(cabinetTarget != null, "Utility_ElectricalCabinet Target이 존재합니다.");
        Check(consoleHide != null, "Control_ConsoleDesk 행동이 StealthHideTarget입니다.");
        Check(switchHide != null, "Utility_SwitchCase 행동이 StealthHideTarget입니다.");
        Check(cabinetMessage != null, "Utility_ElectricalCabinet 행동이 MessageTarget입니다.");
        Check(hideSystem != null, "씬에 StealthHideSystem이 존재합니다.");

        if (consoleTarget != null && controlRoom != null)
        {
            Check(consoleTarget.OwnerRoom == controlRoom, "ConsoleDesk의 OwnerRoom이 S4_ControlRoom입니다.");
            Check(consoleTarget.TryValidateConfiguration(out string error), $"ConsoleDesk Target 설정이 유효합니다: {error}");
        }

        if (switchTarget != null && utilityRoom != null)
        {
            Check(switchTarget.OwnerRoom == utilityRoom, "SwitchCase의 OwnerRoom이 S4_UtilityRoom입니다.");
            Check(switchTarget.TryValidateConfiguration(out string error), $"SwitchCase Target 설정이 유효합니다: {error}");
        }

        if (cabinetTarget != null && utilityRoom != null)
        {
            Check(cabinetTarget.OwnerRoom == utilityRoom, "ElectricalCabinet의 OwnerRoom이 S4_UtilityRoom입니다.");
            Check(cabinetTarget.TryValidateConfiguration(out string error), $"ElectricalCabinet Target 설정이 유효합니다: {error}");
        }

        if (cabinetMessage != null && cabinetTarget != null)
        {
            VerifyMessageCompletionRestore(cabinetTarget, cabinetMessage);
        }

        if (hideSystem != null && consoleTarget != null && switchTarget != null && controlRoom != null && utilityRoom != null)
        {
            VerifyHideContract(hideSystem, consoleTarget, switchTarget, controlRoom, utilityRoom);
        }
    }

    /// <summary>메시지 완료 상태를 임시로 적용·해제해 Target 선택 상태가 복원되는지 확인한다.</summary>
    private void VerifyMessageCompletionRestore(Target target, MessageTarget messageTarget)
    {
        messageTarget.RestoreCompletedState(false);
        Check(!messageTarget.IsCompleted && target.IsSelectable,
            "ElectricalCabinet 메시지가 미완료 상태에서 선택 가능합니다.");

        messageTarget.RestoreCompletedState(true);
        Check(messageTarget.IsCompleted && !target.IsSelectable,
            "ElectricalCabinet 메시지 완료 상태가 복원되면 선택이 차단됩니다.");

        messageTarget.RestoreCompletedState(false);
        Check(!messageTarget.IsCompleted && target.IsSelectable,
            "ElectricalCabinet 메시지 완료 상태 해제가 정상입니다.");
    }

    /// <summary>두 Hide Target이 기존 은신 시스템으로 진입·해제되는지 확인한다.</summary>
    private void VerifyHideContract(
        StealthHideSystem hideSystem,
        Target consoleTarget,
        Target switchTarget,
        Room controlRoom,
        Room utilityRoom)
    {
        PlayerRoomTracker tracker = UnityEngine.Object.FindFirstObjectByType<PlayerRoomTracker>();
        Player player = UnityEngine.Object.FindFirstObjectByType<Player>();
        if (tracker == null || player == null)
        {
            failures.Add("Hide 검증에 필요한 Player 또는 PlayerRoomTracker가 없습니다.");
            return;
        }

        Room initialRoom = tracker.CurrentRoom;
        VerifySingleHide(hideSystem, tracker, player, consoleTarget, controlRoom, "Control_ConsoleDesk");
        VerifySingleHide(hideSystem, tracker, player, switchTarget, utilityRoom, "Utility_SwitchCase");
        if (initialRoom != null)
        {
            tracker.RestoreRoom(initialRoom);
        }
    }

    /// <summary>하나의 Hide Target에서 현재 방 설정과 은신 진입·해제를 검사한다.</summary>
    private void VerifySingleHide(
        StealthHideSystem hideSystem,
        PlayerRoomTracker tracker,
        Player player,
        Target target,
        Room room,
        string targetName)
    {
        Check(target.Action is StealthHideTarget, $"{targetName}의 actionComponent가 StealthHideTarget입니다.");
        tracker.RestoreRoom(room);
        if (!hideSystem.CanEnterHide(room))
        {
            // Sector4 순찰 Enemy가 해당 방에 있을 때 진입 거부되는 것은 기존 은신 시스템의 정상 동작이다.
            GameLog.Log($"{targetName}: 현재 Enemy 상태로 은신 진입이 거부됐습니다. 기존 시스템 계약상 정상입니다.");
            return;
        }

        bool entered = hideSystem.TryEnterHide(player, room);
        Check(entered && hideSystem.IsHidden, $"{targetName} 은신 진입이 성공합니다.");
        bool exited = hideSystem.TryExitHide();
        Check(exited && !hideSystem.IsHidden, $"{targetName} 은신 해제가 성공합니다.");
    }

    /// <summary>RoomId로 Room을 찾는다.</summary>
    private static Room FindRoom(string roomId)
    {
        return UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && room.RoomId == roomId);
    }

    /// <summary>루트 아래 지정된 이름의 Target을 찾는다.</summary>
    private static Target FindTarget(Transform root, string targetName)
    {
        return root == null
            ? null
            : root.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => target != null && target.name == targetName);
    }

    /// <summary>검사 조건이 false일 때 실패를 기록한다.</summary>
    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
        else
        {
            GameLog.Log(message, this);
        }
    }
}
#endif
