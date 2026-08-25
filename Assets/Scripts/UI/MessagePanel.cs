using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace HauntedHouse.UI
{
    /// <summary>
    /// 획득, 실패와 일반 조사 메시지를 공통 화면에 표시하고 사용자 확인까지 기다린다.
    /// scaled time을 사용하지 않으므로 게임 일시정지 중에도 닫을 수 있다.
    /// </summary>
    public sealed class MessagePanel : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text messageLabel;
        [SerializeField] private Button confirmButton;

        private bool awaitingConfirmation;

        private void Awake()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(Dismiss);
            }

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(Dismiss);
            }
        }

        public IEnumerator Show(string message)
        {
            if (panelRoot == null || messageLabel == null || confirmButton == null)
            {
                GameLog.LogError($"MessagePanel '{name}'의 필수 참조가 누락됐습니다.", this);
                yield break;
            }

            messageLabel.text = message ?? string.Empty;
            awaitingConfirmation = true;
            panelRoot.SetActive(true);

            while (awaitingConfirmation)
            {
                yield return null;
            }

            panelRoot.SetActive(false);
        }

        public void Dismiss()
        {
            awaitingConfirmation = false;
        }

        private void OnDisable()
        {
            awaitingConfirmation = false;
        }
    }
}
