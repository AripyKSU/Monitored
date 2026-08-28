#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Dialogue;
using HauntedHouse.Interaction;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MorgueRoom2 Target의 런타임 참조와 Save5 복원 계약을 Play Mode에서 임시 검증한다.
/// 실제 씬 직렬화 상태는 변경하지 않으며, 상세 상호작용 순서는 수동 Sector4 테스트에서 확인한다.
/// </summary>
[InitializeOnLoad]
public static class HospitalMorgueRoom2PlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalMorgueRoom2VerificationPending";

    /// <summary>스크립트 리로드 뒤에도 Play Mode 진입 콜백을 유지한다.</summary>
    static HospitalMorgueRoom2PlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox에서 MorgueRoom2 Play Mode 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector4 MorgueRoom2 Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("MorgueRoom2 검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning(
                $"MorgueRoom2 검증은 '{ScenePath}' 씬에서 실행해야 합니다. " +
                $"현재 씬: {SceneManager.GetActiveScene().path}");
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
        new GameObject("HospitalMorgueRoom2VerificationRunner")
            .AddComponent<HospitalMorgueRoom2VerificationRunner>();
    }
}

/// <summary>MorgueRoom2 Target의 런타임 계약과 Save5의 진행 데이터를 검사하는 임시 Runner다.</summary>
public sealed class HospitalMorgueRoom2VerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 20f;
    private readonly List<string> failures = new();
    private int passCount;

    /// <summary>세션 초기화 후 MorgueRoom2 Target과 Save5를 검사하고 Play Mode를 종료한다.</summary>
    private IEnumerator Start()
    {
        yield return WaitForGameplayReady();
        VerifyRuntimeReferences();
        VerifySave5Definition();
        ReportResult();
        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>초기 체크포인트 복원이 끝나 GameSession이 Playing이 될 때까지 기다린다.</summary>
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

    /// <summary>MorgueRoom2 Room, Box A·B Target과 Box B 실행 설정을 검사한다.</summary>
    private void VerifyRuntimeReferences()
    {
        Room room = FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.RoomId == "S4_MorgueRoom2");
        Target[] targets = room == null
            ? Array.Empty<Target>()
            : room.GetComponentsInChildren<Target>(true);
        Target boxA = targets.FirstOrDefault(target => target != null && target.name == "MorgueBoxA");
        Target boxB = targets.FirstOrDefault(target => target != null && target.name == "MorgueBoxB");

        Check(room != null, "S4_MorgueRoom2 Room을 찾았습니다.", "S4_MorgueRoom2 Room이 없습니다.");
        Check(boxA != null, "MorgueBoxA Target을 찾았습니다.", "MorgueBoxA Target이 없습니다.");
        Check(boxB != null, "MorgueBoxB Target을 찾았습니다.", "MorgueBoxB Target이 없습니다.");
        Check(boxA == null || boxA.Action is MessageTarget,
            "MorgueBoxA가 MessageTarget입니다.", "MorgueBoxA의 행동이 MessageTarget이 아닙니다.");
        Check(boxB == null || boxB.Action is MorgueBoxBTarget,
            "MorgueBoxB가 MorgueBoxBTarget입니다.", "MorgueBoxB의 행동이 MorgueBoxBTarget이 아닙니다.");

        if (boxB?.Action is MorgueBoxBTarget boxBAction)
        {
            Check(boxBAction.TryValidateConfiguration(out string error),
                "MorgueBoxBTarget 런타임 참조가 유효합니다.", $"MorgueBoxBTarget 설정 오류: {error}");
            Check(boxB.OwnerRoom == room,
                "MorgueBoxB의 OwnerRoom이 MorgueRoom2입니다.", "MorgueBoxB의 OwnerRoom이 잘못됐습니다.");
        }
    }

    /// <summary>Save5가 마스터키·일기 flag와 Sector5Ready를 복원하도록 정의됐는지 검사한다.</summary>
    private void VerifySave5Definition()
    {
        CheckpointDefinition save5 = FindObjectsByType<CheckpointDefinition>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(checkpoint => checkpoint != null && checkpoint.CheckpointId == "Save5");

        Check(save5 != null, "Save5 CheckpointDefinition을 찾았습니다.", "Save5 CheckpointDefinition이 없습니다.");
        if (save5 == null)
        {
            return;
        }

        Check(save5.StoryStage == StoryStage.Sector5Ready,
            "Save5 StoryStage가 Sector5Ready입니다.",
            $"Save5 StoryStage가 Sector5Ready가 아닙니다: {save5.StoryStage}");
        Check(save5.ProgressFlags.Contains(StoryProgressFlags.KillerDiary3Read),
            "Save5가 KillerDiary3Read를 복원합니다.", "Save5에 KillerDiary3Read가 없습니다.");
        Check(save5.ProgressFlags.Contains(StoryProgressFlags.MasterKeyAcquired),
            "Save5가 MasterKeyAcquired를 복원합니다.", "Save5에 MasterKeyAcquired가 없습니다.");
        Check(save5.InventoryItems.Any(item => item != null && item.ItemId == "master_key"),
            "Save5가 MasterKey를 복원합니다.", "Save5 Inventory에 MasterKey가 없습니다.");
        Check(!save5.ProgressFlags.Contains(StoryProgressFlags.FinalChaseStarted),
            "Save5가 FinalChaseStarted를 포함하지 않습니다.",
            "Save5에 FinalChaseStarted가 잘못 포함됐습니다.");
    }

    /// <summary>검증 결과를 Console에 남긴 뒤 실패가 있어도 씬을 원상 복귀한다.</summary>
    private void ReportResult()
    {
        if (failures.Count == 0)
        {
            GameLog.Log($"MorgueRoom2 Play Mode 검증 성공: {passCount}개 항목.", this);
            return;
        }

        GameLog.LogError(
            $"MorgueRoom2 Play Mode 검증 실패: 통과 {passCount}개, 실패 {failures.Count}개\n- " +
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
