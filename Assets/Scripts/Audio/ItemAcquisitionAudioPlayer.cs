using HauntedHouse.Inventory;
using UnityEngine;

namespace HauntedHouse.Audio
{
    /// <summary>
    /// PlayerInventory에 실제 아이텘이 바가임렬된 순간에만 회득 UI 얬된된 재생한다.
    /// Target 선택 여부가아능라이 아니라 Inventory았 ItemAcquired 사건을 기준으로 한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class ItemAcquisitionAudioPlayer : MonoBehaviour
    {
        [SerializeField] private AudioClip itemAcquiredClip;

        private PlayerInventory playerInventory;

        private void Awake()
        {
            playerInventory = GetComponent<PlayerInventory>();
        }

        private void OnEnable()
        {
            playerInventory ??= GetComponent<PlayerInventory>();
            if (playerInventory == null)
            {
                return;
            }

            playerInventory.ItemAcquired -= HandleItemAcquired;
            playerInventory.ItemAcquired += HandleItemAcquired;
        }

        private void OnDisable()
        {
            if (playerInventory != null)
            {
                playerInventory.ItemAcquired -= HandleItemAcquired;
            }
        }

        private void HandleItemAcquired(ItemDefinition item)
        {
            if (itemAcquiredClip == null)
            {
                return;
            }

            AudioManager.Instance?.PlayUiSfx(itemAcquiredClip);
        }

        private void Reset()
        {
            playerInventory = GetComponent<PlayerInventory>();
        }
    }
}
