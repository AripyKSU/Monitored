using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.UI;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// Target 도착 후 공용 메시지 UI를 표시하고 사용자가 확인할 때까지 명령 완료를 기다린다.
    /// </summary>
    public sealed class MessageTarget : MonoBehaviour, ITargetAction
    {
        [SerializeField] private MessagePanel messagePanel;
        [SerializeField, TextArea] private string message;

        public IEnumerator Execute(TargetActionContext context)
        {
            if (messagePanel == null)
            {
                GameLog.LogError($"MessageTarget '{name}'에 MessagePanel이 연결되지 않았습니다.", this);
                yield break;
            }

            yield return messagePanel.Show(message);
        }

        private void Reset()
        {
            messagePanel = FindFirstObjectByType<MessagePanel>();
        }
    }
}
