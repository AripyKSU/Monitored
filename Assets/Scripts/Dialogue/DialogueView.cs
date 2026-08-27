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
            ResolveReferences();
            Hide();
        }

        /// <summary>
        /// Inspector 참조가 비어 있거나 패널 활성화 순서가 달라도 현재 Dialogue UI 계층에서 표현 요소를 다시 찾는다.
        /// </summary>
        private void ResolveReferences()
        {
            if (root == null)
            {
                Transform rootTransform = transform.Find("Dialogue UI");
                if (rootTransform != null)
                {
                    root = rootTransform.gameObject;
                }
            }

            if (radioRoot == null)
            {
                Transform radioTransform = transform.Find("Dialogue UI/Radio");
                if (radioTransform != null)
                {
                    radioRoot = radioTransform.gameObject;
                }
            }

            if (bubbleImage == null)
            {
                Transform bubbleTransform = transform.Find("Dialogue UI/BubbleArea/Bubble");
                bubbleImage = bubbleTransform != null ? bubbleTransform.GetComponent<Image>() : null;
            }

            if (dialogueLabel == null)
            {
                Transform labelTransform = transform.Find("Dialogue UI/BubbleArea/DialogueText");
                dialogueLabel = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
            }
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
                // Bubble Image만 좌우반전한다. DialogueText는 BubbleArea의 형제이므로
                // Text Transform이나 부모를 뒤집지 않아 글자가 거꾸로 보이지 않는다.
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
            // DialogueService가 다른 오브젝트의 Awake에서 먼저 검증하더라도 참조를 지연 보완한다.
            ResolveReferences();
            return root != null && bubbleImage != null && dialogueLabel != null;
        }
    }
}
