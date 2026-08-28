using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace HauntedHouse.UI
{
    /// <summary>
    /// 엔딩 결과와 제작 정보를 표시하고 기존 타이틀 씬으로 돌아가는 Credits UI를 관리한다.
    /// 34·35번 엔딩 흐름과의 연결은 외부에서 Show 메서드를 호출하는 방식으로 분리한다.
    /// </summary>
    public sealed class EndingCreditsView : MonoBehaviour
    {
        [Header("UI 구성")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset creditsTemplate;

        [Header("표시 문구")]
        [SerializeField] private string defaultProductionInfo = "Haunted House 제작팀\n감사합니다.";
        [SerializeField] private string titleSceneName = "MainMenu";

        private VisualElement creditsRoot;
        private Label endingNameLabel;
        private Label productionInfoLabel;
        private Button returnToTitleButton;
        private bool isReturningToTitle;
        private bool isUiReady;

        /// <summary>UIDocument와 재사용 가능한 Credits 템플릿을 준비한다.</summary>
        private void Awake()
        {
            uiDocument ??= GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }

            if (uiDocument.visualTreeAsset == null && creditsTemplate != null)
            {
                uiDocument.visualTreeAsset = creditsTemplate;
            }
        }

        /// <summary>UI 요소를 찾고 타이틀 복귀 버튼 이벤트를 연결한다.</summary>
        private void OnEnable()
        {
            EnsureUi();
            if (returnToTitleButton != null)
            {
                returnToTitleButton.clicked -= HandleReturnToTitleClicked;
                returnToTitleButton.clicked += HandleReturnToTitleClicked;
            }

            Hide();
        }

        /// <summary>컴포넌트가 비활성화될 때 UI 이벤트 연결을 해제한다.</summary>
        private void OnDisable()
        {
            if (returnToTitleButton != null)
            {
                returnToTitleButton.clicked -= HandleReturnToTitleClicked;
            }

            // UIDocument가 비활성화되면 다음 활성화 때 새 VisualElement 트리를 다시 조회한다.
            isUiReady = false;
            creditsRoot = null;
            endingNameLabel = null;
            productionInfoLabel = null;
            returnToTitleButton = null;
        }

        /// <summary>엔딩 이름과 기본 제작 정보를 Credits 화면에 표시한다.</summary>
        /// <param name="endingName">표시할 엔딩 이름이다. 비어 있으면 일반 Ending으로 표시한다.</param>
        public void Show(string endingName)
        {
            Show(endingName, defaultProductionInfo);
        }

        /// <summary>엔딩 이름과 제작 정보를 지정하여 Credits 화면을 표시한다.</summary>
        /// <param name="endingName">Bad Ending 또는 Happy Ending 등 표시할 엔딩 이름이다.</param>
        /// <param name="productionInfo">제작 정보 영역에 표시할 문구다.</param>
        public void Show(string endingName, string productionInfo)
        {
            EnsureUi();
            if (!isUiReady)
            {
                return;
            }

            // 외부 EndingController가 넘긴 결과를 UI 전용 문구로만 변환하고,
            // 엔딩 상태 변경이나 게임플레이 입력 제어는 호출자에게 맡긴다.
            endingNameLabel.text = string.IsNullOrWhiteSpace(endingName) ? "Ending" : endingName;
            productionInfoLabel.text = string.IsNullOrWhiteSpace(productionInfo)
                ? defaultProductionInfo
                : productionInfo;
            isReturningToTitle = false;
            returnToTitleButton.SetEnabled(true);
            creditsRoot.style.display = DisplayStyle.Flex;
        }

        /// <summary>Bad Ending 표시를 위한 편의 진입점이다.</summary>
        public void ShowBadEnding()
        {
            Show("Bad Ending");
        }

        /// <summary>Happy Ending 표시를 위한 편의 진입점이다.</summary>
        public void ShowHappyEnding()
        {
            Show("Happy Ending");
        }

        /// <summary>Credits 화면을 숨겨 다음 엔딩 이벤트를 받을 수 있는 상태로 되돌린다.</summary>
        public void Hide()
        {
            if (creditsRoot != null)
            {
                creditsRoot.style.display = DisplayStyle.None;
            }

            isReturningToTitle = false;
            returnToTitleButton?.SetEnabled(true);
        }

        /// <summary>Credits 화면이 현재 표시 중인지 반환한다.</summary>
        public bool IsVisible => creditsRoot != null && creditsRoot.resolvedStyle.display != DisplayStyle.None;

        /// <summary>연결된 UI 문서에서 필수 요소를 찾고, 템플릿이 없으면 런타임 대체 UI를 만든다.</summary>
        private void EnsureUi()
        {
            if (isUiReady || uiDocument == null)
            {
                return;
            }

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null)
            {
                GameLog.LogError("EndingCreditsView의 UI Toolkit rootVisualElement를 찾지 못했습니다.", this);
                return;
            }

            creditsRoot = root.Q<VisualElement>("EndingCreditsRoot");
            endingNameLabel = root.Q<Label>("EndingName");
            productionInfoLabel = root.Q<Label>("ProductionInfo");
            returnToTitleButton = root.Q<Button>("ReturnToTitleButton");

            // UXML을 아직 지정하지 않은 테스트 씬에서도 컴포넌트가 동작하도록
            // 최소한의 런타임 UI를 생성한다. 정식 화면은 creditsTemplate으로 지정한다.
            if (creditsRoot == null && uiDocument.visualTreeAsset == null)
            {
                BuildFallbackUi(root);
            }

            if (creditsRoot == null || endingNameLabel == null || productionInfoLabel == null ||
                returnToTitleButton == null)
            {
                GameLog.LogError(
                    "EndingCreditsView 필수 UI 요소가 누락됐습니다. " +
                    "EndingCredits.uxml의 이름을 확인하세요.",
                    this);
                return;
            }

            isUiReady = true;
        }

        /// <summary>UXML이 없는 임시 검증 환경을 위해 동일한 이름의 기본 UI를 구성한다.</summary>
        /// <param name="root">요소를 추가할 UI Toolkit 루트다.</param>
        private void BuildFallbackUi(VisualElement root)
        {
            creditsRoot = new VisualElement { name = "EndingCreditsRoot" };
            creditsRoot.style.flexGrow = 1f;
            creditsRoot.style.backgroundColor = new Color(0.015f, 0.02f, 0.035f, 0.96f);
            creditsRoot.style.alignItems = Align.Center;
            creditsRoot.style.justifyContent = Justify.Center;
            root.Add(creditsRoot);

            VisualElement card = new VisualElement { name = "CreditsCard" };
            card.style.width = 680f;
            card.style.minHeight = 420f;
            card.style.paddingLeft = 56f;
            card.style.paddingRight = 56f;
            card.style.paddingTop = 48f;
            card.style.paddingBottom = 48f;
            card.style.backgroundColor = new Color(0.07f, 0.09f, 0.13f, 0.98f);
            card.style.alignItems = Align.Center;
            creditsRoot.Add(card);

            Label heading = new Label("ENDING") { name = "EndingHeading" };
            heading.style.fontSize = 18f;
            heading.style.color = new Color(0.65f, 0.72f, 0.83f);
            card.Add(heading);

            endingNameLabel = new Label("Ending") { name = "EndingName" };
            endingNameLabel.style.fontSize = 42f;
            endingNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            endingNameLabel.style.color = new Color(0.95f, 0.78f, 0.35f);
            endingNameLabel.style.marginTop = 18f;
            card.Add(endingNameLabel);

            VisualElement divider = new VisualElement { name = "Divider" };
            divider.style.height = 2f;
            divider.style.width = 420f;
            divider.style.marginTop = 30f;
            divider.style.marginBottom = 30f;
            divider.style.backgroundColor = new Color(0.65f, 0.72f, 0.83f, 0.35f);
            card.Add(divider);

            productionInfoLabel = new Label { name = "ProductionInfo" };
            productionInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            productionInfoLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            productionInfoLabel.style.fontSize = 20f;
            productionInfoLabel.style.color = new Color(0.88f, 0.9f, 0.95f);
            card.Add(productionInfoLabel);

            returnToTitleButton = new Button { name = "ReturnToTitleButton", text = "타이틀로 돌아가기" };
            returnToTitleButton.style.width = 300f;
            returnToTitleButton.style.height = 58f;
            returnToTitleButton.style.marginTop = 42f;
            card.Add(returnToTitleButton);
        }

        /// <summary>타이틀 복귀 버튼을 한 번의 씬 전환 요청으로 제한한다.</summary>
        private void HandleReturnToTitleClicked()
        {
            if (isReturningToTitle)
            {
                return;
            }

            isReturningToTitle = true;
            returnToTitleButton.SetEnabled(false);

            if (!Application.CanStreamedLevelBeLoaded(titleSceneName))
            {
                GameLog.LogError(
                    $"EndingCreditsView가 타이틀 씬을 찾지 못했습니다: {titleSceneName}",
                    this);
                isReturningToTitle = false;
                returnToTitleButton.SetEnabled(true);
                return;
            }

            // Build Settings에 등록된 기존 MainMenu 씬을 이름으로 로드하여
            // 별도의 중복 타이틀 시스템을 만들지 않는다.
            SceneManager.LoadScene(titleSceneName);
        }

        /// <summary>컴포넌트를 처음 추가할 때 같은 오브젝트의 UIDocument를 자동으로 찾는다.</summary>
        private void Reset()
        {
            uiDocument = GetComponent<UIDocument>();
        }
    }
}
