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

        /// <summary>메시지 상호작용이 완료되었는지 반환한다.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>체크포인트에서 사용할 상호작용 완료 ID다.</summary>
        public string CompletionId => string.IsNullOrWhiteSpace(completionId) ? name : completionId;

        public IEnumerator Execute(TargetActionContext context)
        {
            if (messagePanel == null)
            {
                GameLog.LogError($"MessageTarget '{name}'에 MessagePanel이 연결되지 않았습니다.", this);
                yield break;
            }

            yield return messagePanel.Show(message);
            IsCompleted = true;
            context?.MarkCompleted();
        }

        /// <summary>체크포인트의 메시지 상호작용 완료 상태를 적용한다.</summary>
        public void RestoreCompletedState(bool completed)
        {
            IsCompleted = completed;
            GetComponent<Target>()?.SetSelectable(!completed);
        }

        private void Reset()
        {
            messagePanel = FindFirstObjectByType<MessagePanel>();
            completionId = name;
        }
    }
}
