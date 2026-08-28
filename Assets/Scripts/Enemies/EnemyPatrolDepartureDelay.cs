using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace HauntedHouse.Enemies
{
    /// <summary>
    /// 순찰 목적지가 경고 UI에 공개된 뒤 실제 출발까지의 유예 시간을 적용한다.
    /// 목적지 도착 후 대기는 PatrolPoint가 계속 담당하며, 이 컴포넌트는 출발 직전 대기만 분리해 소유한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyPatrolDepartureDelay : MonoBehaviour
    {
        private EnemyController enemy;
        private NavMeshAgent agent;
        private Coroutine delayRoutine;
        private float delaySeconds;
        private bool readyForNextDeparture = true;

        /// <summary>현재 목적지 경고 뒤 출발 유예를 적용 중인지 반환한다.</summary>
        public bool IsDelaying => delayRoutine != null;

        /// <summary>현재 적용된 출발 유예 시간을 초 단위로 반환한다.</summary>
        public float DelaySeconds => delaySeconds;

        /// <summary>대상 적의 다음 순찰 출발 유예 시간을 갱신한다.</summary>
        /// <param name="controller">유예 시간을 적용할 적이다.</param>
        /// <param name="seconds">목적지 경고 후 정지할 시간이다.</param>
        public void Configure(EnemyController controller, float seconds)
        {
            Unsubscribe();
            enemy = controller;
            agent = enemy != null ? enemy.GetComponent<NavMeshAgent>() : null;
            delaySeconds = Mathf.Max(0f, seconds);
            readyForNextDeparture = true;

            if (enemy != null)
            {
                enemy.DestinationChanged += HandleDestinationChanged;
            }
        }

        /// <summary>컴포넌트가 비활성화될 때 이벤트와 진행 중 대기를 정리한다.</summary>
        private void OnDisable()
        {
            CancelDelay(false);
            Unsubscribe();
        }

        /// <summary>
        /// 목적지 이벤트 뒤 같은 프레임에 문 통과 코루틴이 Agent를 다시 움직여도 유예가 끝날 때까지 정지를 유지한다.
        /// </summary>
        private void LateUpdate()
        {
            if (delayRoutine != null && agent != null && agent.enabled && agent.isOnNavMesh && !agent.isStopped)
            {
                agent.isStopped = true;
            }
        }

        /// <summary>도착으로 목적지가 해제된 뒤 처음 지정되는 다음 목적지에만 출발 유예를 적용한다.</summary>
        /// <param name="destinationRoom">경고 UI에 전달된 다음 목적지 방이다.</param>
        private void HandleDestinationChanged(Room destinationRoom)
        {
            if (destinationRoom == null)
            {
                readyForNextDeparture = true;
                return;
            }

            // 여러 방을 지나는 동안 중간 목적지가 갱신되어도 한 번 출발한 이동에는 대기를 반복하지 않는다.
            if (!readyForNextDeparture || delaySeconds <= 0f || enemy == null || enemy.Mode != EnemyMode.Patrol ||
                agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            readyForNextDeparture = false;
            CancelDelay(false);
            agent.isStopped = true;
            delayRoutine = StartCoroutine(ReleaseAfterDelay());
        }

        /// <summary>설정된 시간이 지난 뒤 현재 순찰 경로의 이동을 재개한다.</summary>
        private IEnumerator ReleaseAfterDelay()
        {
            float remaining = delaySeconds;
            while (remaining > 0f)
            {
                if (enemy == null || enemy.Mode != EnemyMode.Patrol)
                {
                    delayRoutine = null;
                    yield break;
                }

                remaining -= Time.deltaTime;
                yield return null;
            }

            delayRoutine = null;
            if (agent != null && agent.enabled && agent.isOnNavMesh && enemy != null && enemy.Mode == EnemyMode.Patrol)
            {
                agent.isStopped = false;
            }
        }

        /// <summary>진행 중인 유예 코루틴을 끝내고 필요할 때 이동 정지를 해제한다.</summary>
        /// <param name="resumeMovement">현재 NavMesh 이동을 즉시 재개할지 여부다.</param>
        private void CancelDelay(bool resumeMovement)
        {
            if (delayRoutine != null)
            {
                StopCoroutine(delayRoutine);
                delayRoutine = null;
            }

            if (resumeMovement && agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }
        }

        /// <summary>현재 적의 목적지 변경 이벤트 구독을 해제한다.</summary>
        private void Unsubscribe()
        {
            if (enemy != null)
            {
                enemy.DestinationChanged -= HandleDestinationChanged;
            }
        }
    }
}
