using System;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>특정 아이템을 획득했을 때 지정한 Story Flag를 기록한다.</summary>
    public sealed class StoryFlagOnItemAcquired : MonoBehaviour
    {
        [SerializeField] private ItemDefinition watchedItem;
        [SerializeField] private string flagId;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private GameSession gameSession;

        private void OnEnable()
        {
            ResolveReferences();
            if (inventory != null)
            {
                inventory.ItemAcquired -= HandleItemAcquired;
                inventory.ItemAcquired += HandleItemAcquired;
            }
        }

        private void OnDisable()
        {
            if (inventory != null)
            {
                inventory.ItemAcquired -= HandleItemAcquired;
            }
        }

        private void HandleItemAcquired(ItemDefinition item)
        {
            if (item == null || watchedItem == null || gameSession == null || string.IsNullOrWhiteSpace(flagId))
            {
                return;
            }

            if (string.Equals(item.ItemId, watchedItem.ItemId, StringComparison.Ordinal))
            {
                gameSession.SetStoryFlag(flagId);
            }
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            inventory ??= FindFirstObjectByType<PlayerInventory>();
            gameSession ??= FindFirstObjectByType<GameSession>();
        }
    }
}
