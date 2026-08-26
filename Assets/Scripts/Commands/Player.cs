using System.Collections;
using HauntedHouse.Core;
using UnityEngine;
using UnityEngine.AI;

namespace HauntedHouse.Commands
{
    /// <summary>
    /// 플레이어 캐릭터가 외부 시스템에 제공하는 기능의 진입점이다.
    /// NavMeshAgent 세부 제어는 internal PlayerMover에 위임하고 Runner에는 이동 코루틴과 결과만 제공한다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class Player : MonoBehaviour
    {
        [Header("이동 구성")]
        [SerializeField] private NavMeshAgent navMeshAgent;
        [SerializeField, Min(0f)] private float arrivalTolerance = 0.05f;
        [SerializeField, Min(0.1f)] private float pathResolveTimeout = 2f;
        [SerializeField, Min(0.1f)] private float moveTimeout = 30f;

        [Header("게임 흐름")]
        [SerializeField] private GamePauseService pauseService;

        private PlayerMover mover;

        /// <summary>
        /// 가장 최근 이동 명령이 정상적으로 도착했는지 반환한다.
        /// </summary>
        public bool LastMoveSucceeded => mover != null && mover.LastMoveSucceeded;

        /// <summary>
        /// 가장 최근 이동 명령의 실패 원인을 반환한다.
        /// </summary>
        public string LastMoveFailureReason => mover?.LastFailureReason ?? "PlayerMover가 초기화되지 않았습니다.";

        /// <summary>
        /// NavMeshAgent를 확인하고 Player 전용 이동 구현체를 생성한다.
        /// </summary>
        private void Awake()
        {
            navMeshAgent ??= GetComponent<NavMeshAgent>();
            pauseService ??= FindFirstObjectByType<GamePauseService>();
            if (navMeshAgent == null)
            {
                GameLog.LogError($"Player '{name}'에 NavMeshAgent가 없습니다.", this);
                enabled = false;
                return;
            }

            // PlayerMover는 Unity 컴포넌트가 아니며 Player가 생명주기를 독점한다.
            mover = new PlayerMover(
                navMeshAgent,
                arrivalTolerance,
                pathResolveTimeout,
                moveTimeout,
                () => pauseService != null && pauseService.IsPaused);
        }

        /// <summary>
        /// Target의 접근 지점까지 이동하고 도착 또는 실패가 확정될 때까지 기다린다.
        /// 외부 시스템은 NavMeshAgent 대신 이 메서드만 사용한다.
        /// </summary>
        /// <param name="approachPoint">도착 위치와 최종 방향을 제공하는 Transform.</param>
        public IEnumerator MoveTo(Transform approachPoint)
        {
            if (mover == null)
            {
                GameLog.LogError($"Player '{name}'의 PlayerMover가 초기화되지 않았습니다.", this);
                yield break;
            }

            // 실제 이동과 도착 판정은 Player 내부 구현체에 위임한다.
            yield return mover.MoveTo(approachPoint);
        }

        /// <summary>
        /// 이동 완료 후 플레이어가 지정한 월드 방향을 수평으로 바라보게 한다.
        /// 문 통과처럼 같은 접근 지점을 반대 방향으로 재사용하는 경우 최종 방향을 별도로 확정할 때 사용한다.
        /// </summary>
        /// <param name="worldDirection">플레이어가 바라볼 월드 방향.</param>
        public void FaceDirection(Vector3 worldDirection)
        {
            Vector3 horizontalDirection = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
            if (horizontalDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(horizontalDirection.normalized, Vector3.up);
        }

        /// <summary>진행 중인 이동을 중단하고 다음 명령이 이전 경로를 재사용하지 않게 한다.</summary>
        public void AbortMove()
        {
            mover?.Abort();
        }

        /// <summary>체크포인트의 Transform 위치와 방향으로 플레이어를 즉시 배치한다.</summary>
        public bool WarpTo(Transform respawnPoint)
        {
            return mover != null && mover.WarpTo(respawnPoint);
        }

        /// <summary>
        /// 컴포넌트를 처음 추가할 때 같은 GameObject의 NavMeshAgent를 자동 연결한다.
        /// </summary>
        private void Reset()
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
            pauseService = FindFirstObjectByType<GamePauseService>();
        }
    }
}
