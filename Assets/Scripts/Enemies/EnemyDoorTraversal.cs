using System.Collections;
using HauntedHouse.World;
using UnityEngine;
using UnityEngine.AI;

namespace HauntedHouse.Enemies
{
    /// <summary>
    /// 적이 RoomGraph 경로의 한 Doorway를 통과하는 실행을 담당한다.
    /// 문 앞 대기, 문 열기, 통과 확정, 문 닫기를 하나의 코루틴으로 묶는다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(EnemyRoomTracker))]
    public sealed class EnemyDoorTraversal : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private EnemyRoomTracker roomTracker;

        [Header("문 통과")]
        [SerializeField, Min(0f)] private float waitBeforeOpening = 1f;
        [SerializeField, Min(0f)] private float arrivalTolerance = 0.05f;
        [SerializeField, Min(0f)] private float movementTimeout = 30f;

        private Doorway activeDoorway;
        private DoorTraversal activeTraversal;
        private bool passageStarted;
        private bool moveSucceeded;

        /// <summary>현재 Doorway 통과 코루틴이 실행 중인지 반환한다.</summary>
        public bool IsTraversing { get; private set; }

        /// <summary>마지막 Doorway 통과가 성공했는지 반환한다.</summary>
        public bool LastTraversalSucceeded { get; private set; }

        /// <summary>필수 참조를 보완한다.</summary>
        private void Awake()
        {
            agent ??= GetComponent<NavMeshAgent>();
            roomTracker ??= GetComponent<EnemyRoomTracker>();
        }

        /// <summary>
        /// 지정한 그래프 간선의 Doorway를 통과한다.
        /// DoorTarget의 ApproachPoint에서 대기한 뒤 문을 열고 통과한다.
        /// 통과 Sensor가 먼저 감지하면 그 이벤트를 사용하고, 감지하지 못하면 목적지 진입 지점 도착 시 명시적으로 확정한다.
        /// </summary>
        /// <param name="step">이번에 통과할 RoomGraph 간선.</param>
        public IEnumerator Execute(RoomGraphStep step)
        {
            LastTraversalSucceeded = false;
            IsTraversing = true;

            if (step.Doorway == null || !step.Doorway.TryGetTraversal(step.SourceRoom, out DoorTraversal traversal))
            {
                GameLog.LogError(
                    $"EnemyDoorTraversal '{name}'이 '{step.SourceRoom?.RoomId}'에서 " +
                    $"'{step.DestinationRoom?.RoomId}'로 통과할 Doorway를 찾지 못했습니다.",
                    this);
                IsTraversing = false;
                yield break;
            }

            activeDoorway = step.Doorway;
            activeTraversal = traversal;
            passageStarted = false;
            activeDoorway.PassageStarted += HandlePassageStarted;

            bool doorClosed = false;
            try
            {
                // PassageStart는 문 안쪽의 통과 경계이므로 대기 위치로 사용하지 않는다.
                // 문 앞 정지 지점은 방향별 DoorTarget이 소유한 ApproachPoint를 사용한다.
                yield return MoveTo(traversal.EntryPoint);
                if (!moveSucceeded)
                {
                    yield break;
                }

                // 적이 문 앞에 잠시 멈춰 플레이어가 접근을 확인할 시간을 준다.
                if (waitBeforeOpening > 0f)
                {
                    yield return new WaitForSeconds(waitBeforeOpening);
                }

                // 문 열기 애니메이션이 끝난 뒤에만 통과 이동을 시작한다.
                yield return activeDoorway.Open();
                yield return MoveTo(traversal.PassageStart);
                if (!moveSucceeded)
                {
                    yield break;
                }

                yield return MoveTo(traversal.DestinationEntryPoint);
                if (!moveSucceeded)
                {
                    yield break;
                }

                // Sensor가 벽 구조나 Collider 설정으로 감지하지 못한 경우에도 방 변경을 보장한다.
                if (!passageStarted)
                {
                    activeDoorway.NotifyPassageStarted(gameObject, traversal);
                }

                // 목적지 EntryPoint에 도착한 직후 문을 닫는다.
                yield return activeDoorway.Close();
                doorClosed = true;
                LastTraversalSucceeded = roomTracker != null && roomTracker.CurrentRoom == traversal.DestinationRoom;
                if (!LastTraversalSucceeded)
                {
                    GameLog.LogError(
                        $"EnemyDoorTraversal '{name}'의 방 변경이 확정되지 않았습니다: " +
                        $"'{traversal.SourceRoom?.RoomId}' → '{traversal.DestinationRoom?.RoomId}'",
                        this);
                }
            }
            finally
            {
                activeDoorway.PassageStarted -= HandlePassageStarted;
                if (!doorClosed && activeDoorway != null)
                {
                    activeDoorway.ForceClosed();
                }

                activeDoorway = null;
                activeTraversal = default;
                passageStarted = false;
                IsTraversing = false;
            }
        }

