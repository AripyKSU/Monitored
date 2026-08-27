using System;
using System.Collections;
using HauntedHouse.World;
using UnityEngine;

/// <summary>
/// ServiceHallway의 실제 CCTV를 두 개의 직렬화된 Pose 사이에서 왕복 이동시킨다.
/// 방 전환은 PlayerRoomTracker에 위임하고, 이 컴포넌트는 현재 방에 머무는 동안의 시점 연출만 담당한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CctvPosePatrol : MonoBehaviour
{
    /// <summary>
    /// CCTV의 월드 위치와 회전으로 구성된 하나의 시점 Pose다.
    /// </summary>
    [Serializable]
    public struct PoseData
    {
        [SerializeField] private Vector3 position;
        [SerializeField] private Vector3 eulerAngles;

        /// <summary>Pose의 월드 위치를 반환한다.</summary>
        public Vector3 Position => position;

        /// <summary>저장된 오일러 각을 Quaternion으로 변환해 반환한다.</summary>
        public Quaternion Rotation => Quaternion.Euler(eulerAngles);

        /// <summary>Transform의 월드 위치와 회전으로 Pose를 생성한다.</summary>
        public PoseData(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            eulerAngles = rotation.eulerAngles;
        }
    }

    [Header("방 참조")]
    [SerializeField] private PlayerRoomTracker playerRoomTracker;
    [SerializeField] private Room serviceHallwayRoom;
    [SerializeField] private Room kitchenRoom;
    [SerializeField] private Room centerHallwayBRoom;

    [Header("CCTV Pose")]
    [SerializeField] private PoseData poseA;
    [SerializeField] private PoseData poseB;
    [Min(0f)] [SerializeField] private float moveDuration = 2f;
    [Min(0f)] [SerializeField] private float endWaitDuration = 1f;

    private Coroutine patrolCoroutine;
    private bool isInsideServiceHallway;

    /// <summary>Pose A를 외부 검증기와 Editor 도구가 읽을 수 있게 반환한다.</summary>
    public PoseData PoseA => poseA;

    /// <summary>Pose B를 외부 검증기와 Editor 도구가 읽을 수 있게 반환한다.</summary>
    public PoseData PoseB => poseB;

    /// <summary>두 Pose 사이를 이동하는 데 걸리는 시간을 반환한다.</summary>
    public float MoveDuration => moveDuration;

    /// <summary>양 끝점에서 머무는 시간을 반환한다.</summary>
    public float EndWaitDuration => endWaitDuration;

    /// <summary>순찰 대상 ServiceHallway Room 참조를 반환한다.</summary>
    public Room ServiceHallwayRoom => serviceHallwayRoom;

    /// <summary>Kitchen 진입 방향 판정에 사용하는 Room 참조를 반환한다.</summary>
    public Room KitchenRoom => kitchenRoom;

    /// <summary>CenterHallway_B 진입 방향 판정에 사용하는 Room 참조를 반환한다.</summary>
    public Room CenterHallwayBRoom => centerHallwayBRoom;

    /// <summary>순찰 대상 플레이어 방 추적기 참조를 반환한다.</summary>
    public PlayerRoomTracker PlayerRoomTracker => playerRoomTracker;

    /// <summary>현재 ServiceHallway CCTV 왕복 Coroutine이 실행 중인지 반환한다.</summary>
    public bool IsPatrolling => patrolCoroutine != null;

    /// <summary>
    /// 방 추적기의 변경 이벤트를 구독해 실제 방 상태와 CCTV 순찰 상태를 연결한다.
    /// </summary>
    private void OnEnable()
    {
        if (playerRoomTracker != null)
        {
            playerRoomTracker.RoomEntered -= HandleRoomEntered;
            playerRoomTracker.RoomEntered += HandleRoomEntered;
        }
    }

    /// <summary>
    /// 체크포인트 복원처럼 RoomEntered 이벤트 없이 이미 방이 정해진 경우도 초기화한다.
    /// </summary>
    private void Start()
    {
        if (!TryValidateConfiguration())
        {
            return;
        }

        // RestoreRoom은 이벤트를 발생시키지 않으므로 Start에서 현재 방을 한 번 직접 동기화한다.
        if (playerRoomTracker.CurrentRoom == serviceHallwayRoom)
        {
            BeginPatrol(poseA, poseB);
        }
    }

    /// <summary>
    /// 이벤트 구독을 해제하고 씬이 비활성화될 때 실행 중인 순찰을 종료한다.
    /// </summary>
    private void OnDisable()
    {
        if (playerRoomTracker != null)
        {
            playerRoomTracker.RoomEntered -= HandleRoomEntered;
        }

        StopPatrol();
    }

    /// <summary>
    /// 플레이어가 어느 방에서 ServiceHallway로 들어왔는지에 따라 시작 Pose를 선택한다.
    /// </summary>
    /// <param name="previousRoom">진입 전 논리 방.</param>
    /// <param name="enteredRoom">진입 후 논리 방.</param>
    private void HandleRoomEntered(Room previousRoom, Room enteredRoom)
    {
        if (enteredRoom != serviceHallwayRoom)
        {
            // ServiceHallway를 벗어난 즉시 더 이상 CCTV Transform을 갱신하지 않는다.
            isInsideServiceHallway = false;
            StopPatrol();
            return;
        }

        if (!TryValidateConfiguration())
        {
            return;
        }

        bool enteredFromKitchen = previousRoom == kitchenRoom;
        bool enteredFromCenterHallway = previousRoom == centerHallwayBRoom;
        if (!enteredFromKitchen && !enteredFromCenterHallway && previousRoom != null)
        {
            GameLog.LogWarning(
                $"CctvPosePatrol '{name}'가 예상하지 않은 Room '{previousRoom.RoomId}'에서 진입했습니다. Pose A를 사용합니다.",
                this);
        }

        PoseData startPose = enteredFromCenterHallway ? poseB : poseA;
        PoseData nextPose = enteredFromCenterHallway ? poseA : poseB;
        BeginPatrol(startPose, nextPose);
    }

    /// <summary>
    /// 지정한 시작점에서 CCTV 왕복 순회를 새로 시작한다.
    /// </summary>
    private void BeginPatrol(PoseData startPose, PoseData nextPose)
    {
        isInsideServiceHallway = true;
        StopPatrol();
        ApplyPose(startPose);
        patrolCoroutine = StartCoroutine(PatrolLoop(startPose, nextPose));
    }

    /// <summary>
    /// 두 Pose를 번갈아 이동하며 플레이어가 방에 머무는 동안 왕복한다.
    /// </summary>
    private IEnumerator PatrolLoop(PoseData currentPose, PoseData nextPose)
    {
        while (isInsideServiceHallway && IsPlayerInServiceHallway())
        {
            yield return MoveToPose(currentPose, nextPose);

            if (!isInsideServiceHallway || !IsPlayerInServiceHallway())
            {
                break;
            }

            // 끝점 대기 동안에도 일반 scaled time을 사용해 게임 일시정지와 함께 멈춘다.
            if (endWaitDuration > 0f)
            {
                yield return new WaitForSeconds(endWaitDuration);
            }

            PoseData previousPose = currentPose;
            currentPose = nextPose;
            nextPose = previousPose;
        }

        patrolCoroutine = null;
    }

    /// <summary>
    /// 현재 Pose에서 목표 Pose까지 위치와 회전을 함께 보간한다.
    /// </summary>
    private IEnumerator MoveToPose(PoseData fromPose, PoseData toPose)
    {
        if (moveDuration <= 0f)
        {
            ApplyPose(toPose);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < moveDuration && isInsideServiceHallway && IsPlayerInServiceHallway())
        {
            float normalizedTime = Mathf.Clamp01(elapsed / moveDuration);
            transform.SetPositionAndRotation(
                Vector3.Lerp(fromPose.Position, toPose.Position, normalizedTime),
                Quaternion.Slerp(fromPose.Rotation, toPose.Rotation, normalizedTime));

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (isInsideServiceHallway && IsPlayerInServiceHallway())
        {
            ApplyPose(toPose);
        }
    }

    /// <summary>직렬화된 Pose를 CCTV Transform에 즉시 적용한다.</summary>
    private void ApplyPose(PoseData pose)
    {
        transform.SetPositionAndRotation(pose.Position, pose.Rotation);
    }

    /// <summary>PlayerRoomTracker의 현재 방이 ServiceHallway인지 확인한다.</summary>
    private bool IsPlayerInServiceHallway()
    {
        return playerRoomTracker != null && playerRoomTracker.CurrentRoom == serviceHallwayRoom;
    }

    /// <summary>기존 순찰 Coroutine을 중지하고 순찰 상태를 초기화한다.</summary>
    private void StopPatrol()
    {
        if (patrolCoroutine != null)
        {
            StopCoroutine(patrolCoroutine);
            patrolCoroutine = null;
        }
    }

    /// <summary>Editor 설정이 제공하는 참조가 모두 유효한지 확인한다.</summary>
    private bool TryValidateConfiguration()
    {
        if (playerRoomTracker == null || serviceHallwayRoom == null ||
            kitchenRoom == null || centerHallwayBRoom == null)
        {
            GameLog.LogError($"CctvPosePatrol '{name}'의 방 또는 PlayerRoomTracker 참조가 비어 있습니다.", this);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Editor 설정 도구가 런타임 참조를 명시적으로 직렬화한다.
    /// </summary>
    public void ConfigureReferences(
        PlayerRoomTracker tracker,
        Room serviceHallway,
        Room kitchen,
        Room centerHallwayB)
    {
        playerRoomTracker = tracker;
        serviceHallwayRoom = serviceHallway;
        kitchenRoom = kitchen;
        centerHallwayBRoom = centerHallwayB;
    }

    /// <summary>
    /// Editor 설정 도구가 두 CCTV 오브젝트의 월드 Transform을 Pose 데이터로 복사한다.
    /// </summary>
    public void SetPoseData(PoseData newPoseA, PoseData newPoseB)
    {
        poseA = newPoseA;
        poseB = newPoseB;
    }

    /// <summary>Editor 설정 도구가 순찰 시간 값을 직렬화한다.</summary>
    public void SetDurations(float newMoveDuration, float newEndWaitDuration)
    {
        moveDuration = Mathf.Max(0f, newMoveDuration);
        endWaitDuration = Mathf.Max(0f, newEndWaitDuration);
    }

    /// <summary>
    /// 컴포넌트를 처음 추가했을 때 같은 GameObject의 PlayerRoomTracker를 보수적으로 연결한다.
    /// 실제 Room 참조는 Editor 설정 도구가 명시적으로 지정한다.
    /// </summary>
    private void Reset()
    {
        playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
    }
}
