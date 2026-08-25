using UnityEngine;
using UnityEngine.AI;

namespace HauntedHouse.Enemies
{
    /// <summary>
    /// EnemyController가 결정한 순찰·추격·대기 상태를 Animator Trigger로 변환한다.
    /// 실제 이동 판정은 NavMeshAgent의 이동 속도와 정지 상태를 함께 확인해,
    /// 문 통과나 은신 방 대기처럼 논리 상태는 추격이지만 실제로 멈춘 경우에도
    /// 걷기 애니메이션이 계속 재생되지 않도록 한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyController))]
    public sealed class EnemyAnimationController : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private EnemyController enemyController;
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private Animator animator;

        [Header("Animator Trigger")]
        [SerializeField] private string startWalkingTrigger = "StartWalking";
        [SerializeField] private string stopWalkingTrigger = "StopWalking";
        [SerializeField] private string lookAroundTrigger = "LookAround";

        [Header("이동 판정")]
        [SerializeField, Min(0f)] private float movementThreshold = 0.01f;

        private bool walking;

        /// <summary>현재 걷기 애니메이션 상태인지 반환한다.</summary>
        public bool IsWalking => walking;

        /// <summary>필수 참조를 자동으로 보완하고 애니메이션 구성을 확인한다.</summary>
        private void Awake()
        {
            enemyController ??= GetComponent<EnemyController>();
            agent ??= GetComponent<NavMeshAgent>();
            animator ??= GetComponentInChildren<Animator>(true);

            if (animator == null)
            {
                GameLog.LogError(
                    $"EnemyAnimationController '{name}'에 Animator 참조가 없습니다.",
                    this);
            }
        }

        /// <summary>적 상태 변경 이벤트를 구독한다.</summary>
        private void OnEnable()
        {
            if (enemyController != null)
            {
                enemyController.StateChanged -= HandleStateChanged;
                enemyController.StateChanged += HandleStateChanged;
            }

            RefreshAnimation(true);
        }

        /// <summary>적 상태 변경 이벤트를 해제한다.</summary>
        private void OnDisable()
        {
            if (enemyController != null)
            {
                enemyController.StateChanged -= HandleStateChanged;
            }
        }

        /// <summary>
        /// 상태 이벤트만으로 놓칠 수 있는 NavMeshAgent의 실제 정지·재개를 보정한다.
        /// 목적지 설정 직후에는 desiredVelocity도 함께 확인해 첫 프레임의 지연을 줄인다.
        /// </summary>
        private void Update()
        {
            if (enemyController == null || animator == null)
            {
                return;
            }

            bool shouldWalk = ShouldPlayWalking();
            if (shouldWalk != walking)
            {
                SetWalking(shouldWalk, false);
            }
        }

        /// <summary>EnemyController의 상태가 바뀌면 즉시 애니메이션 전환을 요청한다.</summary>
        /// <param name="previousState">변경 전 상태.</param>
        /// <param name="currentState">변경 후 상태.</param>
        private void HandleStateChanged(EnemyState previousState, EnemyState currentState)
        {
            if (currentState == EnemyState.Inactive || currentState == EnemyState.Waiting)
            {
                SetWalking(false, true);
                return;
            }

            if (currentState == EnemyState.Patrolling || currentState == EnemyState.Chasing)
            {
                RefreshAnimation(true);
            }
        }

        /// <summary>
        /// 현재 적이 실제로 이동 중인지 확인한다.
        /// Waiting, Inactive, Doorway 대기 상태에서는 걷기를 재생하지 않는다.
        /// </summary>
        private bool ShouldPlayWalking()
        {
            if (enemyController.State != EnemyState.Patrolling &&
                enemyController.State != EnemyState.Chasing)
            {
                return false;
            }

            if (agent == null || !agent.enabled || !agent.isOnNavMesh || agent.isStopped)
            {
                return false;
            }

            float thresholdSquared = movementThreshold * movementThreshold;
            return agent.velocity.sqrMagnitude > thresholdSquared ||
                   agent.desiredVelocity.sqrMagnitude > thresholdSquared;
        }

        /// <summary>현재 NavMeshAgent와 적 상태를 기준으로 애니메이션을 동기화한다.</summary>
        /// <param name="force">이미 같은 상태여도 Trigger를 다시 보낼지 여부.</param>
        private void RefreshAnimation(bool force)
        {
            SetWalking(ShouldPlayWalking(), force);
        }

        /// <summary>
        /// 걷기 시작·종료 Trigger를 서로 정리한 뒤 Animator에 전달한다.
        /// 정지 시 LookAround Trigger도 함께 보내 기본 Idle 동작으로 복귀시킨다.
        /// </summary>
        /// <param name="value">걷기 애니메이션을 재생할지 여부.</param>
        /// <param name="force">현재 상태와 같아도 Trigger를 보낼지 여부.</param>
        private void SetWalking(bool value, bool force)
        {
            if (animator == null || (!force && walking == value))
            {
                return;
            }

            walking = value;
            if (value)
            {
                animator.ResetTrigger(stopWalkingTrigger);
                animator.ResetTrigger(lookAroundTrigger);
                animator.SetTrigger(startWalkingTrigger);
            }
            else
            {
                animator.ResetTrigger(startWalkingTrigger);
                animator.SetTrigger(stopWalkingTrigger);
                animator.SetTrigger(lookAroundTrigger);
            }
        }

        /// <summary>컴포넌트를 추가할 때 같은 오브젝트의 이동 참조를 기본값으로 연결한다.</summary>
        private void Reset()
        {
            enemyController = GetComponent<EnemyController>();
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>(true);
        }
    }
}
