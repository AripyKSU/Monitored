using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using HauntedHouse.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// CoreSandbox의 모든 Door_V3·SlideDoor를 양방향으로 실제 통과하며 이동, 문 이격, CCTV 가시성을 검사한다.
/// 에디터 메뉴에서 시작한 검증만 Play Mode 진입 직후 자동 실행하고 완료되면 Play Mode를 종료한다.
/// </summary>
[InitializeOnLoad]
public static class CoreSandboxDoorPlayModeVerifier
{
    private const string PendingKey = "HauntedHouse.CoreSandboxDoorPlayModeVerificationPending";

    /// <summary>도메인 리로드 뒤에도 Play Mode 진입 이벤트를 받을 수 있도록 콜백을 등록한다.</summary>
    static CoreSandboxDoorPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    /// <summary>CoreSandbox의 모든 지원 문에 대한 양방향 인게임 검증을 시작한다.</summary>
    [MenuItem("Haunted House/Verify CoreSandbox Prefab Doors In Play Mode")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogWarning("문 플레이 검증은 Edit Mode에서 시작해야 합니다.");
            return;
        }

        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>요청된 Play Mode가 준비되면 런타임 코루틴 실행기를 생성한다.</summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        GameObject runnerObject = new GameObject("CoreSandboxDoorPlayModeVerificationRunner");
        runnerObject.AddComponent<CoreSandboxDoorPlayModeVerificationRunner>();
    }
}

/// <summary>
/// 플레이 프레임에서 각 DoorTarget의 실제 코루틴을 실행하고 결과를 누적하는 임시 검증 컴포넌트다.
/// </summary>
public sealed class CoreSandboxDoorPlayModeVerificationRunner : MonoBehaviour
{
    private const float PositionSampleRadius = 5f;
    private const float ClearanceMargin = 0.2f;

    /// <summary>씬 초기화가 끝난 다음 전체 양방향 검증 코루틴을 시작한다.</summary>
    private IEnumerator Start()
    {
        yield return null;
        yield return VerifyAllDoors();
    }

    /// <summary>지원 프리팹 앵커가 있는 모든 Doorway를 수집해 양방향으로 실제 통과한다.</summary>
    private IEnumerator VerifyAllDoors()
    {
        List<string> failures = new List<string>();
        Player player = FindFirstObjectByType<Player>(FindObjectsInactive.Include);
        PlayerRoomTracker tracker = FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        CameraDirector cameraDirector = FindFirstObjectByType<CameraDirector>(FindObjectsInactive.Include);
        Doorway[] doorways = FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(doorway => doorway.transform.Find("TraversalAnchors") != null)
            .OrderBy(doorway => doorway.name)
            .ToArray();

        // 문 검증 도중 적 조우로 게임이 일시정지되어 이동 코루틴이 멈추지 않도록 적만 임시 비활성화한다.
        foreach (HauntedHouse.Enemies.EnemyController enemy in
                 FindObjectsByType<HauntedHouse.Enemies.EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            enemy.gameObject.SetActive(false);
        }

        if (player == null || tracker == null || cameraDirector == null)
        {
            failures.Add("Player, PlayerRoomTracker 또는 CameraDirector가 없습니다.");
        }

        foreach (Doorway doorway in doorways)
        {
            if (failures.Count > 0 && player == null)
            {
                break;
            }

            doorway.SetLocked(false);
            yield return VerifyDirection(doorway, doorway.RoomA, player, tracker, cameraDirector, failures);
            yield return VerifyDirection(doorway, doorway.RoomB, player, tracker, cameraDirector, failures);
        }

        if (failures.Count == 0)
        {
            GameLog.Log($"CoreSandbox 문 인게임 검증 성공: {doorways.Length}개 문, {doorways.Length * 2}개 방향 이동·이격·CCTV 가시성이 유효합니다.");
        }
        else
        {
            GameLog.LogError(
                $"CoreSandbox 문 인게임 검증 실패: {failures.Count}건\n- " + string.Join("\n- ", failures));
        }

        yield return null;
        EditorApplication.ExitPlaymode();
    }

