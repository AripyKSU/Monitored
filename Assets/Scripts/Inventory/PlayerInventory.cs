using System;
using System.Collections.Generic;
using UnityEngine;

namespace HauntedHouse.Inventory
{
    /// <summary>
    /// 플레이어가 현재 보유한 고유 아이템 목록을 관리한다.
    /// 아이템 동일성은 ScriptableObject 참조가 아니라 ItemId를 기준으로 판단한다.
    /// </summary>
    public sealed class PlayerInventory : MonoBehaviour
    {
        [SerializeField] private ItemDefinition[] initialItems = Array.Empty<ItemDefinition>();

        private readonly List<ItemDefinition> items = new();

        public IReadOnlyList<ItemDefinition> Items => items;

        /// <summary>
        /// 아이템 추가, 제거 또는 전체 초기화 후 UI와 진행 시스템에 변경을 알린다.
        /// </summary>
        public event Action Changed;

        /// <summary>
        /// 일반 플레이 중 고유 아이템을 새로 획득했을 때 아이템 자체를 전달한다.
        /// 체크포인트 복원은 진행 사건으로 취급하지 않으므로 이 이벤트를 발생시키지 않는다.
        /// </summary>
        public event Action<ItemDefinition> ItemAcquired;

        private void Awake()
        {
            items.Clear();

            if (initialItems == null)
            {
                return;
            }

            foreach (ItemDefinition item in initialItems)
            {
                if (!TryAddInternal(item, out string error))
                {
                    GameLog.LogError(error, this);
                }
            }
        }

        /// <summary>
        /// 아직 보유하지 않은 유효한 고유 아이템을 추가한다.
        /// </summary>
        public bool TryAdd(ItemDefinition item)
        {
            if (!TryAddInternal(item, out _))
            {
                return false;
            }

            Changed?.Invoke();
            ItemAcquired?.Invoke(item);
            return true;
        }

        /// <summary>
        /// 같은 ItemId를 가진 아이템을 현재 보유하고 있는지 확인한다.
        /// </summary>
        public bool Contains(ItemDefinition item)
        {
            return item != null && IndexOf(item.ItemId) >= 0;
        }

        /// <summary>
        /// 같은 ItemId를 가진 아이템 하나를 제거한다.
        /// </summary>
        public bool TryRemove(ItemDefinition item)
        {
            if (item == null)
            {
                return false;
            }

            int index = IndexOf(item.ItemId);
            if (index < 0)
            {
                return false;
            }

            items.RemoveAt(index);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 체크포인트 정의의 고유 아이템 목록으로 현재 보유 목록을 교체한다.
        /// 복원 중에는 아이템 획득 사건을 발행하지 않고 UI 변경만 한 번 알린다.
        /// </summary>
        /// <param name="restoredItems">복원할 아이템 목록.</param>
        public void RestoreItems(IEnumerable<ItemDefinition> restoredItems)
        {
            List<ItemDefinition> replacement = new();
            if (restoredItems != null)
            {
                foreach (ItemDefinition item in restoredItems)
                {
                    if (item == null)
                    {
                        GameLog.LogError($"PlayerInventory '{name}'의 복원 목록에 빈 아이템이 있습니다.", this);
                        continue;
                    }

                    if (replacement.Exists(existing =>
                            string.Equals(existing.ItemId, item.ItemId, StringComparison.Ordinal)))
                    {
                        GameLog.LogError(
                            $"PlayerInventory '{name}'의 복원 목록에 ItemId '{item.ItemId}'가 중복됩니다.",
                            this);
                        continue;
                    }

                    if (!item.TryValidate(out string error))
                    {
                        GameLog.LogError(error, this);
                        continue;
                    }

                    replacement.Add(item);
                }
            }

            items.Clear();
            items.AddRange(replacement);
            Changed?.Invoke();
        }

        private bool TryAddInternal(ItemDefinition item, out string error)
        {
            if (item == null)
            {
                error = $"PlayerInventory '{name}'에 빈 아이템을 추가할 수 없습니다.";
                return false;
            }

            if (!item.TryValidate(out error))
            {
                return false;
            }

            if (IndexOf(item.ItemId) >= 0)
            {
                error = $"PlayerInventory '{name}'에 ItemId '{item.ItemId}'가 이미 존재합니다.";
                return false;
            }

            items.Add(item);
            error = string.Empty;
            return true;
        }

        private int IndexOf(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return -1;
            }

            for (int index = 0; index < items.Count; index++)
            {
                ItemDefinition item = items[index];
                if (item != null && string.Equals(item.ItemId, itemId, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
