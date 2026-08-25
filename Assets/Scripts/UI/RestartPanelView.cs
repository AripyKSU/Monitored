using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using UnityEngine;
using UnityEngine.UI;

namespace HauntedHouse.UI
{
    /// <summary>
    /// 사망 확정 후 화면 중앙에 임시 Restart 버튼을 표시한다.
    /// 버튼은 활성 체크포인트 복원을 요청하고 복원이 시작되면 즉시 숨긴다.
    /// </summary>
    public sealed class RestartPanelView : MonoBehaviour
    {
        [SerializeField] private GameSession gameSession;
        [SerializeField] private CheckpointService checkpointService;
        [SerializeField] private PlayerCaptureDeathSystem captureDeathSystem;
        [SerializeField] private RectTransform buttonRoot;
        [SerializeField] private Button restartButton;

        /// <summary>필수 참조를 보완하고 임시 버튼을 준비한다.</summary>
        private void Awake()
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            checkpointService ??= FindFirstObjectByType<CheckpointService>();
            captureDeathSystem ??= FindFirstObjectByType<PlayerCaptureDeathSystem>();
            GameLog.Log(
                $"RestartPanelView 초기화. GameSession={gameSession?.name ?? "null"}, " +
                $"CheckpointService={checkpointService?.name ?? "null"}, " +
                $"CaptureDeathSystem={captureDeathSystem?.name ?? "null"}.",
                this);

            if (gameSession == null || checkpointService == null || captureDeathSystem == null)
            {
                GameLog.LogError("RestartPanelView의 진행 시스템 참조가 누락됐습니다.", this);
            }

            EnsureTemporaryButton();
            SetVisible(false);
        }

        /// <summary>사망 UI와 세션 단계 이벤트를 구독한다.</summary>
        private void OnEnable()
        {
            if (captureDeathSystem != null)
            {
                captureDeathSystem.DeathUiRequested -= HandleDeathUiRequested;
                captureDeathSystem.DeathUiRequested += HandleDeathUiRequested;
            }

            if (gameSession != null)
            {
                gameSession.PhaseChanged -= HandlePhaseChanged;
                gameSession.PhaseChanged += HandlePhaseChanged;
            }

            GameLog.Log(
                $"RestartPanelView 이벤트 구독 완료. CaptureSubscribed={captureDeathSystem != null}, " +
                $"PhaseSubscribed={gameSession != null}.",
                this);
        }

        /// <summary>사망 UI와 세션 단계 이벤트를 해제한다.</summary>
        private void OnDisable()
        {
            if (captureDeathSystem != null)
            {
                captureDeathSystem.DeathUiRequested -= HandleDeathUiRequested;
            }

            if (gameSession != null)
            {
                gameSession.PhaseChanged -= HandlePhaseChanged;
            }
        }

        /// <summary>사망 UI 요청을 받으면 중앙 Restart 버튼을 표시한다.</summary>
        private void HandleDeathUiRequested()
        {
            GameLog.Log(
                $"RestartPanelView가 DeathUiRequested를 받았습니다. " +
                $"Phase={gameSession?.Phase.ToString() ?? "null"}.",
                this);

            if (gameSession == null || gameSession.Phase == GameSessionPhase.Dead)
            {
                SetVisible(true);
            }
            else
            {
                GameLog.LogWarning("DeathUiRequested 시점의 GameSession Phase가 Dead가 아니어서 버튼을 표시하지 않았습니다.", this);
            }
        }

        /// <summary>복원 단계에 진입하면 Restart 버튼을 숨기고 Playing에서도 상태를 정리한다.</summary>
        private void HandlePhaseChanged(GameSessionPhase phase)
        {
            GameLog.Log($"RestartPanelView가 GameSession PhaseChanged를 받았습니다: {phase}.", this);
            if (phase != GameSessionPhase.Dead)
            {
                SetVisible(false);
            }
        }

        /// <summary>Restart 버튼 클릭을 활성 체크포인트 복원 요청으로 변환한다.</summary>
        private void HandleRestartClicked()
        {
            if (gameSession == null || gameSession.Phase != GameSessionPhase.Dead)
            {
                return;
            }

            checkpointService?.RestoreActiveCheckpoint();
        }

        /// <summary>임시 버튼의 표시 여부를 변경한다.</summary>
        private void SetVisible(bool value)
        {
            if (buttonRoot != null)
            {
                buttonRoot.gameObject.SetActive(value);
                GameLog.Log(
                    $"RestartPanelView 버튼 표시 상태 변경: {value}. " +
                    $"ButtonActive={buttonRoot.gameObject.activeInHierarchy}, " +
                    $"CanvasScale={buttonRoot.GetComponentInParent<Canvas>()?.transform.lossyScale.ToString() ?? "null"}.",
                    this);
            }
            else
            {
                GameLog.LogError("RestartPanelView 버튼 Root가 없어 표시 상태를 변경할 수 없습니다.", this);
            }
        }

        /// <summary>연결된 버튼이 없을 때 Canvas 중앙에 임시 Restart 버튼을 만든다.</summary>
        private void EnsureTemporaryButton()
        {
            if (restartButton != null)
            {
                restartButton.onClick.RemoveListener(HandleRestartClicked);
                restartButton.onClick.AddListener(HandleRestartClicked);
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            canvas ??= FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                GameLog.LogError("RestartPanelView가 사용할 Canvas를 찾지 못했습니다.", this);
                return;
            }

            GameLog.Log(
                $"RestartPanelView가 임시 버튼을 생성합니다. Canvas={canvas.name}, " +
                $"CanvasActive={canvas.gameObject.activeInHierarchy}, " +
                $"CanvasScale={canvas.transform.lossyScale}.",
                this);

            GameObject buttonObject = new GameObject(
                "Temporary Restart Button",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(canvas.transform, false);

            buttonRoot = buttonObject.GetComponent<RectTransform>();
            buttonRoot.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRoot.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRoot.pivot = new Vector2(0.5f, 0.5f);
            buttonRoot.anchoredPosition = Vector2.zero;
            buttonRoot.sizeDelta = new Vector2(260f, 80f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.2f, 0.05f, 0.05f, 0.96f);

            restartButton = buttonObject.GetComponent<Button>();
            restartButton.targetGraphic = image;
            restartButton.onClick.AddListener(HandleRestartClicked);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = "다시 시작";
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
        }

        /// <summary>컴포넌트를 처음 추가했을 때 진행 시스템과 사망 시스템을 연결한다.</summary>
        private void Reset()
        {
            gameSession = FindFirstObjectByType<GameSession>();
            checkpointService = FindFirstObjectByType<CheckpointService>();
            captureDeathSystem = FindFirstObjectByType<PlayerCaptureDeathSystem>();
        }
    }
}
