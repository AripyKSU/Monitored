using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// Target 도착 후 공용 메시지 UI를 표시하고 사용자가 확인할 때까지 명령 완료를 기다린다.
    /// </summary>
    public sealed class MessageTarget : MonoBehaviour, ITargetAction, ICompletedState
    {
        [SerializeField] private MessagePanel messagePanel;
        [SerializeField, TextArea] private string message;
        [SerializeField] private string completionId;

        [Header("스토리 진행")]
        [Tooltip("메시지를 끝까지 읽었을 때 추가할 Story Flag입니다. 비워 두면 기존 메시지 Target처럼 동작합니다.")]
        [SerializeField] private GameSession gameSession;
        [SerializeField] private string completionFlagId;
        [Tooltip("완료 후에도 Target을 다시 선택할 수 있게 유지합니다.")]
        [SerializeField] private bool allowRepeatedInteraction;

        [Header("Audio")]
        [SerializeField] private AudioClip messageInteractionSound;

        /// <summary>메시지 상호작용이 완료되었는지 반환한다.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>체크포인트에서 사용할 상호작용 완료 ID다.</summary>
        public string CompletionId => string.IsNullOrWhiteSpace(completionId) ? name : completionId;

        /// <summary>메시지를 표시하고 확인된 뒤 완료 상태와 선택적인 Story Flag를 기록한다.</summary>
        public IEnumerator Execute(TargetActionContext context)
        {
            if (messagePanel == null)
            {
                GameLog.LogError($"MessageTarget '{name}'에 MessagePanel이 연결되지 않았습니다.", this);
                yield break;
            }

            yield return messagePanel.Show(message);
            if (messageInteractionSound != null)
            {
                HauntedHouse.Audio.AudioManager.Instance?.PlayUiSfx(messageInteractionSound);
            }

            IsCompleted = true;
            // 메시지 확인이 끝난 시점에만 Story Flag를 기록해 중도 취소·복원 단계의 오기록을 막는다.
            if (gameSession != null && !string.IsNullOrWhiteSpace(completionFlagId))
            {
                gameSession.SetStoryFlag(completionFlagId);
            }

            // 반복 조사 대상은 첫 확인에서 진행 상태만 기록하고 Target 자체는 계속 선택 가능하게 유지한다.
            bool remainsSelectable = AllowsRepeatedInteraction;
            GetComponent<Target>()?.SetSelectable(remainsSelectable);
            if (!remainsSelectable)
            {
                context?.MarkCompleted();
            }
        }

        /// <summary>체크포인트의 메시지 상호작용 완료 상태를 적용한다.</summary>
        public void RestoreCompletedState(bool completed)
        {
            // Story Flag는 체크포인트에 저장되는 진행 기준이므로 완료 상태 배열에
            // 별도 항목이 없어도 flag가 켜진 메시지는 다시 선택할 수 없게 한다.
            bool completedByFlag = gameSession != null &&
                                   !string.IsNullOrWhiteSpace(completionFlagId) &&
                                   gameSession.HasStoryFlag(completionFlagId);
            IsCompleted = completed || completedByFlag;
            GetComponent<Target>()?.SetSelectable(AllowsRepeatedInteraction || !IsCompleted);
        }

        /// <summary>완료 후에도 메시지를 다시 조사할 수 있는 대상인지 반환한다.</summary>
        private bool AllowsRepeatedInteraction =>
            allowRepeatedInteraction ||
            completionFlagId == StoryProgressFlags.FreezerHintRead ||
            CompletionId is "MorgueBoxA" or "Morgue1_Box_C" or "Morgue1_Box_D";

        /// <summary>컴포넌트를 처음 추가할 때 공용 UI와 진행 세션을 자동 연결한다.</summary>
        private void Reset()
        {
            messagePanel = FindFirstObjectByType<MessagePanel>();
            gameSession = FindFirstObjectByType<GameSession>();
            completionId = name;
        }
    }
}
