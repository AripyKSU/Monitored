using System.Collections.Generic;
using HauntedHouse.Core;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace HauntedHouse.UI
{
    /// <summary>
    /// 보유 아이템의 아이콘, 이름과 설명을 열람하는 패널이다.
    /// 아이템 소비와 사용 가능 여부는 판단하지 않는다.
    /// </summary>
    public sealed class InventoryPanel : MonoBehaviour
    {
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private GamePauseService pauseService;
        [SerializeField] private bool pauseWhileOpen = true;
        [SerializeField] private GameSession gameSession;

        [Header("화면 구성")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform itemContainer;
        [SerializeField] private InventoryItemView itemViewPrefab;
        [SerializeField] private Text descriptionLabel;
        [SerializeField] private Text emptyLabel;

        private readonly List<InventoryItemView> spawnedViews = new();
        private GamePauseService.PauseHandle pauseHandle;

        private const int MaxItemsPerRow = 3;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            inventory ??= FindFirstObjectByType<PlayerInventory>();
            pauseService ??= FindFirstObjectByType<GamePauseService>();
            gameSession ??= FindFirstObjectByType<GameSession>();

            ConfigureItemGrid();

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        /// <summary>인벤토리 아이템을 한 줄에 최대 세 개씩 배치하도록 컨테이너의 Grid 설정을 보장한다.</summary>
        private void ConfigureItemGrid()
        {
            if (itemContainer == null)
            {
                return;
            }

            // 기존 한 줄 전용 HorizontalLayoutGroup은 Grid와 동시에 자식을 배치할 수 없으므로 런타임에서 교체한다.
            HorizontalLayoutGroup horizontal = itemContainer.GetComponent<HorizontalLayoutGroup>();
            if (horizontal != null)
            {
                // LayoutGroup에는 중복 방지 특성이 있어 지연 Destroy 상태에서는 Grid 추가가 거부된다.
                DestroyImmediate(horizontal);
            }

            GridLayoutGroup grid = itemContainer.GetComponent<GridLayoutGroup>();
            if (grid == null)
            {
                grid = itemContainer.gameObject.AddComponent<GridLayoutGroup>();
            }

            RectTransform itemRect = itemViewPrefab != null ? itemViewPrefab.GetComponent<RectTransform>() : null;
            if (itemRect != null)
            {
                grid.cellSize = itemRect.rect.size;
            }

            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = MaxItemsPerRow;

            // 두 번째 줄이 기존 130px 컨테이너에 잘리지 않도록 실제 셀 높이를 기준으로 세로 공간을 확보한다.
            if (itemContainer is RectTransform containerRect)
            {
                float twoRowHeight = grid.cellSize.y * 2f + grid.spacing.y;
                containerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, twoRowHeight);
            }
        }

        private void OnEnable()
        {
            if (inventory != null)
            {
                inventory.Changed -= Refresh;
                inventory.Changed += Refresh;
            }
        }

        private void OnDisable()
        {
            if (inventory != null)
            {
                inventory.Changed -= Refresh;
            }

            ReleasePause();
        }

        public void Open()
        {
            if (gameSession != null && !gameSession.IsGameplayInputEnabled)
            {
                return;
            }

            if (panelRoot == null || inventory == null || itemContainer == null || itemViewPrefab == null)
            {
                GameLog.LogError($"InventoryPanel '{name}'의 필수 참조가 누락됐습니다.", this);
                return;
            }

            if (IsOpen)
            {
                return;
            }

            if (pauseWhileOpen)
            {
                if (pauseService == null)
                {
                    GameLog.LogError($"InventoryPanel '{name}'에 GamePauseService가 연결되지 않았습니다.", this);
                    return;
                }

                pauseHandle = pauseService.AcquirePause(this);
            }

            panelRoot.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            ReleasePause();
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Refresh()
        {
            ClearViews();

            if (inventory == null || itemContainer == null || itemViewPrefab == null)
            {
                return;
            }

            IReadOnlyList<ItemDefinition> items = inventory.Items;
            if (emptyLabel != null)
            {
                emptyLabel.gameObject.SetActive(items.Count == 0);
            }

            if (descriptionLabel != null)
            {
                descriptionLabel.text = string.Empty;
            }

            foreach (ItemDefinition item in items)
            {
                InventoryItemView view = Instantiate(itemViewPrefab, itemContainer);
                view.Bind(item, ShowDescription);
                spawnedViews.Add(view);
            }
        }

        private void ShowDescription(ItemDefinition item)
        {
            if (descriptionLabel != null)
            {
                descriptionLabel.text = item != null ? item.Description : string.Empty;
            }
        }

        private void ClearViews()
        {
            foreach (InventoryItemView view in spawnedViews)
            {
                if (view != null)
                {
                    Destroy(view.gameObject);
                }
            }

            spawnedViews.Clear();
        }

        private void ReleasePause()
        {
            pauseHandle?.Dispose();
            pauseHandle = null;
        }

        private void Reset()
        {
            inventory = FindFirstObjectByType<PlayerInventory>();
            pauseService = FindFirstObjectByType<GamePauseService>();
            gameSession = FindFirstObjectByType<GameSession>();
        }
    }
}
