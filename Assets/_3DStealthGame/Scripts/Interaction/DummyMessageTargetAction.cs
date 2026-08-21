using System.Collections;
using HauntedHouse.Commands;
using UnityEngine;
using UnityEngine.UI;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// CoreSandbox에서 Target 도착 후 행동과 Executing 대기를 검증하기 위한 임시 행동이다.
    /// 설정된 메시지를 화면에 잠시 표시한 뒤 종료하며 실제 아이템·문·은신 규칙은 포함하지 않는다.
    /// </summary>
    public sealed class DummyMessageTargetAction : MonoBehaviour, ITargetAction
    {
        [Header("검증 메시지")]
        [SerializeField] private Text messageLabel;
        [SerializeField, TextArea] private string message = "대상 행동을 실행했습니다.";
        [SerializeField, Min(0f)] private float displayDuration = 1.25f;

        /// <summary>
        /// Target 이름을 포함한 더미 메시지를 표시하고 설정 시간 동안 행동 완료를 지연한다.
        /// </summary>
        /// <param name="context">현재 Target과 Player를 담은 행동 실행 정보.</param>
        public IEnumerator Execute(TargetActionContext context)
        {
            string targetName = context.Target != null ? context.Target.name : "Unknown Target";
            string resolvedMessage = $"{targetName}: {message}";

            // 화면 UI 연결 여부와 관계없이 Console에도 남겨 테스트 결과를 추적할 수 있게 한다.
            GameLog.Log(resolvedMessage, this);

            if (messageLabel != null)
            {
                messageLabel.text = resolvedMessage;
                messageLabel.gameObject.SetActive(true);
            }

            // 0초로 설정하면 즉시 완료 행동도 같은 컴포넌트로 검증할 수 있다.
            if (displayDuration > 0f)
            {
                yield return new WaitForSeconds(displayDuration);
            }

            if (messageLabel != null)
            {
                messageLabel.gameObject.SetActive(false);
            }
        }
    }
}
