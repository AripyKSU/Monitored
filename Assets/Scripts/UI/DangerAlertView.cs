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

            // 눈동자의 앵커는 직계 부모를 기준으로 계산된다. Gameplay HUD처럼 부모가
            // 작은 고정 Rect로 저장되어 있으면 (0, -48)도 화면 밖 좌표가 되므로,
            // 전용 HUD 부모를 먼저 Canvas 전체 영역으로 확장한다.
            StretchParentToCanvas();

            RectTransform rectTransform = alertImage.rectTransform;
            rectTransform.anchorMin = new Vector2(0.5f, 1f);
            rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = topCenterOffset;
        }

        /// <summary>
        /// 눈동자 전용 부모 RectTransform을 가장 가까운 Canvas의 전체 화면 영역으로 확장한다.
        /// 해상도와 Canvas Scaler 배율이 바뀌어도 자식의 상단 중앙 앵커가 화면을 기준으로 유지된다.
        /// </summary>
        private void StretchParentToCanvas()
        {
            RectTransform parentRect = alertImage.rectTransform.parent as RectTransform;
            Canvas canvas = alertImage.canvas;
            if (parentRect == null || canvas == null || parentRect == canvas.transform)
            {
                return;
            }

            parentRect.anchorMin = Vector2.zero;
            parentRect.anchorMax = Vector2.one;
            parentRect.pivot = new Vector2(0.5f, 0.5f);
            parentRect.anchoredPosition = Vector2.zero;
            parentRect.sizeDelta = Vector2.zero;
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
