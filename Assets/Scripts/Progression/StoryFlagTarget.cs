using HauntedHouse.Commands;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// 하나의 Story flag가 활성화된 경우에만 같은 GameObject의 Target을 선택 가능하게 한다.
    /// Target GameObject를 끄지 않고 선택 상태만 갱신해 체크포인트 복원과 기존 참조를 보존한다.
    /// </summary>
    [RequireComponent(typeof(Target))]
    public sealed class StoryFlagTarget : MonoBehaviour
    {
        [Header("조건")]
        [SerializeField] private string requiredFlagId;

        [Header("공용 진행 상태")]
        [SerializeField] private GameSession gameSession;
        [SerializeField] private Target target;

        /// <summary>현재 요구 flag가 활성화되어 Target을 선택할 수 있는지 반환한다.</summary>
        public bool IsConditionMet => gameSession != null &&
                                       !string.IsNullOrWhiteSpace(requiredFlagId) &&
                                       gameSession.HasStoryFlag(requiredFlagId);

        /// <summary>flag 변경과 플레이 시작 시점에 Target 상태를 갱신한다.</summary>
        private void OnEnable()
        {
            if (gameSession != null)
            {
                gameSession.StoryFlags.Changed -= HandleStoryFlagsChanged;
                gameSession.StoryFlags.Changed += HandleStoryFlagsChanged;
                gameSession.GameplayStarted -= HandleGameplayStarted;
                gameSession.GameplayStarted += HandleGameplayStarted;
            }

            RefreshTargetState();
        }

        /// <summary>컴포넌트가 비활성화될 때 진행 상태 이벤트 구독을 해제한다.</summary>
        private void OnDisable()
        {
            if (gameSession != null)
            {
                gameSession.StoryFlags.Changed -= HandleStoryFlagsChanged;
                gameSession.GameplayStarted -= HandleGameplayStarted;
            }
        }

        /// <summary>Story flag가 바뀌면 조건 결과를 다시 계산한다.</summary>
        private void HandleStoryFlagsChanged()
        {
            RefreshTargetState();
        }

        /// <summary>체크포인트 복원이 끝난 뒤 복원된 flag 기준으로 Target을 다시 계산한다.</summary>
        private void HandleGameplayStarted()
        {
            RefreshTargetState();
        }

        /// <summary>
        /// 현재 조건을 Target의 선택 상태에 반영한다.
        /// GameObject 자체를 비활성화하지 않아 다른 시스템의 참조와 체크포인트 대상은 유지한다.
        /// </summary>
        private void RefreshTargetState()
        {
            if (target != null)
            {
                target.SetSelectable(IsConditionMet);
            }
        }

        /// <summary>컴포넌트를 처음 추가할 때 현재 씬의 진행 상태와 Target을 연결한다.</summary>
        private void Reset()
        {
            target = GetComponent<Target>();
            gameSession = FindFirstObjectByType<GameSession>();
        }

        /// <summary>Inspector에서 참조나 flag가 바뀐 경우 편집 모드에서도 Target 상태를 갱신한다.</summary>
        private void OnValidate()
        {
            target ??= GetComponent<Target>();
            RefreshTargetState();
        }
    }
}
