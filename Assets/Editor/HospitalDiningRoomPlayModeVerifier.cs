#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sector3 DiningRoom Target의 런타임 참조와 FreezerHintRead에 따른 복원 상태를 저장 없이 검증하는 Editor 진입점이다.
/// 실제 씬·체크포인트 에셋은 수정하지 않고 Play Mode의 임시 세션만 사용한다.
/// </summary>
[InitializeOnLoad]
public static class HospitalDiningRoomPlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalDiningRoomVerificationPending";

    /// <summary>스크립트 리로드 뒤에도 예약된 Play Mode 검증 콜백을 유지한다.</summary>
    static HospitalDiningRoomPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox를 저장하지 않은 채 DiningRoom 런타임 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector3 DiningRoom Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("DiningRoom 검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning($"DiningRoom 검증은 '{ScenePath}' 씬에서 실행해야 합니다.");
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
        new GameObject("HospitalDiningRoomVerificationRunner")
            .AddComponent<HospitalDiningRoomVerificationRunner>();
    }
}

/// <summary>DiningRoom의 Target 참조와 Story Flag 기반 완료 복원을 검사하는 임시 런타임 Runner다.</summary>
public sealed class HospitalDiningRoomVerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 15f;
    private readonly List<string> failures = new();

    /// <summary>세션 준비를 기다린 뒤 두 Target과 flag 복원 계약을 검사하고 Play Mode를 종료한다.</summary>
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
            GameLog.Log("Sector3 DiningRoom Play Mode 검증을 통과했습니다.");
        }
        else
        {
            GameLog.LogError("Sector3 DiningRoom Play Mode 검증 실패:\n- " + string.Join("\n- ", failures));
        }

        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>두 조사 Target의 Room·행동 참조와 flag 완료 복원 동작을 검사한다.</summary>
    private void VerifyTargets(GameSession gameSession)
    {
        Room diningRoom = FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && room.RoomId == "S3_DiningRoom");
        Target freezerTarget = FindTarget(diningRoom, "Story_FreezerHintBoard");
        Target diaryTarget = FindTarget(diningRoom, "Story_DiaryPage_Dining");

        Check(diningRoom != null, "S3_DiningRoom 참조가 존재합니다.");
        Check(freezerTarget != null, "Story_FreezerHintBoard Target이 존재합니다.");
        Check(diaryTarget != null, "Story_DiaryPage_Dining Target이 존재합니다.");
        Check(freezerTarget == null || freezerTarget.Action is MessageTarget,
            "Story_FreezerHintBoard 행동이 MessageTarget입니다.");
        Check(diaryTarget == null || diaryTarget.Action is MessageTarget,
            "Story_DiaryPage_Dining 행동이 MessageTarget입니다.");

        if (freezerTarget?.Action is MessageTarget freezerMessage)
        {
            gameSession.ClearStoryFlag(StoryProgressFlags.FreezerHintRead);
            freezerMessage.RestoreCompletedState(false);
            Check(!freezerMessage.IsCompleted && freezerTarget.IsSelectable,
                "FreezerHintRead가 없을 때 힌트보드가 선택 가능합니다.");

            gameSession.SetStoryFlag(StoryProgressFlags.FreezerHintRead);
            freezerMessage.RestoreCompletedState(false);
            Check(freezerMessage.IsCompleted && freezerTarget.IsSelectable,
                "FreezerHintRead 복원 후에도 힌트보드를 다시 선택할 수 있습니다.");
        }
    }

    /// <summary>지정한 Room의 자식 계층에서 Target을 이름으로 찾는다.</summary>
    private static Target FindTarget(Room room, string targetName)
    {
        return room == null
            ? null
            : room.GetComponentsInChildren<Target>(true)
                .FirstOrDefault(target => target != null && target.name == targetName);
    }

    /// <summary>검사 조건이 false일 때 실패 메시지를 누적한다.</summary>
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
