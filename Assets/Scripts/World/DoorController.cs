using System.Collections;
using HauntedHouse.Audio;
using UnityEngine;

namespace HauntedHouse.World
{
    /// <summary>
    /// 문 Visual의 Animator에 열기·닫기 Trigger를 전달하고 연출 완료 시점을 관리한다.
    /// 실제 이동·회전 방식은 문별 Animator Controller와 AnimationClip에 위임한다.
    /// </summary>
    public sealed class DoorController : MonoBehaviour
    {
        [Header("문 애니메이션")]
        [Tooltip("문 Visual 오브젝트에 붙은 Animator입니다.")]
        [SerializeField] private Animator animator;
        [Tooltip("문을 여는 Animator Trigger 이름입니다.")]
        [SerializeField] private string openTrigger = "Open";
        [Tooltip("문을 닫는 Animator Trigger 이름입니다.")]
        [SerializeField] private string closeTrigger = "Close";
        [Tooltip("닫힌 상태의 Animator State 이름입니다.")]
        [SerializeField] private string closedStateName = "Closed";
        [Tooltip("열린 상태의 Animator State 이름입니다.")]
        [SerializeField] private string openStateName = "Open";
        [SerializeField, Min(0f)] private float animationDuration = 0.25f;
        [Tooltip("열린 동안 통로를 막지 않도록 비활성화할 실제 문짝 Collider입니다.")]
        [SerializeField] private Collider[] movingLeafColliders = System.Array.Empty<Collider>();

 [Header("문 사운드")]
 [SerializeField] private AudioClip movementSound;
 [SerializeField, Range(0f, 1f)] private float movementSoundVolume = 1f;

        public bool IsOpen { get; private set; }

        /// <summary>
        /// Animator 참조가 비어 있는 경우 DoorVisual 자식에서 자동으로 찾아 초기화한다.
        /// </summary>
        private void Awake()
        {
            ResolveAnimator();
            if (animator == null)
            {
                GameLog.LogError($"DoorController '{name}'에 DoorVisual Animator가 연결되지 않았습니다.", this);
                enabled = false;
                return;
            }

            IsOpen = false;
            SetMovingLeafCollision(true);
        }

        /// <summary>
        /// Inspector 참조가 없는 경우 현재 문 계층의 Animator를 찾는다.
        /// </summary>
        private void Reset()
        {
            ResolveAnimator();
        }

        /// <summary>
        /// 문 Visual의 Animator를 찾아 필드에 저장한다.
        /// </summary>
        private void ResolveAnimator()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }
        }

        /// <summary>
        /// 문이 닫혀 있으면 Open Trigger를 전달하고 열기 연출 완료까지 기다린다.
        /// </summary>
        public IEnumerator Open()
        {
            if (IsOpen || !TryPrepareAnimation())
            {
                yield break;
            }

            // 닫기 Trigger가 남아 있으면 전이 조건이 충돌할 수 있으므로 먼저 제거한다.
            animator.ResetTrigger(closeTrigger);
            animator.SetTrigger(openTrigger);
 PlayMovementSound();
            yield return WaitForAnimation();
            animator.Play(openStateName, 0, 1f);
            // 열린 포즈에서도 회전 피벗이나 메시 두께 때문에 문짝 Collider가 통로를 가로막지 않게 한다.
            SetMovingLeafCollision(false);
            IsOpen = true;
        }

        /// <summary>
        /// 플레이어가 목적지 방 진입 위치에 도착한 뒤 Close Trigger로 문을 닫는다.
        /// </summary>
        public IEnumerator Close()
        {
            if (!IsOpen || !TryPrepareAnimation())
            {
                yield break;
            }

            // 열기 Trigger가 남아 있으면 닫기 전이가 다시 열림 상태로 돌아갈 수 있다.
            animator.ResetTrigger(openTrigger);
            animator.SetTrigger(closeTrigger);
 PlayMovementSound();
            yield return WaitForAnimation();
            animator.Play(closedStateName, 0, 1f);
            // 한 번 열린 문은 논리적 Doorway가 통행을 제어한다.
            // 닫는 순간 문짝 Collider를 되살리면 목적지에 선 Actor를 물리적으로 문 안으로 밀어 넣을 수 있어 비활성 상태를 유지한다.
            SetMovingLeafCollision(false);
            IsOpen = false;
        }

        /// <summary>
        /// 이동 실패나 컴포넌트 비활성화 시 Animator를 닫힌 State로 즉시 복구한다.
        /// </summary>
        public void ForceClosed()
        {
            if (animator != null)
            {
                // 진행 중인 Trigger와 전이를 모두 초기화한 뒤 닫힌 포즈를 즉시 적용한다.
                animator.ResetTrigger(openTrigger);
                animator.ResetTrigger(closeTrigger);
                animator.Play(closedStateName, 0, 0f);
                animator.Update(0f);
            }

            // 실패 복구 시에도 이미 문 경계에 들어간 Actor를 Collider로 가두지 않는다.
            SetMovingLeafCollision(false);
            IsOpen = false;
        }

        /// <summary>실제로 애니메이션되는 문짝 Collider만 일괄 활성화하거나 비활성화한다.</summary>
        private void SetMovingLeafCollision(bool enabled)
        {
            if (movingLeafColliders == null)
            {
                return;
            }

            foreach (Collider leafCollider in movingLeafColliders)
            {
                if (leafCollider != null)
                {
                    leafCollider.enabled = enabled;
                }
            }
        }

        /// <summary>
        /// Animator와 Trigger 설정이 유효한지 확인한다.
        /// </summary>
        private bool TryPrepareAnimation()
        {
            ResolveAnimator();
            if (animator == null)
            {
                GameLog.LogError($"DoorController '{name}'의 Animator가 없습니다.", this);
                return false;
            }

            if (string.IsNullOrWhiteSpace(openTrigger) || string.IsNullOrWhiteSpace(closeTrigger))
            {
                GameLog.LogError($"DoorController '{name}'의 Open/Close Trigger 이름이 비어 있습니다.", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 문별 AnimationClip 길이만큼 대기해 Doorway 이동이 연출보다 먼저 시작되지 않게 한다.
        /// </summary>
        private void PlayMovementSound()
 {
 if (movementSound == null)
 {
 return;
 }

 AudioManager.Instance?.PlaySfx(movementSound, transform.position, movementSoundVolume);
 }

 private IEnumerator WaitForAnimation()
        {
            if (animationDuration <= 0f)
            {
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }
}
