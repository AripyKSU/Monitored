using System;
using System.Collections.Generic;
using HauntedHouse.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace HauntedHouse.UI
{
    /// <summary>
    /// 현재 보유 아이템 목록을 모달 선택지로 표시하고 선택 또는 취소 결과만 전달한다.
    /// 정답 아이템과 대상 효과는 알지 못한다.
    /// </summary>
    public sealed class ItemSelectionPanel : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform itemContainer;
        [SerializeField] private InventoryItemView itemViewPrefab;
        [SerializeField] private Text emptyLabel;

        private readonly List<InventoryItemView> spawnedViews = new();

        public bool IsOpen { get; private set; }

        public event Action<ItemDefinition> ItemSelected;
        public event Action Cancelled;

        private void Awake()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        public bool Open(IReadOnlyList<ItemDefinition> items)
        {
            if (panelRoot == null || itemContainer == null || itemViewPrefab == null || items == null)
            {
                GameLog.LogError($"ItemSelectionPanel '{name}'의 필수 참조가 누락됐습니다.", this);
                return false;
            }

            Close();
            IsOpen = true;
            panelRoot.SetActive(true);

            if (emptyLabel != null)
            {
                emptyLabel.gameObject.SetActive(items.Count == 0);
            }

            foreach (ItemDefinition item in items)
            {
                InventoryItemView view = Instantiate(itemViewPrefab, itemContainer);
                view.Bind(item, Select);
                spawnedViews.Add(view);
            }

            return true;
        }

        public void Cancel()
        {
            if (!IsOpen)
            {
                return;
            }

            Close();
            Cancelled?.Invoke();
        }

        public void Close()
        {
            IsOpen = false;
            ClearViews();

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        private void Select(ItemDefinition item)
        {
            if (!IsOpen || item == null)
            {
                return;
            }

            Close();
            ItemSelected?.Invoke(item);
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
    }
}
