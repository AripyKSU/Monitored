using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HauntedHouse.Dialogue
{
    /// <summary>
    /// 실제 대화 UI 요소의 표시 상태만 관리한다.
    /// 재생 순서나 입력 정책은 판단하지 않는다.
    /// </summary>
    public sealed class DialogueView : MonoBehaviour
    {
        [Header("화면 구성")]
        [SerializeField] private GameObject root;
        [SerializeField] private GameObject radioRoot;
        [SerializeField] private Image bubbleImage;
        [SerializeField] private TMP_Text dialogueLabel;

        [Header("말풍선 방향")]
        [Tooltip("Radio 화자의 말풍선 Image X 배율. 현재 스프라이트의 기본 방향에 맞춰 설정한다.")]
        [SerializeField] private float radioBubbleScaleX = 1f;

        [Tooltip("Player 화자의 말풍선 Image X 배율. 보통 Radio 방향의 반대 부호다.")]
        [SerializeField] private float playerBubbleScaleX = -1f;

        private void Awake()
        {
            root ??= transform.Find("Dialogue UI")?.gameObject;
            radioRoot ??= transform.Find("Dialogue UI/Radio")?.gameObject;
            bubbleImage ??= transform.Find("Dialogue UI/BubbleArea/Bubble")?.GetComponent<Image>();
            dialogueLabel ??= transform.Find("Dialogue UI/BubbleArea/DialogueText")?.GetComponent<TMP_Text>();

            Hide();
        }

        public void Show()
        {
            if (root != null)
            {
                root.SetActive(true);
            }
        }

        public void Hide()
        {
            if (root != null)
            {
                root.SetActive(false);
            }

            if (dialogueLabel != null)
            {
                dialogueLabel.text = string.Empty;
                dialogueLabel.maxVisibleCharacters = 0;
            }
        }

        public void SetSpeaker(DialogueSpeaker speaker)
        {
            bool isRadio = speaker == DialogueSpeaker.Radio;

            if (radioRoot != null)
            {
                radioRoot.SetActive(isRadio);
            }

            if (bubbleImage != null)
            {
                Vector3 scale = bubbleImage.rectTransform.localScale;
                scale.x = isRadio ? radioBubbleScaleX : playerBubbleScaleX;
                bubbleImage.rectTransform.localScale = scale;
            }
        }

        public int SetText(string text)
        {
            if (dialogueLabel == null)
            {
                return 0;
            }

            dialogueLabel.text = text ?? string.Empty;
            dialogueLabel.maxVisibleCharacters = 0;
            dialogueLabel.ForceMeshUpdate();

            return dialogueLabel.textInfo.characterCount;
        }

        public void SetVisibleCharacters(int count)
        {
            if (dialogueLabel != null)
            {
                dialogueLabel.maxVisibleCharacters = count;
            }
        }

        public void ShowAllCharacters()
        {
            if (dialogueLabel != null)
            {
                dialogueLabel.maxVisibleCharacters = int.MaxValue;
            }
        }

        public bool IsValid()
        {
            return root != null && bubbleImage != null && dialogueLabel != null;
        }
    }
}
