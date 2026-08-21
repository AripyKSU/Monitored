using System;
using HauntedHouse.Inventory;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HauntedHouse.UI
{
    /// <summary>
    /// 인벤토리와 아이템 선택 패널에서 재사용하는 아이템 한 칸의 화면 표현이다.
    /// </summary>
    public sealed class InventoryItemView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private Text nameLabel;
        [SerializeField] private Button button;

        private UnityAction clickAction;

        /// <summary>
        /// 표시할 아이템과 선택 콜백을 연결한다. 콜백이 없으면 읽기 전용 칸으로 표시한다.
        /// </summary>
        public void Bind(ItemDefinition item, Action<ItemDefinition> selected)
        {
            ClearClickAction();

            if (iconImage != null)
            {
                iconImage.sprite = item != null ? item.Icon : null;
                iconImage.enabled = item != null && item.Icon != null;
            }

            if (nameLabel != null)
            {
                nameLabel.text = item != null ? item.DisplayName : string.Empty;
            }

            if (button == null)
            {
                return;
            }

            button.interactable = item != null && selected != null;
            if (button.interactable)
            {
                clickAction = () => selected(item);
                button.onClick.AddListener(clickAction);
            }
        }

        private void OnDestroy()
        {
            ClearClickAction();
        }

        private void ClearClickAction()
        {
            if (button != null && clickAction != null)
            {
                button.onClick.RemoveListener(clickAction);
            }

            clickAction = null;
        }

        private void Reset()
        {
            iconImage = GetComponentInChildren<Image>();
            nameLabel = GetComponentInChildren<Text>();
            button = GetComponent<Button>();
        }
    }
}
