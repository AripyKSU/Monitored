using System.Collections;
using UnityEngine;

namespace HauntedHouse.Dialogue
{
    /// <summary>
    /// DialogueLine 하나를 UI에 타이핑하는 책임을 가진다.
    /// Sequence 진행과 Pause/입력 정책은 DialogueService가 담당한다.
    /// </summary>
    public sealed class DialoguePresenter : MonoBehaviour
    {
        [SerializeField] private DialogueView view;
        [SerializeField, Min(1f)] private float charactersPerSecond = 30f;

        private bool isTyping;
        private bool skipTypingRequested;

        public bool IsTyping => isTyping;

        private void Awake()
        {
            view ??= GetComponent<DialogueView>();
        }

        public IEnumerator ShowLine(DialogueLine line)
        {
            if (line == null || view == null || !view.IsValid())
            {
                GameLog.LogError($"DialoguePresenter '{name}'의 필수 참조가 누락됐습니다.", this);
                yield break;
            }

            skipTypingRequested = false;
            isTyping = true;

            view.Show();
            view.SetSpeaker(line.Speaker);

            int characterCount = view.SetText(line.Text);
            if (characterCount <= 0)
            {
                isTyping = false;
                yield break;
            }

            float secondsPerCharacter = 1f / Mathf.Max(1f, charactersPerSecond);
            float elapsed = 0f;
            int visibleCharacters = 0;

            while (visibleCharacters < characterCount && !skipTypingRequested)
            {
                elapsed += Time.unscaledDeltaTime;

                int targetVisible = Mathf.Min(
                    characterCount,
                    Mathf.FloorToInt(elapsed / secondsPerCharacter));

                if (targetVisible > visibleCharacters)
                {
                    visibleCharacters = targetVisible;
                    view.SetVisibleCharacters(visibleCharacters);
                }

                yield return null;
            }

            view.ShowAllCharacters();
            isTyping = false;
            skipTypingRequested = false;
        }

        public void CompleteTyping()
        {
            if (!isTyping)
            {
                return;
            }

            skipTypingRequested = true;
            view?.ShowAllCharacters();
        }

        public void Hide()
        {
            isTyping = false;
            skipTypingRequested = false;
            view?.Hide();
        }

        public bool IsValid()
        {
            return view != null && view.IsValid();
        }
    }
}
