#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using HauntedHouse.Core;
using HauntedHouse.Dialogue;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sector5 정문 도착 Controller의 참조와 Save5 초기 상태를 Play Mode에서 검증한다.
/// 실제 이동·추격 중 안내 표시·재진입은 수동 시나리오로 이어서 확인한다.
/// </summary>
[InitializeOnLoad]
public static class HospitalSector5EntrancePlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalSector5EntranceVerificationPending";

    /// <summary>스크립트 리로드 후에도 Play Mode 상태 변경 콜백을 유지한다.</summary>
    static HospitalSector5EntrancePlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox에서 정문 도착 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector5 Entrance Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Sector5 정문 검증은 다른 Play Mode 작업이 끝난 뒤 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning($"Sector5 정문 검증은 '{ScenePath}' 씬에서 실행해야 합니다.");
            return;
        }

        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Play Mode 진입 직후 임시 검증 Runner를 한 번 생성한다.</summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        new GameObject("HospitalSector5EntranceVerificationRunner")
            .AddComponent<HospitalSector5EntranceVerificationRunner>();
    }
}

/// <summary>정문 Controller의 런타임 참조와 도착 flag 초기 상태를 검사하는 Runner다.</summary>
public sealed class HospitalSector5EntranceVerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 20f;
    private readonly List<string> failures = new();
    private int passCount;

    /// <summary>세션 초기화 후 정문 구성과 초기 flag를 검사하고 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        yield return WaitForGameplayReady();
        VerifyController();
        VerifyInitialEntranceState();
        ReportResult();
        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>GameSession이 Playing이 될 때까지 기다린다.</summary>
    private IEnumerator WaitForGameplayReady()
    {
        float deadline = Time.realtimeSinceStartup + StartupTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            GameSession session = FindFirstObjectByType<GameSession>();
            if (session != null && session.Phase == GameSessionPhase.Playing)
            {
                Pass("GameSession이 Playing 상태로 초기화되었습니다.");
                yield break;
            }

            yield return null;
        }

        Fail("GameSession이 제한 시간 안에 Playing 상태가 되지 않았습니다.");
    }

    /// <summary>Controller의 정문 Room·Dialogue 참조와 실행 설정을 검사한다.</summary>
    private void VerifyController()
    {
        Sector5EntranceController controller = FindFirstObjectByType<Sector5EntranceController>(
            FindObjectsInactive.Include);
        Check(controller != null, "Sector5EntranceController를 찾았습니다.",
            "Sector5EntranceController가 없습니다.");
        if (controller == null)
        {
            return;
        }

        Check(controller.EntranceRoom != null && controller.EntranceRoom.RoomId == "S5_DoorArea",
            "정문 Controller가 S5_DoorArea를 참조합니다.",
            "정문 Controller의 Room 참조가 S5_DoorArea가 아닙니다.");
        Check(controller.EntranceDialogue != null && !controller.EntranceDialogue.PauseGame,
            "정문 안내 Dialogue가 Non-Pause 설정입니다.",
            "정문 안내 Dialogue가 Pause 설정입니다.");
        Check(controller.EntranceDialogue != null && controller.EntranceDialogue.Lines.Count > 0 &&
              controller.EntranceDialogue.Lines[0].Text == "[왼쪽 방]",
            "정문 안내 Dialogue 문구가 '[왼쪽 방]'입니다.",
            "정문 안내 Dialogue 문구가 올바르지 않습니다.");
    }

    /// <summary>Save5 복원 직후 정문 도착이 자동 처리되지 않는지 검사한다.</summary>
    private void VerifyInitialEntranceState()
    {
        GameSession session = FindFirstObjectByType<GameSession>();
        Check(session != null && !session.HasStoryFlag(StoryProgressFlags.EntranceReached),
            "Play Mode 시작 직후 EntranceReached가 꺼져 있습니다.",
            "Play Mode 시작 직후 EntranceReached가 켜져 있습니다.");
    }

    /// <summary>검증 결과를 GameLog에 남긴다.</summary>
    private void ReportResult()
    {
        if (failures.Count == 0)
        {
            GameLog.Log($"Sector5 정문 도착 Play Mode 정적 검증 성공: {passCount}개 항목.", this);
            return;
        }

        GameLog.LogError(
            $"Sector5 정문 도착 Play Mode 검증 실패: 통과 {passCount}개, 실패 {failures.Count}개\n- " +
            string.Join("\n- ", failures), this);
    }

    /// <summary>검사 조건이 참이면 통과 수를, 거짓이면 실패 메시지를 기록한다.</summary>
    private void Check(bool condition, string passMessage, string failureMessage)
    {
        if (condition)
        {
            passCount++;
            GameLog.Log(passMessage, this);
            return;
        }

        failures.Add(failureMessage);
    }

    /// <summary>실패 메시지를 누적한다.</summary>
    private void Fail(string message)
    {
        failures.Add(message);
    }

    /// <summary>대기 조건이 충족된 항목을 통과 처리한다.</summary>
    private void Pass(string message)
    {
        passCount++;
        GameLog.Log(message, this);
    }
}
#endif
