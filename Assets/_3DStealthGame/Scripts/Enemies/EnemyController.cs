using System;
using HauntedHouse.World;
using UnityEngine;
using UnityEngine.AI;

namespace HauntedHouse.Enemies
{
    /// <summary>
    /// 하나의 적을 고정 순찰 경로에 따라 이동시키고 상태와 현재 목적지를 공개한다.
    /// 추격 상태는 예약만 하고 실제 추격 규칙은 은신·포획 단계에서 추가한다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(EnemyRoomTracker))]
    public sealed class EnemyController : MonoBehaviour
    {
        [Header("실행 구성")]
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private EnemyRoomTracker roomTracker;
        [SerializeField] private PatrolRoute initialRoute;
        [SerializeField] private bool initiallyActive = true;

        [Header("도착 판정")]
        [SerializeField, Min(0f)] private float arrivalTolerance = 0.05f;

        private Renderer[] renderers;
        private Collider[] colliders;
        private int targetIndex;
        private float waitRemaining;
        private PatrolPoint currentDestination;

        /// <summary>현재 적 상태를 반환한다.</summary>
        public EnemyState State { get; private set; } = EnemyState.Inactive;

        /// <summary>현재 적용된 순찰 경로를 반환한다.</summary>
        public PatrolRoute CurrentRoute { get; private set; }

        /// <summary>적이 현재 속한 방을 반환한다.</summary>
        public Room CurrentRoom => roomTracker != null ? roomTracker.CurrentRoom : null;

        /// <summary>현재 이동 중인 순찰 목적지의 방을 반환한다.</summary>
        public Room NextDestinationRoom => currentDestination != null ? currentDestination.Room : null;

        /// <summary>상태가 변경될 때 이전 상태와 새 상태를 전달한다.</summary>
        public event Action<EnemyState, EnemyState> StateChanged;

        /// <summary>필수 참조와 초기 순찰 구성을 확인하고 첫 상태를 시작한다.</summary>
        private void Awake()
        {
            agent ??= GetComponent<NavMeshAgent>();
            roomTracker ??= GetComponent<EnemyRoomTracker>();
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
        }

        /// <summary>초기 경로를 적용하고 Inspector의 활성화 설정에 따라 순찰을 시작한다.</summary>
        private void Start()
        {
            string error = string.Empty;
            if (initialRoute == null || !initialRoute.TryValidateConfiguration(out error))
            {
                GameLog.LogError(error ?? $"EnemyController '{name}'에 InitialRoute가 없습니다.", this);
                return;
            }

            SetRoute(initialRoute, initialRoute.StartIndex);
            SetActive(initiallyActive);
        }

        /// <summary>현재 상태에 따라 이동 또는 대기 시간을 갱신한다.</summary>
        private void Update()
        {
            if (State == EnemyState.Patrolling)
            {
                UpdatePatrolling();
            }
            else if (State == EnemyState.Waiting)
            {
                UpdateWaiting();
            }
        }

        /// <summary>
        /// 새 경로를 적용하고 지정한 인덱스의 지점부터 순찰을 시작한다.
        /// 적이 비활성 상태이면 경로만 저장하고 재활성화 시 사용한다.
        /// </summary>
        public void SetRoute(PatrolRoute newRoute, int startIndex)
        {
            string error = string.Empty;
            if (newRoute == null || !newRoute.TryValidateConfiguration(out error))
            {
                GameLog.LogError(error ?? $"EnemyController '{name}'에 새 PatrolRoute가 없습니다.", this);
                return;
            }

            if (startIndex < 0 || startIndex >= newRoute.Count)
            {
                GameLog.LogError($"EnemyController '{name}'의 경로 시작 인덱스가 범위를 벗어났습니다: {startIndex}", this);
                return;
            }

            CurrentRoute = newRoute;
            targetIndex = startIndex;
            currentDestination = null;
            waitRemaining = 0f;

            if (State == EnemyState.Patrolling || State == EnemyState.Waiting)
            {
                BeginPatrolling();
            }
        }

        /// <summary>경로의 Inspector 시작 인덱스를 사용해 새 경로를 적용한다.</summary>
        public void SetRoute(PatrolRoute newRoute)
        {
            SetRoute(newRoute, newRoute != null ? newRoute.StartIndex : 0);
        }

        /// <summary>
        /// 적을 논리적으로 활성화하거나 비활성화한다.
        /// GameObject 자체는 유지해 외부 시스템이 같은 참조로 재활성화할 수 있게 한다.
        /// </summary>
        public void SetActive(bool value)
        {
            if (!value)
            {
                StopMovement();
                currentDestination = null;
                roomTracker?.ClearRoom();
                SetVisible(false);
                SetState(EnemyState.Inactive);
                return;
            }

            if (CurrentRoute == null)
            {
                GameLog.LogError($"EnemyController '{name}'에 활성화할 PatrolRoute가 없습니다.", this);
                return;
            }

            if (!EnableAgentAt(transform.position))
            {
                return;
            }

            SetVisible(true);
            BeginPatrolling();
        }

