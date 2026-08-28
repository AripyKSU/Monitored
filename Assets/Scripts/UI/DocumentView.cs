using HauntedHouse.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HauntedHouse.UI
{
    /// <summary>선택 이미지 뒤에 회색 테두리 본문 패널을 겹쳐 표시하는 공용 문서 조사 화면이다.</summary>
    public sealed class DocumentView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private TMP_Text bodyLabel;

        private bool initialized;

        /// <summary>참조를 확인하고 게임 시작 시 문서 화면을 숨긴다.</summary>
        private void Awake()
        {
            ResolveReferences();
            if (!initialized)
            {
                initialized = true;
                Hide();
            }
        }

        /// <summary>문서 데이터의 선택 이미지와 본문을 화면에 표시한다.</summary>
        public void Show(DocumentDefinition document)
        {
            ResolveReferences();
            if (document == null || root == null || bodyLabel == null)
            {
                return;
            }

            // 비활성 오브젝트의 첫 활성화 과정에서 Awake가 표시 데이터를 다시 지우지 않게
            // Show 요청이 초기 상태를 먼저 확정한다.
            initialized = true;
            bool hasImage = document.BackgroundImage != null;
            if (backgroundImage != null)
            {
                backgroundImage.sprite = document.BackgroundImage;
                backgroundImage.gameObject.SetActive(hasImage);
                backgroundImage.preserveAspect = true;
            }

            bodyLabel.text = document.BodyText;
            root.SetActive(true);
        }

        /// <summary>이미지와 본문 UI를 함께 숨기고 표시 데이터를 정리한다.</summary>
        public void Hide()
        {
            ResolveReferences();
            if (root != null)
            {
                root.SetActive(false);
            }

            if (backgroundImage != null)
            {
                backgroundImage.sprite = null;
            }

            if (bodyLabel != null)
            {
                bodyLabel.text = string.Empty;
            }
        }

        public bool IsValid()
        {
            ResolveReferences();
            return root != null && bodyLabel != null;
        }

        private void ResolveReferences()
        {
            root ??= transform.Find("Document UI")?.gameObject;
            backgroundImage ??= transform.Find("Document UI/BackgroundImage")?.GetComponent<Image>();
            bodyLabel ??= transform.Find("Document UI/BorderPanel/BodyText")?.GetComponent<TMP_Text>();
        }
    }
}