    /// <summary>지정한 시작 방에서 DoorTarget을 실행하고 실제 결과를 검사한다.</summary>
    private static IEnumerator VerifyDirection(
        Doorway doorway,
        Room sourceRoom,
        Player player,
        PlayerRoomTracker tracker,
        CameraDirector cameraDirector,
        List<string> failures)
    {
        if (!doorway.TryGetTraversal(sourceRoom, out DoorTraversal traversal))
        {
            failures.Add($"{doorway.name}/{sourceRoom?.RoomId}: DoorTraversal을 만들 수 없습니다.");
            yield break;
        }

        Target target = doorway.GetComponentsInChildren<Target>(true)
            .FirstOrDefault(candidate => candidate.OwnerRoom == sourceRoom);
        if (target == null || target.Action == null)
        {
            failures.Add($"{doorway.name}/{sourceRoom.RoomId}: 방향별 DoorTarget이 없습니다.");
            yield break;
        }

        if (!TryWarpToNavMesh(player, traversal.EntryPoint))
        {
            failures.Add($"{doorway.name}/{sourceRoom.RoomId}: 시작 접근점으로 Player를 배치하지 못했습니다.");
            yield break;
        }

        // 서로 다른 NavMesh 섬으로 Warp한 직후 Link 점유와 경로 상태가 갱신될 시간을 준다.
        yield return null;
        yield return null;

        tracker.RestoreRoom(sourceRoom);
        if (!cameraDirector.CutTo(sourceRoom))
        {
            failures.Add($"{doorway.name}/{sourceRoom.RoomId}: 시작 CCTV로 전환하지 못했습니다.");
            yield break;
        }

        TargetActionContext context = new TargetActionContext(target, player);
        yield return target.Action.Execute(context);
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        string label = $"{doorway.name}/{sourceRoom.RoomId}->{traversal.DestinationRoom.RoomId}";
        if (!context.IsCompleted || !player.LastMoveSucceeded)
        {
            failures.Add($"{label}: 이동이 완료되지 않았습니다. {player.LastMoveFailureReason}");
            yield break;
        }

        if (tracker.CurrentRoom != traversal.DestinationRoom)
        {
            failures.Add($"{label}: 논리 방이 목적지로 변경되지 않았습니다.");
        }

        ValidateDoorClearance(label, doorway, traversal, player, failures);
        ValidateCameraVisibility(label, player, failures);
    }

    /// <summary>NavMesh에 투영한 접근점으로 플레이어를 배치한다.</summary>
    private static bool TryWarpToNavMesh(Player player, Transform entryPoint)
    {
        if (!NavMesh.SamplePosition(entryPoint.position, out NavMeshHit hit, PositionSampleRadius, NavMesh.AllAreas))
        {
            return false;
        }

        GameObject warpPointObject = new GameObject("DoorVerificationWarpPoint");
        Transform warpPoint = warpPointObject.transform;
        warpPoint.SetPositionAndRotation(hit.position, entryPoint.rotation);
        bool succeeded = player.WarpTo(warpPoint);
        Destroy(warpPointObject);
        return succeeded;
    }

    /// <summary>최종 위치에서 플레이어 충돌체 전체가 문 프레임 두께 밖에 있는지 검사한다.</summary>
    private static void ValidateDoorClearance(
        string label,
        Doorway doorway,
        DoorTraversal traversal,
        Player player,
        List<string> failures)
    {
        Renderer frameRenderer = doorway.GetComponentsInChildren<Renderer>(true)
            .FirstOrDefault(renderer => renderer.name.Contains("Door_Frame"));
        Collider playerCollider = player.GetComponentsInChildren<Collider>(true)
            .FirstOrDefault(collider => collider.enabled && !collider.isTrigger);
        if (frameRenderer == null || playerCollider == null)
        {
            failures.Add($"{label}: 문 프레임 또는 플레이어 충돌체가 없습니다.");
            return;
        }

        Vector3 normal = Vector3.ProjectOnPlane(
            traversal.DestinationEntryPoint.position - traversal.EntryPoint.position,
            Vector3.up).normalized;
        float centerDistance = Mathf.Abs(Vector3.Dot(playerCollider.bounds.center - frameRenderer.bounds.center, normal));
        float frameExtent = ProjectedExtent(frameRenderer.bounds.extents, normal);
        float playerExtent = ProjectedExtent(playerCollider.bounds.extents, normal);
        float clearance = centerDistance - frameExtent - playerExtent;
        if (clearance + 0.05f < ClearanceMargin)
        {
            failures.Add(
                $"{label}: 통과 후 문과 플레이어 사이 여유가 {clearance:F2}m뿐입니다. " +
                $"Player={player.transform.position:F2}, Target={traversal.DestinationEntryPoint.position:F2}");
        }
    }

    /// <summary>목적지 CCTV의 실제 카메라 프러스텀 안에 플레이어 Renderer가 들어오는지 검사한다.</summary>
    private static void ValidateCameraVisibility(string label, Player player, List<string> failures)
    {
        Camera camera = Camera.main;
        Renderer playerRenderer = player.GetComponentsInChildren<Renderer>(true)
            .FirstOrDefault(renderer => renderer.enabled);
        if (camera == null || playerRenderer == null)
        {
            failures.Add($"{label}: Main Camera 또는 Player Renderer가 없습니다.");
            return;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        if (!GeometryUtility.TestPlanesAABB(planes, playerRenderer.bounds))
        {
            failures.Add($"{label}: 목적지 CCTV 화면에서 플레이어가 보이지 않습니다.");
        }
    }

    /// <summary>축 정렬 Bounds의 반크기를 지정 방향에 투영한다.</summary>
    private static float ProjectedExtent(Vector3 extents, Vector3 direction)
    {
        return Mathf.Abs(direction.x) * extents.x +
               Mathf.Abs(direction.y) * extents.y +
               Mathf.Abs(direction.z) * extents.z;
    }
}
