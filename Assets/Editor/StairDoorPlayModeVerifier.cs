#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>CoreSandbox의 두 계단 Doorway를 실제 Target 동작으로 양방향 통과해 재베이크 회귀를 검사한다.</summary>
[InitializeOnLoad]
public static class StairDoorPlayModeVerifier
{
    private const string PendingKey = "HauntedHouse.StairDoorPlayModeVerificationPending";

    /// <summary>Play Mode 상태 변경 이벤트를 도메인 리로드 뒤에도 연결한다.</summary>
    static StairDoorPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>계단 상·하행 Target의 실제 인게임 통과 검증을 예약한다.</summary>
    [MenuItem("Haunted House/Verify/Stair Doors Both Directions In Play Mode")]
    public static void Verify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("계단 양방향 검증은 Edit Mode에서 시작해야 합니다.");
            return;
        }

        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Play Mode 진입 후 임시 검증 Runner를 생성한다.</summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        new GameObject("StairDoorPlayModeVerificationRunner").AddComponent<StairDoorPlayModeVerificationRunner>();
    }
}

/// <summary>계단 DoorTarget의 이동 코루틴과 논리 Room 전환을 플레이 프레임에서 검증한다.</summary>
public sealed class StairDoorPlayModeVerificationRunner : MonoBehaviour
{
    private static readonly string[] DoorNames =
    {
        "SlideDoor_ServiceHallwayToStair",
        "SlideDoor_BasementStair"
    };

    /// <summary>씬 초기 복원을 기다린 뒤 두 계단을 각 방향으로 통과하고 결과를 보고한다.</summary>
    private IEnumerator Start()
    {
        yield return null;

        List<string> failures = new();
        Player player = FindFirstObjectByType<Player>(FindObjectsInactive.Include);
        PlayerRoomTracker tracker = FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);

        foreach (EnemyController enemy in FindObjectsByType<EnemyController>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            enemy.SetActive(false);
        }

        Doorway[] stairDoors = FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(doorway => doorway != null && DoorNames.Contains(doorway.name))
            .OrderBy(doorway => doorway.name)
            .ToArray();

        if (player == null || tracker == null)
        {
            failures.Add("Player 또는 PlayerRoomTracker가 없습니다.");
        }
        else if (stairDoors.Length != DoorNames.Length)
        {
            failures.Add($"검증할 계단 Doorway가 {DoorNames.Length}개가 아니라 {stairDoors.Length}개입니다.");
        }
        else
        {
            foreach (Doorway doorway in stairDoors)
            {
                doorway.SetLocked(false);
                yield return VerifyDirection(doorway, doorway.RoomA, player, tracker, failures);
                yield return VerifyDirection(doorway, doorway.RoomB, player, tracker, failures);
            }
        }

        if (failures.Count == 0)
        {
            GameLog.Log("계단 인게임 양방향 검증 성공: 계단 2개, 상·하행 4개 Target 통과와 Room 전환이 모두 정상입니다.", this);
        }
        else
        {
            GameLog.LogError("계단 인게임 양방향 검증 실패:\n- " + string.Join("\n- ", failures), this);
        }

        yield return null;
        EditorApplication.ExitPlaymode();
    }

    /// <summary>지정한 출발 방의 Target을 실행해 실제 이동과 목적지 Room 전환을 확인한다.</summary>
    private static IEnumerator VerifyDirection(
        Doorway doorway,
        Room sourceRoom,
        Player player,
        PlayerRoomTracker tracker,
        List<string> failures)
    {
        if (!doorway.TryGetTraversal(sourceRoom, out DoorTraversal traversal))
        {
            failures.Add($"{doorway.name}/{sourceRoom?.RoomId}: DoorTraversal을 만들 수 없습니다.");
            yield break;
        }

        Target target = doorway.GetComponentsInChildren<Target>(true)
            .FirstOrDefault(candidate => candidate != null && candidate.OwnerRoom == sourceRoom);
        if (target?.Action == null || !NavMesh.SamplePosition(
                target.ApproachPoint.position, out NavMeshHit startHit, 5f, NavMesh.AllAreas))
        {
            failures.Add($"{doorway.name}/{sourceRoom?.RoomId}: 출발 Target 또는 NavMesh 접근점이 유효하지 않습니다.");
            yield break;
        }

        GameObject warpObject = new("StairVerificationWarpPoint");
        warpObject.transform.SetPositionAndRotation(startHit.position, target.ApproachPoint.rotation);
        bool warped = player.WarpTo(warpObject.transform);
        Destroy(warpObject);
        if (!warped)
        {
            failures.Add($"{doorway.name}/{sourceRoom.RoomId}: Player를 출발 NavMesh에 배치하지 못했습니다.");
            yield break;
        }

        tracker.RestoreRoom(sourceRoom);
        yield return null;
        yield return null;

        TargetActionContext context = new(target, player);
        yield return target.Action.Execute(context);
        yield return null;

        string label = $"{doorway.name}/{sourceRoom.RoomId}->{traversal.DestinationRoom.RoomId}";
        if (!context.IsCompleted || !player.LastMoveSucceeded)
        {
            failures.Add($"{label}: 통과 실패. {player.LastMoveFailureReason}");
        }
        else if (tracker.CurrentRoom != traversal.DestinationRoom)
        {
            failures.Add($"{label}: 논리 Room이 목적지로 전환되지 않았습니다.");
        }
    }
}
#endif
