using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace HauntedHouse.Commands
{
    /// <summary>
    /// Player 내부에서 NavMeshAgent 이동과 도착 판정만 수행하는 일반 C# 클래스다.
    /// MonoBehaviour 생명주기나 명령·상호작용 규칙을 소유하지 않으며 Player 외부에는 노출하지 않는다.
    /// </summary>
    internal sealed class PlayerMover
    {
        private readonly NavMeshAgent agent;
        private readonly float arrivalTolerance;
        private readonly float pathResolveTimeout;
        private readonly float moveTimeout;
        private readonly Func<bool> isPaused;

        /// <summary>
        /// 가장 최근 이동이 정상적으로 목적지에 도착했는지 나타낸다.
        /// </summary>
        internal bool LastMoveSucceeded { get; private set; }

        /// <summary>
        /// 가장 최근 이동 실패 원인을 사람이 확인할 수 있는 문자열로 제공한다.
        /// </summary>
        internal string LastFailureReason { get; private set; } = string.Empty;

        /// <summary>
        /// Player가 소유한 NavMeshAgent와 이동 판정 설정으로 이동 구현체를 생성한다.
        /// </summary>
        internal PlayerMover(
            NavMeshAgent agent,
            float arrivalTolerance,
            float pathResolveTimeout,
            float moveTimeout,
            Func<bool> isPaused)
        {
            this.agent = agent;
            this.arrivalTolerance = Mathf.Max(0f, arrivalTolerance);
            this.pathResolveTimeout = Mathf.Max(0.1f, pathResolveTimeout);
            this.moveTimeout = Mathf.Max(0.1f, moveTimeout);
            this.isPaused = isPaused;
        }

        /// <summary>
        /// 지정된 접근 지점으로 이동하고 NavMesh 경로 계산과 실제 도착이 끝날 때까지 기다린다.
        /// 실패는 정상 게임 결과가 아니므로 원인을 기록하고 코루틴을 종료한다.
        /// </summary>
        internal IEnumerator MoveTo(Transform destination)
        {
            // 이전 명령의 결과가 새 명령에 섞이지 않도록 실행 시작 시 결과를 초기화한다.
            LastMoveSucceeded = false;
            LastFailureReason = string.Empty;

            if (destination == null)
            {
                Fail("이동할 ApproachPoint가 없습니다.");
                yield break;
            }

            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                Fail("Player의 NavMeshAgent가 활성 NavMesh 위에 있지 않습니다.");
                yield break;
            }

            // 이전 경로를 제거한 다음 현재 Target의 접근 지점만 새 목적지로 사용한다.
            agent.isStopped = false;
            agent.ResetPath();

            if (!agent.SetDestination(destination.position))
            {
                Fail($"NavMeshAgent가 목적지 '{destination.name}'를 받아들이지 못했습니다.");
                yield break;
            }

            // SetDestination 직후에는 경로가 비동기로 계산되므로 pathPending이 끝날 때까지 기다린다.
            float pathWaitElapsed = 0f;
            while (agent.pathPending)
            {
                if (IsPaused())
                {
                    yield return null;
                    continue;
                }

                pathWaitElapsed += Time.unscaledDeltaTime;
                if (pathWaitElapsed >= pathResolveTimeout)
                {
                    Fail($"목적지 '{destination.name}'의 NavMesh 경로 계산 시간이 초과됐습니다.");
                    yield break;
                }

                yield return null;
            }

            if (agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                Fail($"목적지 '{destination.name}'까지 완전한 NavMesh 경로가 없습니다.");
                yield break;
            }

            // 남은 거리와 실제 속도를 함께 확인해 감속 중인 프레임을 도착으로 오인하지 않는다.
            float moveElapsed = 0f;
            while (!HasArrived(destination.position))
            {
                if (!agent.enabled || !agent.isOnNavMesh)
                {
                    Fail("이동 중 Player의 NavMeshAgent가 NavMesh에서 벗어났습니다.");
                    yield break;
                }

                if (!agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete)
                {
                    Fail($"이동 중 목적지 '{destination.name}'의 NavMesh 경로가 유효하지 않게 됐습니다.");
                    yield break;
                }

                if (IsPaused())
                {
                    agent.isStopped = true;
                    yield return null;
                    continue;
                }

                if (agent.isStopped)
                {
                    agent.isStopped = false;
                }

                moveElapsed += Time.unscaledDeltaTime;
                if (moveElapsed >= moveTimeout)
                {
                    Fail($"목적지 '{destination.name}'까지 이동 시간이 초과됐습니다.");
                    yield break;
                }

                yield return null;
            }

            // 도착 후 남은 경로를 정리하고 ApproachPoint가 지정한 수평 방향을 바라보게 한다.
            agent.isStopped = true;
            agent.ResetPath();
            Vector3 facing = destination.eulerAngles;
            agent.transform.rotation = Quaternion.Euler(0f, facing.y, 0f);

            LastMoveSucceeded = true;
        }

        /// <summary>
        /// 진행 중인 이동을 중단하고 Agent의 현재 경로를 제거한다.
        /// RespawnCoordinator가 이전 명령의 남은 이동을 복원 이후까지 끌고 가지 않도록 사용한다.
        /// </summary>
        internal void Abort()
        {
            LastMoveSucceeded = false;
            LastFailureReason = "Player 이동이 체크포인트 복원으로 중단됐습니다.";

            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }

        /// <summary>Agent를 지정된 Transform 위치와 방향으로 즉시 배치한다.</summary>
        internal bool WarpTo(Transform destination)
        {
            LastMoveSucceeded = false;
            LastFailureReason = string.Empty;

            if (destination == null || agent == null || !agent.enabled)
            {
                LastFailureReason = "Player를 배치할 Transform 또는 NavMeshAgent가 없습니다.";
                return false;
            }

            if (!agent.isOnNavMesh || !agent.Warp(destination.position))
            {
                LastFailureReason = $"Player를 체크포인트 위치 '{destination.name}'에 배치하지 못했습니다.";
                return false;
            }

            agent.isStopped = true;
            agent.ResetPath();
            agent.transform.rotation = destination.rotation;
            LastMoveSucceeded = true;
            return true;
        }

        /// <summary>
        /// 공용 일시정지 중에는 NavMesh 이동과 unscaled 타임아웃 누적을 모두 보류한다.
        /// </summary>
        private bool IsPaused()
        {
            return isPaused != null && isPaused();
        }

        /// <summary>
        /// NavMeshAgent가 허용 거리 안에 있으며 실질적으로 정지했는지 검사한다.
        /// </summary>
        private bool HasArrived(Vector3 destinationPosition)
        {
            float allowedDistance = agent.stoppingDistance + arrivalTolerance;
            // OffMeshLink를 지나는 동안 remainingDistance가 일시적으로 0이 되어도
            // 실제 목표 XZ에 도착하기 전에는 경로를 지우거나 이동을 완료하지 않는다.
            float horizontalDistance = Vector3.ProjectOnPlane(
                agent.transform.position - destinationPosition,
                Vector3.up).magnitude;
            bool isNearDestination = horizontalDistance <= allowedDistance + 0.15f;
            bool hasStoppedMoving = !agent.hasPath || agent.velocity.sqrMagnitude <= 0.01f;

            // 문 통과 직후 NavMeshLink나 문 상태가 갱신되면 Agent가 실제 목적지에 정지한 뒤에도
            // remainingDistance가 잠시 Infinity로 남을 수 있다. 실제 XZ 위치와 정지 상태가 이미
            // 도착 조건을 만족하면 경로 내부 캐시보다 물리 위치를 우선해 불필요한 타임아웃을 막는다.
            if (!agent.pathPending && isNearDestination && hasStoppedMoving)
            {
                return true;
            }

            if (agent.pathPending || float.IsInfinity(agent.remainingDistance))
            {
                return false;
            }

            bool isWithinDistance = agent.remainingDistance <= allowedDistance;
            return isWithinDistance && isNearDestination && hasStoppedMoving;
        }

        /// <summary>
        /// 이동을 안전하게 중단하고 Runner가 기록할 실패 원인을 저장한다.
        /// </summary>
        private void Fail(string reason)
        {
            LastMoveSucceeded = false;
            LastFailureReason = reason;

            // Agent가 NavMesh에서 이미 제거된 경우 Unity 예외가 발생할 수 있으므로 유효할 때만 중단한다.
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }
    }
}
