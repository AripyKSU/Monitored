using HauntedHouse.Core;
using UnityEngine;
using UnityEngine.AI;

namespace HauntedHouse.Commands
{
    /// <summary>
    /// Player의 NavMeshAgent 이동 상태를 Animator 파라미터로 변환해 대기와 걷기 상태를 전환한다.
    /// 실제 이동 명령은 Player와 PlayerMover가 계속 소유하고, 이 컴포넌트는 표현 계층만 담당한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimationController : MonoBehaviour
    {
        private static readonly int IsMovingParameter = Animator.StringToHash("IsMoving");
        private static readonly int CaptureDeathParameter = Animator.StringToHash("CaptureDeath");
        private const string CaptureDeathStateName = "CaptureDeath";

        [Header("애니메이션 구성")]
        [SerializeField] private Animator animator;
        [SerializeField] private NavMeshAgent navMeshAgent;
        [SerializeField, Min(0f)] private float movementThreshold = 0.05f;

        private bool hasIsMovingParameter;
        private bool hasCaptureDeathParameter;
        private bool captureLocked;

        /// <summary>
        /// Animator와 NavMeshAgent를 자동으로 찾고 컨트롤러 파라미터를 검증한다.
        /// </summary>
        private void Awake()
        {
            // 컴포넌트를 Player 루트에 두어도 모델 자식의 Animator를 사용할 수 있도록 자동 검색한다.
            animator ??= GetComponentInChildren<Animator>(true);
            navMeshAgent ??= GetComponent<NavMeshAgent>();

            if (animator == null)
            {
                GameLog.LogWarning($"PlayerAnimationController '{name}'에 Animator가 연결되지 않았습니다.", this);
                enabled = false;
                return;
            }

            // 컨트롤러가 교체되거나 수동으로 잘못 설정된 경우 매 프레임 파라미터 호출을 시도하지 않는다.
            hasIsMovingParameter = HasIsMovingParameter(animator);
            if (!hasIsMovingParameter)
            {
                GameLog.LogWarning(
                    $"Player Animator '{animator.name}'에 bool 파라미터 'IsMoving'이 없습니다.",
                    animator);
            }

            hasCaptureDeathParameter = HasParameter(animator, CaptureDeathParameter, AnimatorControllerParameterType.Trigger);
            if (!hasCaptureDeathParameter)
            {
                GameLog.LogWarning(
                    $"Player Animator '{animator.name}'에 trigger 파라미터 'CaptureDeath'가 없습니다.",
                    animator);
            }

            // Generic 이동 애니메이션이 캐릭터 루트를 직접 움직이지 않고 NavMeshAgent 위치를 따르도록 한다.
            animator.applyRootMotion = false;
        }

        /// <summary>
        /// 매 프레임 Agent의 실제 수평 속도를 확인해 Idle 또는 Walking 상태를 선택한다.
        /// </summary>
        private void Update()
        {
            if (captureLocked)
            {
                if (hasIsMovingParameter)
                {
                    animator.SetBool(IsMovingParameter, false);
                }

                return;
            }

            if (!hasIsMovingParameter || navMeshAgent == null)
            {
                return;
            }

            // velocity에는 가속·감속이 반영되므로 목적지 설정 여부보다 현재 보이는 움직임과 일치한다.
            Vector3 velocity = navMeshAgent.velocity;
            velocity.y = 0f;
            bool isMoving = navMeshAgent.enabled
                            && navMeshAgent.isOnNavMesh
                            && velocity.sqrMagnitude > movementThreshold * movementThreshold;

            animator.SetBool(IsMovingParameter, isMoving);
        }

        /// <summary>포획 사망 애니메이션을 시작하고 플레이어 이동 표현을 잠근다.</summary>
        public bool PlayCaptureDeath()
        {
            if (animator == null || !hasCaptureDeathParameter)
            {
                return false;
            }

            captureLocked = true;
            if (hasIsMovingParameter)
            {
                animator.SetBool(IsMovingParameter, false);
            }

            if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.ResetPath();
            }

            animator.ResetTrigger(CaptureDeathParameter);
            animator.SetTrigger(CaptureDeathParameter);
            return true;
        }

        /// <summary>포획 사망 상태가 마지막 프레임까지 재생됐는지 반환한다.</summary>
        public bool IsCaptureDeathAnimationFinished()
        {
            if (!captureLocked || animator == null || animator.IsInTransition(0))
            {
                return false;
            }

            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            return stateInfo.IsName(CaptureDeathStateName) && stateInfo.normalizedTime >= 1f;
        }

        /// <summary>다시하기 후 포획 애니메이션 잠금을 해제하고 기본 상태로 복원한다.</summary>
        public void ResetCapturePresentation()
        {
            captureLocked = false;
            if (animator != null)
            {
                animator.ResetTrigger(CaptureDeathParameter);
                animator.Rebind();
                animator.Update(0f);
            }

            if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
            }
        }

        /// <summary>
        /// 체크포인트 복원이나 이동 중단 직후에도 애니메이터를 대기 상태로 되돌린다.
        /// </summary>
        private void OnDisable()
        {
            if (animator != null && hasIsMovingParameter)
            {
                animator.SetBool(IsMovingParameter, false);
            }
        }

        /// <summary>
        /// 지정된 Animator Controller에 IsMoving bool 파라미터가 있는지 확인한다.
        /// </summary>
        private static bool HasIsMovingParameter(Animator targetAnimator)
        {
            return HasParameter(targetAnimator, IsMovingParameter, AnimatorControllerParameterType.Bool);
        }

        private static bool HasParameter(
            Animator targetAnimator,
            int nameHash,
            AnimatorControllerParameterType parameterType)
        {
            foreach (AnimatorControllerParameter parameter in targetAnimator.parameters)
            {
                if (parameter.nameHash == nameHash && parameter.type == parameterType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 컴포넌트를 처음 추가할 때 Player 루트의 NavMeshAgent와 자식 Animator를 자동 연결한다.
        /// </summary>
        private void Reset()
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>(true);
        }
    }
}
