#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using HauntedHouse.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sector5 최종 추격 시작 Controller와 Save5 초기 상태를 Play Mode에서 임시 검증한다.
/// 실제 이동·대화 입력은 수동 테스트로 확인하고, 자동 검증은 런타임 참조와 재시작 전제를 검사한다.
/// </summary>
[InitializeOnLoad]
public static class HospitalSector5FinalChasePlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalSector5FinalChaseVerificationPending";

    /// <summary>스크립트 리로드 뒤에도 Play Mode 진입 콜백을 유지한다.</summary>
    static HospitalSector5FinalChasePlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox에서 Sector5 최종 추격 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector5 Final Chase Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("Sector5 최종 추격 검증은 다른 Play Mode 작업이 끝난 뒤 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning($"Sector5 최종 추격 검증은 '{ScenePath}' 씬에서 실행해야 합니다.");
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
        new GameObject("HospitalSector5FinalChaseVerificationRunner")
            .AddComponent<HospitalSector5FinalChaseVerificationRunner>();
    }
}

/// <summary>Sector5 Controller 런타임 참조와 Save5의 FinalChase 미시작 조건을 검사하는 Runner다.</summary>
public sealed class HospitalSector5FinalChaseVerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 20f;
    private readonly List<string> failures = new();
    private int passCount;

    /// <summary>세션 초기화 후 참조와 초기 flag를 검사하고 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        yield return WaitForGameplayReady();
        VerifyController();
        VerifyInitialFinalChaseState();
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

    /// <summary>Controller의 런타임 참조와 설정 계약을 검사한다.</summary>
    private void VerifyController()
    {
        Sector5FinalChaseController controller = FindFirstObjectByType<Sector5FinalChaseController>(
            FindObjectsInactive.Include);
        Check(controller != null, "Sector5FinalChaseController를 찾았습니다.",
            "Sector5FinalChaseController가 없습니다.");
        if (controller == null)
        {
            return;
        }

        Check(controller.TryValidateConfiguration(out string error),
            "Sector5FinalChaseController 런타임 참조가 유효합니다.",
            $"Sector5FinalChaseController 설정 오류: {error}");
    }

    /// <summary>Play Mode 시작 시 Final Chase가 자동으로 시작하지 않는지 검사한다.</summary>
    private void VerifyInitialFinalChaseState()
    {
        GameSession session = FindFirstObjectByType<GameSession>();
        Check(session != null && !session.HasStoryFlag(StoryProgressFlags.FinalChaseStarted),
            "Play Mode 시작 직후 FinalChaseStarted가 꺼져 있습니다.",
            "Play Mode 시작 직후 FinalChaseStarted가 자동으로 켜졌습니다.");
        Check(session != null && session.CurrentStoryStage != StoryStage.FinalChase,
            "Play Mode 시작 직후 StoryStage가 FinalChase가 아닙니다.",
            "Play Mode 시작 직후 StoryStage가 FinalChase로 잘못 설정됐습니다.");
    }

    /// <summary>검증 결과를 Console에 남긴다.</summary>
    private void ReportResult()
    {
        if (failures.Count == 0)
        {
            GameLog.Log($"Sector5 최종 추격 시작 Play Mode 정적 검증 성공: {passCount}개 항목.", this);
            return;
        }

        GameLog.LogError(
            $"Sector5 최종 추격 시작 Play Mode 검증 실패: 통과 {passCount}개, 실패 {failures.Count}개\n- " +
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