        /// <summary>
        /// Controller가 비활성화될 때 진행 중인 이동과 문 연출을 안전하게 중단한다.
        /// </summary>
        public void Abort()
        {
            if (activeDoorway != null)
            {
                activeDoorway.PassageStarted -= HandlePassageStarted;
                activeDoorway.ForceClosed();
            }

            StopAgent();
            activeDoorway = null;
            activeTraversal = default;
            passageStarted = false;
            IsTraversing = false;
            LastTraversalSucceeded = false;
        }

        /// <summary>Doorway가 자신이 통과하는 적을 감지했을 때 통과 확정 상태를 기록한다.</summary>
        private void HandlePassageStarted(GameObject actor, DoorTraversal traversal)
        {
            if (actor == gameObject && activeDoorway != null &&
                traversal.DestinationRoom == activeTraversal.DestinationRoom)
            {
                passageStarted = true;
            }
        }

        /// <summary>지정 지점으로 NavMesh 이동을 실행하고 도착 또는 실패까지 기다린다.</summary>
        private IEnumerator MoveTo(Transform destination)
        {
            moveSucceeded = false;
            if (agent == null || destination == null || !agent.enabled || !agent.isOnNavMesh ||
                !agent.SetDestination(destination.position))
            {
                GameLog.LogError($"EnemyDoorTraversal '{name}'이 이동 목적지를 설정하지 못했습니다.", this);
                yield break;
            }

            agent.isStopped = false;
            float elapsed = 0f;
            while (elapsed <= movementTimeout)
            {
                if (agent.pathPending)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                    continue;
                }

                if (agent.pathStatus != NavMeshPathStatus.PathComplete)
                {
                    GameLog.LogError($"EnemyDoorTraversal '{name}'의 이동 경로가 완성되지 않았습니다.", this);
                    StopAgent();
                    yield break;
                }

                if (HasArrived())
                {
                    StopAgent();
                    moveSucceeded = true;
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            GameLog.LogError($"EnemyDoorTraversal '{name}'의 이동 시간이 초과됐습니다.", this);
            StopAgent();
        }

        /// <summary>Agent의 이동을 멈추고 현재 경로를 제거한다.</summary>
        private void StopAgent()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            agent.isStopped = true;
            agent.ResetPath();
        }

        /// <summary>Agent가 목적지에 도착해 정지했는지 판정한다.</summary>
        private bool HasArrived()
        {
            if (agent.pathPending || float.IsInfinity(agent.remainingDistance))
            {
                return false;
            }

            float allowedDistance = agent.stoppingDistance + arrivalTolerance;
            return agent.remainingDistance <= allowedDistance &&
                   (!agent.hasPath || agent.velocity.sqrMagnitude <= 0.01f);
        }

        /// <summary>컴포넌트를 처음 추가할 때 같은 오브젝트의 참조를 연결한다.</summary>
        private void Reset()
        {
            agent = GetComponent<NavMeshAgent>();
            roomTracker = GetComponent<EnemyRoomTracker>();
        }
    }
}