        /// <summary>
        /// 적을 지정한 순찰 지점에 배치하고 해당 지점의 방에서 순찰을 재개한다.
        /// </summary>
        public void SetActive(bool value, PatrolPoint spawnPoint)
        {
            if (!value)
            {
                SetActive(false);
                return;
            }

            string error = string.Empty;
            if (spawnPoint == null || !spawnPoint.TryValidateConfiguration(out error))
            {
                GameLog.LogError(error ?? $"EnemyController '{name}'의 SpawnPoint가 없습니다.", this);
                return;
            }

            if (CurrentRoute == null)
            {
                GameLog.LogError($"EnemyController '{name}'에 활성화할 PatrolRoute가 없습니다.", this);
                return;
            }

            if (!EnableAgentAt(spawnPoint.Position))
            {
                return;
            }

            roomTracker?.ResetAt(spawnPoint);
            SetVisible(true);
            BeginPatrolling();
        }

        /// <summary>현재 목적지로 이동 중인지 확인하고 도착하면 대기 상태로 전환한다.</summary>
        private void UpdatePatrolling()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                GameLog.LogError($"EnemyController '{name}'의 NavMeshAgent가 유효하지 않습니다.", this);
                SetActive(false);
                return;
            }

            if (agent.pathPending)
            {
                return;
            }

            if (agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                GameLog.LogError($"EnemyController '{name}'의 목적지 경로가 완성되지 않았습니다.", this);
                SetActive(false);
                return;
            }

            if (!HasArrived())
            {
                return;
            }

            StopMovement();
            waitRemaining = currentDestination.WaitDuration;
            currentDestination = null;
            SetState(EnemyState.Waiting);
        }

        /// <summary>현재 지점의 대기 시간이 끝나면 다음 순찰 지점으로 이동한다.</summary>
        private void UpdateWaiting()
        {
            waitRemaining -= Time.deltaTime;
            if (waitRemaining > 0f)
            {
                return;
            }

            targetIndex = (targetIndex + 1) % CurrentRoute.Count;
            BeginPatrolling();
        }

        /// <summary>현재 인덱스의 순찰 지점을 다음 목적지로 지정하고 이동을 시작한다.</summary>
        private void BeginPatrolling()
        {
            if (CurrentRoute == null || CurrentRoute.Count == 0)
            {
                GameLog.LogError($"EnemyController '{name}'에 유효한 순찰 경로가 없습니다.", this);
                SetActive(false);
                return;
            }

            currentDestination = CurrentRoute.GetPoint(targetIndex);
            if (currentDestination == null)
            {
                GameLog.LogError($"EnemyController '{name}'의 순찰 목적지가 비어 있습니다.", this);
                SetActive(false);
                return;
            }

            if (agent == null || !agent.enabled || !agent.isOnNavMesh || !agent.SetDestination(currentDestination.Position))
            {
                GameLog.LogError($"EnemyController '{name}'가 순찰 목적지를 설정하지 못했습니다.", this);
                SetActive(false);
                return;
            }

            agent.isStopped = false;
            SetState(EnemyState.Patrolling);
        }

        /// <summary>Agent를 중지하고 현재 경로를 제거한다.</summary>
        private void StopMovement()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            agent.isStopped = true;
            agent.ResetPath();
        }

        /// <summary>Agent를 지정 위치에 배치하고 활성화한다.</summary>
        private bool EnableAgentAt(Vector3 position)
        {
            if (agent == null)
            {
                GameLog.LogError($"EnemyController '{name}'에 NavMeshAgent가 없습니다.", this);
                return false;
            }

            agent.enabled = true;
            if (!agent.isOnNavMesh || !agent.Warp(position))
            {
                GameLog.LogError($"EnemyController '{name}'를 NavMesh 위치에 배치하지 못했습니다.", this);
                agent.enabled = false;
                return false;
            }

            return true;
        }

        /// <summary>적의 표시 Renderer와 충돌 Collider를 한꺼번에 전환한다.</summary>
        private void SetVisible(bool value)
        {
            if (renderers != null)
            {
                foreach (Renderer renderer in renderers)
                {
                    if (renderer != null) renderer.enabled = value;
                }
            }

            if (colliders != null)
            {
                foreach (Collider collider in colliders)
                {
                    if (collider != null) collider.enabled = value;
                }
            }
        }

        /// <summary>실제 상태가 달라질 때만 상태를 저장하고 이벤트를 발생시킨다.</summary>
        private void SetState(EnemyState value)
        {
            if (State == value)
            {
                return;
            }

            EnemyState previous = State;
            State = value;
            StateChanged?.Invoke(previous, State);
        }

        /// <summary>Agent가 허용 거리 안에 있고 실질적으로 정지했는지 확인한다.</summary>
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

        /// <summary>컴포넌트를 처음 추가할 때 같은 오브젝트의 필수 참조를 연결한다.</summary>
        private void Reset()
        {
            agent = GetComponent<NavMeshAgent>();
            roomTracker = GetComponent<EnemyRoomTracker>();
        }
    }
}
