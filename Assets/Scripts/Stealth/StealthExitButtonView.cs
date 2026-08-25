using UnityEngine;
using UnityEngine.UI;

namespace HauntedHouse.Stealth
{
    /// <summary>
    /// 은신 중 우측 하단에 임시 나가기 버튼을 생성하고 은신 시스템에 연결한다.
    /// 추후 정식 UI 프리팹으로 교체할 수 있도록 버튼 생성과 게임 규칙을 분리한다.
    /// </summary>
    public sealed class StealthExitButtonView : MonoBehaviour
    {
        [SerializeField] private StealthHideSystem stealthHideSystem;
        [SerializeField] private RectTransform buttonRoot;
        [SerializeField] private Button exitButton;

        /// <summary>은신 시스템과 임시 버튼을 준비한다.</summary>
        private void Awake()
        {
            stealthHideSystem ??= FindFirstObjectByType<StealthHideSystem>();
            EnsureTemporaryButton();
        }

        /// <summary>은신 상태 이벤트를 구독한다.</summary>
        private void OnEnable()
        {
            if (stealthHideSystem != null)
            {
                stealthHideSystem.StateChanged -= HandleStealthStateChanged;
                stealthHideSystem.StateChanged += HandleStealthStateChanged;
            }

            HandleStealthStateChanged(
                stealthHideSystem != null ? stealthHideSystem.State : StealthState.Visible,
                stealthHideSystem != null ? stealthHideSystem.HidingRoom : null);
        }

        /// <summary>컴포넌트가 비활성화될 때 이벤트를 해제한다.</summary>
        private void OnDisable()
        {
            if (stealthHideSystem != null)
            {
                stealthHideSystem.StateChanged -= HandleStealthStateChanged;
            }
        }

        /// <summary>은신 상태에 따라 임시 버튼을 표시하거나 숨긴다.</summary>
        private void HandleStealthStateChanged(StealthState state, Room hidingRoom)
        {
            if (buttonRoot != null)
            {
                buttonRoot.gameObject.SetActive(state == StealthState.Hidden);
            }
        }

        /// <summary>임시 버튼이 연결되지 않은 경우 Canvas 우측 하단에 런타임으로 생성한다.</summary>
        private void EnsureTemporaryButton()
        {
            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(HandleExitButtonClicked);
                exitButton.onClick.AddListener(HandleExitButtonClicked);
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            canvas ??= FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                GameLog.LogError("StealthExitButtonView가 사용할 Canvas를 찾지 못했습니다.", this);
                return;
            }

            GameObject buttonObject = new GameObject(
                "Temporary Exit Hide Button",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(canvas.transform, false);

            buttonRoot = buttonObject.GetComponent<RectTransform>();
            buttonRoot.anchorMin = new Vector2(1f, 0f);
            buttonRoot.anchorMax = new Vector2(1f, 0f);
            buttonRoot.pivot = new Vector2(1f, 0f);
            buttonRoot.anchoredPosition = new Vector2(-32f, 32f);
            buttonRoot.sizeDelta = new Vector2(180f, 64f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.12f, 0.16f, 0.22f, 0.95f);

            exitButton = buttonObject.GetComponent<Button>();
            exitButton.targetGraphic = image;
            exitButton.onClick.AddListener(HandleExitButtonClicked);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = "나가기";
            label.fontSize = 24;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;

            buttonRoot.gameObject.SetActive(false);
        }

        /// <summary>임시 버튼 클릭을 은신 해제 요청으로 변환한다.</summary>
        private void HandleExitButtonClicked()
        {
            stealthHideSystem?.TryExitHide();
        }

        /// <summary>컴포넌트를 처음 추가했을 때 은신 시스템을 연결한다.</summary>
        private void Reset()
        {
            stealthHideSystem = FindFirstObjectByType<StealthHideSystem>();
        }
    }
}
