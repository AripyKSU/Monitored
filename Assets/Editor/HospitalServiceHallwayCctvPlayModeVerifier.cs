#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ServiceHallway 동적 CCTV를 저장 없이 Play Mode에서 양방향으로 검증하는 Editor 진입점이다.
/// 다른 Sector3 검증기와 파일을 공유하지 않아 동시 작업 시 충돌 범위를 줄인다.
/// </summary>
[InitializeOnLoad]
public static class HospitalServiceHallwayCctvPlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalServiceHallwayCctvVerificationPending";

    /// <summary>스크립트 리로드 뒤에도 예약된 Play Mode 검증 콜백을 유지한다.</summary>
    static HospitalServiceHallwayCctvPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>현재 CoreSandbox에서 저장하지 않는 CCTV Play Mode 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Hospital Sector3 ServiceHallway CCTV Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("ServiceHallway CCTV 검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            GameLog.LogWarning($"ServiceHallway CCTV 검증은 '{ScenePath}' 씬에서 실행해야 합니다.");
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
        new GameObject("HospitalServiceHallwayCctvVerificationRunner")
            .AddComponent<HospitalServiceHallwayCctvVerificationRunner>();
    }
}

/// <summary>ServiceHallway CCTV의 Pose 보간·진입 방향·퇴실 정지를 검사하는 임시 런타임 Runner다.</summary>
public sealed class HospitalServiceHallwayCctvVerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 15f;
    private const float PositionTolerance = 0.05f;
    private const float RotationTolerance = 2f;
    private readonly List<string> failures = new();

    /// <summary>세션 준비를 기다린 뒤 동적 CCTV 계약을 검사하고 Play Mode를 종료한다.</summary>
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
            yield return VerifyCctv();
        }

        if (failures.Count == 0)
        {
            GameLog.Log("Sector3 ServiceHallway 동적 CCTV Play Mode 검증을 통과했습니다.");
        }
        else
        {
            GameLog.LogError("Sector3 ServiceHallway 동적 CCTV Play Mode 검증 실패:\n- " +
                            string.Join("\n- ", failures));
        }

        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    /// <summary>Kitchen·CenterHallway_B 양방향 진입과 CCTV 왕복·퇴실 정지를 순서대로 검사한다.</summary>
    private IEnumerator VerifyCctv()
    {
        Room serviceHallway = FindRoom("S3_HallwayB");
        Room kitchen = FindRoom("S3_Kitchen");
        Room centerHallwayB = FindRoom("CenterHallway_B");
        PlayerRoomTracker tracker = FindFirstObjectByType<PlayerRoomTracker>();
        CctvPosePatrol patrol = serviceHallway?.RoomCamera?.GetComponent<CctvPosePatrol>();

        Check(serviceHallway != null, "S3_HallwayB Room이 존재합니다.");
        Check(kitchen != null, "S3_Kitchen Room이 존재합니다.");
        Check(centerHallwayB != null, "CenterHallway_B Room이 존재합니다.");
        Check(tracker != null, "PlayerRoomTracker가 존재합니다.");
        Check(patrol != null, "ServiceHallway CCTV에 CctvPosePatrol이 존재합니다.");
        if (serviceHallway == null || kitchen == null || centerHallwayB == null ||
            tracker == null || patrol == null)
        {
            yield break;
        }

        Check(patrol.PlayerRoomTracker == tracker, "CctvPosePatrol의 PlayerRoomTracker 참조가 일치합니다.");
        Check(patrol.ServiceHallwayRoom == serviceHallway, "CctvPosePatrol의 ServiceHallway 참조가 일치합니다.");
        Check(patrol.KitchenRoom == kitchen, "CctvPosePatrol의 Kitchen 참조가 일치합니다.");
        Check(patrol.CenterHallwayBRoom == centerHallwayB, "CctvPosePatrol의 CenterHallway_B 참조가 일치합니다.");

        // 첫 번째 경로는 Kitchen에서 진입해 Pose A에서 시작해야 한다.
        Check(tracker.RestoreRoom(kitchen), "검증 시작 방을 Kitchen으로 설정했습니다.");
        Check(tracker.EnterRoom(serviceHallway), "Kitchen에서 ServiceHallway로 진입했습니다.");
        // EnterRoom 이벤트가 동기적으로 Pose A를 적용하므로 첫 프레임 전에 시작점을 검사한다.
        Check(IsNear(patrol.transform, patrol.PoseA),
            "Kitchen 진입 직후 CCTV가 Pose A입니다. " + DescribePose(patrol.transform, patrol.PoseA));

        yield return WaitScaledSeconds(patrol.MoveDuration * 0.5f);
        if (patrol.MoveDuration > 0f)
        {
            Check(!IsNear(patrol.transform, patrol.PoseA), "Pose A에서 Pose B 방향으로 위치·회전이 보간됩니다.");
        }

        // 앞선 절반 구간을 제외하고 남은 절반보다 조금 더 기다려 B 도착·대기 구간을 검사한다.
        yield return WaitScaledSeconds(patrol.MoveDuration * 0.6f + 0.1f);
        Check(IsNear(patrol.transform, patrol.PoseB),
            "첫 번째 보간이 Pose B에서 종료됩니다. " + DescribePose(patrol.transform, patrol.PoseB));

        yield return WaitScaledSeconds(patrol.EndWaitDuration + patrol.MoveDuration + 0.1f);
        Check(IsNear(patrol.transform, patrol.PoseA),
            "끝점 대기 후 Pose A로 왕복합니다. " + DescribePose(patrol.transform, patrol.PoseA));

        // ServiceHallway를 벗어나면 현재 Transform에서 더 이상 갱신하지 않아야 한다.
        Check(tracker.EnterRoom(centerHallwayB), "ServiceHallway에서 CenterHallway_B로 퇴실했습니다.");
        Vector3 exitPosition = patrol.transform.position;
        Quaternion exitRotation = patrol.transform.rotation;
        yield return null;
        yield return null;
        Check(Vector3.Distance(exitPosition, patrol.transform.position) <= PositionTolerance &&
              Quaternion.Angle(exitRotation, patrol.transform.rotation) <= RotationTolerance,
            "ServiceHallway 퇴실 후 CCTV 순찰이 중지됩니다.");
        Check(!patrol.IsPatrolling, "ServiceHallway 퇴실 후 순찰 Coroutine이 종료됩니다.");

        // 반대쪽에서 다시 진입하면 Pose B로 재초기화해야 한다.
        Check(tracker.EnterRoom(serviceHallway), "CenterHallway_B에서 ServiceHallway로 재진입했습니다.");
        Check(IsNear(patrol.transform, patrol.PoseB), "CenterHallway_B 진입 직후 CCTV가 Pose B입니다.");
        yield return WaitScaledSeconds(patrol.MoveDuration + 0.1f);
        Check(IsNear(patrol.transform, patrol.PoseA), "반대 방향 재진입 후 Pose A로 이동합니다.");
    }

    /// <summary>지정한 초만큼 일반 scaled time을 기다린다.</summary>
    private static IEnumerator WaitScaledSeconds(float seconds)
    {
        if (seconds <= 0f)
        {
            yield return null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>Pose의 위치와 회전이 허용 오차 안에 있는지 검사한다.</summary>
    private static bool IsNear(Transform target, CctvPosePatrol.PoseData pose)
    {
        return target != null &&
               Vector3.Distance(target.position, pose.Position) <= PositionTolerance &&
               Quaternion.Angle(target.rotation, pose.Rotation) <= RotationTolerance;
    }

    /// <summary>Pose 검증 실패 시 실제 Transform과 목표 Pose의 차이를 문자열로 만든다.</summary>
    private static string DescribePose(Transform target, CctvPosePatrol.PoseData pose)
    {
        if (target == null)
        {
            return "actual=null";
        }

        return $"actualPos={target.position}, targetPos={pose.Position}, " +
               $"posDelta={Vector3.Distance(target.position, pose.Position):0.000}, " +
               $"actualRot={target.rotation.eulerAngles}, targetRot={pose.Rotation.eulerAngles}, " +
               $"rotDelta={Quaternion.Angle(target.rotation, pose.Rotation):0.000}";
    }

    /// <summary>RoomId로 씬의 Room을 찾는다.</summary>
    private static Room FindRoom(string roomId)
    {
        return FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && room.RoomId == roomId);
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
