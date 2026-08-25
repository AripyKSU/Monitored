using HauntedHouse.Threat;
using UnityEngine;
using UnityEngine.UI;

namespace HauntedHouse.UI
{
    /// <summary>
    /// 화면 중앙 위쪽에 현재 위험경보 아이콘 하나를 표시한다.
    /// 경보 규칙은 DangerAlertSystem이 계산하며 이 컴포넌트는 Image 표현만 담당한다.
    /// </summary>
    public sealed class DangerAlertView : MonoBehaviour
    {
        [Header("경보 Image")]
        [SerializeField] private Image alertImage;
        [SerializeField] private Sprite cautionSprite;
        [SerializeField] private Sprite emergencySprite;

        [Header("상단 중앙 배치")]
        [SerializeField] private bool forceTopCenterLayout = true;
        [SerializeField] private Vector2 topCenterOffset = new Vector2(0f, -48f);

        [Header("경보 시스템")]
        [SerializeField] private DangerAlertSystem alertSystem;

        /// <summary>현재 Image에 반영된 경보 단계를 반환한다.</summary>
        public DangerAlertLevel CurrentLevel { get; private set; } = DangerAlertLevel.None;

        /// <summary>경보 시스템 참조를 보완하고 Image를 화면 상단 중앙에 배치한다.</summary>
        private void Awake()
        {
            alertImage ??= GetComponent<Image>();
            alertSystem ??= FindFirstObjectByType<DangerAlertSystem>();

            if (forceTopCenterLayout)
            {
                ApplyTopCenterLayout();
            }
        }

        /// <summary>활성화 시 현재 경보를 구독하고 즉시 Image를 동기화한다.</summary>
        private void OnEnable()
        {
            if (alertSystem == null)
            {
                return;
            }

            alertSystem.AlertChanged -= HandleAlertChanged;
            alertSystem.AlertChanged += HandleAlertChanged;
            SetAlert(alertSystem.CurrentLevel);
        }

        /// <summary>비활성화 시 경보 시스템 이벤트 구독을 해제한다.</summary>
        private void OnDisable()
        {
            if (alertSystem != null)
            {
                alertSystem.AlertChanged -= HandleAlertChanged;
            }
        }

        /// <summary>경보 시스템에서 전달된 단계를 Image에 반영한다.</summary>
        private void HandleAlertChanged(DangerAlertLevel level)
        {
            SetAlert(level);
        }

        /// <summary>
        /// 지정된 경보 단계에 맞는 Sprite를 설정하고 None이면 Image를 숨긴다.
        /// </summary>
        /// <param name="level">표시할 경보 단계.</param>
        public void SetAlert(DangerAlertLevel level)
        {
            CurrentLevel = level;

            if (alertImage == null)
            {
                return;
            }

            alertImage.sprite = level switch
            {
                DangerAlertLevel.Caution => cautionSprite,
                DangerAlertLevel.Emergency => emergencySprite,
                _ => null
            };

            bool shouldShow = level != DangerAlertLevel.None && alertImage.sprite != null;
            alertImage.enabled = shouldShow;
        }

        /// <summary>Image RectTransform의 Anchor를 화면 중앙 상단으로 설정한다.</summary>
        private void ApplyTopCenterLayout()
        {
            if (alertImage == null)
            {
                return;
            }

            RectTransform rectTransform = alertImage.rectTransform;
            rectTransform.anchorMin = new Vector2(0.5f, 1f);
            rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = topCenterOffset;
        }

        /// <summary>컴포넌트를 처음 추가할 때 같은 GameObject의 Image를 연결한다.</summary>
        private void Reset()
        {
            alertImage = GetComponent<Image>();
            if (alertImage != null)
            {
                alertImage.raycastTarget = false;
            }

            alertSystem = FindFirstObjectByType<DangerAlertSystem>();
        }
    }
}
