using System.Collections.Generic;
using HauntedHouse.Core;
using HauntedHouse.Inventory;
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
        [SerializeField] private GamePauseController pauseController;
        [SerializeField] private bool pauseWhileOpen = true;

        [Header("화면 구성")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform itemContainer;
        [SerializeField] private InventoryItemView itemViewPrefab;
        [SerializeField] private Text descriptionLabel;
        [SerializeField] private Text emptyLabel;

        private readonly List<InventoryItemView> spawnedViews = new();
        private GamePauseController.PauseHandle pauseHandle;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            inventory ??= FindFirstObjectByType<PlayerInventory>();
            pauseController ??= FindFirstObjectByType<GamePauseController>();

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
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
                if (pauseController == null)
                {
                    GameLog.LogError($"InventoryPanel '{name}'에 GamePauseController가 연결되지 않았습니다.", this);
                    return;
                }

                pauseHandle = pauseController.AcquirePause(this);
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
            pauseController = FindFirstObjectByType<GamePauseController>();
        }
    }
}
