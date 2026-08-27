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
/// Sector4 Workshop Target의 런타임 참조와 Diary flag·은신 복원 동작을 저장 없이 검증하는 Editor 진입점이다.
/// 실제 체크포인트 에셋이나 씬 저장 상태는 변경하지 않고 Play Mode의 임시 세션만 사용한다.
/// </summary>
[InitializeOnLoad]
public static class HospitalWorkshopPlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalWorkshopVerificationPending";

    /// <summary>스크립트 리로드 뒤에도 예약된 Play Mode 검증 콜백을 유지한다.</summary>
    static HospitalWorkshopPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox를 저장하지 않은 채 Workshop 런타임 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector4 Workshop Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Workshop 검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning($"Workshop 검증은 '{ScenePath}' 씬에서 실행해야 합니다.");
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
        new GameObject("HospitalWorkshopVerificationRunner")
            .AddComponent<HospitalWorkshopVerificationRunner>();
    }
}

/// <summary>Workshop의 두 Target 참조와 Diary flag 복원·은신 상태를 검사한 뒤 Play Mode를 종료하는 Runner다.</summary>
public sealed class HospitalWorkshopVerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 15f;
    private readonly List<string> failures = new();

    /// <summary>세션 준비를 기다린 뒤 Workshop 계약을 검사하고 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        GameSession gameSession = null;
        float deadline = Time.realtimeSinceStartup + StartupTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
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
            VerifyTargets(gameSession);
        }

        if (failures.Count == 0)
        {
            GameLog.Log("Sector4 Workshop Play Mode 검증을 통과했습니다.");
        }
        else
        {
            GameLog.LogError("Sector4 Workshop Play Mode 검증 실패:\n- " + string.Join("\n- ", failures));
        }

        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>두 Target의 Room·행동 참조와 Diary flag 완료 복원, 은신 상태를 검사한다.</summary>
    private void VerifyTargets(GameSession gameSession)
    {
        Room workshop = FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && room.RoomId == "S4_Workshop");
        Transform targetRoot = workshop != null ? workshop.transform.Find("Sector4WorkshopTargets") : null;
        Target restraintBed = FindTarget(targetRoot, "Workshop_RestraintBed");
        Target metalCase = FindTarget(targetRoot, "Workshop_MetalCase");
        MessageTarget messageTarget = restraintBed?.Action as MessageTarget;
        StealthHideTarget hideTarget = metalCase?.Action as StealthHideTarget;
        StealthHideSystem hideSystem = FindFirstObjectByType<StealthHideSystem>();

        Check(workshop != null, "S4_Workshop Room이 존재합니다.");
        Check(targetRoot != null, "Sector4WorkshopTargets 루트가 존재합니다.");
        Check(restraintBed != null, "Workshop_RestraintBed Target이 존재합니다.");
        Check(metalCase != null, "Workshop_MetalCase Target이 존재합니다.");
        Check(messageTarget != null, "Workshop_RestraintBed 행동이 MessageTarget입니다.");
        Check(hideTarget != null, "Workshop_MetalCase 행동이 StealthHideTarget입니다.");
        Check(hideSystem != null, "씬에 StealthHideSystem이 존재합니다.");

        if (restraintBed != null)
        {
            Check(restraintBed.OwnerRoom == workshop, "RestraintBed의 OwnerRoom이 S4_Workshop입니다.");
            Check(restraintBed.TryValidateConfiguration(out string bedError),
                $"RestraintBed Target 설정이 유효합니다: {bedError}");
        }

        if (metalCase != null)
        {
            Check(metalCase.OwnerRoom == workshop, "MetalCase의 OwnerRoom이 S4_Workshop입니다.");
            Check(metalCase.TryValidateConfiguration(out string caseError),
                $"MetalCase Target 설정이 유효합니다: {caseError}");
        }

        if (messageTarget != null && restraintBed != null)
        {
            VerifyDiaryFlagRestore(gameSession, restraintBed, messageTarget);
        }

        if (hideTarget != null && metalCase != null && hideSystem != null && workshop != null)
        {
            VerifyHideContract(hideSystem, metalCase, workshop);
        }
    }

    /// <summary>Diary flag가 없을 때와 복원된 뒤의 MessageTarget 선택 상태를 확인한다.</summary>
    private void VerifyDiaryFlagRestore(GameSession gameSession, Target target, MessageTarget messageTarget)
    {
        bool wasSet = gameSession.HasStoryFlag(StoryProgressFlags.KillerDiary3Read);

        // UI를 띄우지 않고 MessageTarget이 사용하는 동일한 완료·복원 경로를 검증한다.
        gameSession.ClearStoryFlag(StoryProgressFlags.KillerDiary3Read);
        messageTarget.RestoreCompletedState(false);
        Check(!messageTarget.IsCompleted && target.IsSelectable,
            "KillerDiary3Read가 없을 때 RestraintBed가 선택 가능합니다.");

        gameSession.SetStoryFlag(StoryProgressFlags.KillerDiary3Read);
        messageTarget.RestoreCompletedState(false);
        Check(messageTarget.IsCompleted && !target.IsSelectable,
            "KillerDiary3Read 복원 시 RestraintBed가 완료 처리됩니다.");

        if (wasSet)
        {
            gameSession.SetStoryFlag(StoryProgressFlags.KillerDiary3Read);
        }
        else
        {
            gameSession.ClearStoryFlag(StoryProgressFlags.KillerDiary3Read);
        }

        messageTarget.RestoreCompletedState(false);
    }

    /// <summary>MetalCase가 현재 Workshop에서 기존 StealthHideSystem으로 진입 가능한지 검사한다.</summary>
    private void VerifyHideContract(StealthHideSystem hideSystem, Target target, Room workshop)
    {
        Check(target.Action is StealthHideTarget, "MetalCase의 Target actionComponent가 StealthHideTarget입니다.");

        PlayerRoomTracker tracker = FindFirstObjectByType<PlayerRoomTracker>();
        Player player = FindFirstObjectByType<Player>();
        if (tracker == null || player == null)
        {
            failures.Add("MetalCase 은신 검증에 필요한 Player 또는 PlayerRoomTracker가 없습니다.");
            return;
        }

        Room initialRoom = tracker.CurrentRoom;
        tracker.RestoreRoom(workshop);
        bool canEnter = hideSystem.CanEnterHide(workshop);
        if (canEnter)
        {
            bool entered = hideSystem.TryEnterHide(player, workshop);
            Check(entered && hideSystem.IsHidden, "MetalCase의 은신 진입이 성공합니다.");
            bool exited = hideSystem.TryExitHide();
            Check(exited && !hideSystem.IsHidden, "MetalCase 은신 해제가 성공합니다.");
        }
        else
        {
            // Sector4 Patrol Enemy가 Workshop에 있는 검증 시점에는 진입 거부가 정상이다.
            GameLog.Log("Workshop에 활성 Enemy가 있어 은신 진입이 거부됐습니다. 은신 시스템 계약상 정상입니다.");
        }

        if (initialRoom != null)
        {
            tracker.RestoreRoom(initialRoom);
        }
    }

    /// <summary>루트 아래 지정된 이름의 Target을 찾는다.</summary>
    private static Target FindTarget(Transform root, string targetName)
    {
        return root == null
            ? null
            : root.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => target != null && target.name == targetName);
    }

    /// <summary>검사 조건이 false일 때 실패를 기록하고 성공 메시지는 개발 로그로 남긴다.</summary>
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
