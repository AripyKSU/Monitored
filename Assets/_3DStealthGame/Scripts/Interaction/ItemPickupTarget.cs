using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Inventory;
using HauntedHouse.UI;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// Target 도착 후 고유 아이템을 인벤토리에 추가하고 획득 메시지 뒤 월드 표현을 소모한다.
    /// </summary>
    public sealed class ItemPickupTarget : MonoBehaviour, ITargetAction
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private MessagePanel messagePanel;
        [SerializeField] private GameObject worldObject;
        [SerializeField, TextArea] private string pickupMessage;

        public IEnumerator Execute(TargetActionContext context)
        {
            if (context?.Target == null || item == null || inventory == null || messagePanel == null)
            {
                GameLog.LogError($"ItemPickupTarget '{name}'의 필수 참조가 누락됐습니다.", this);
                yield break;
            }

            if (!inventory.TryAdd(item))
            {
                GameLog.LogError(
                    $"ItemPickupTarget '{name}'이 이미 보유 중이거나 유효하지 않은 아이템 '{item.name}'을 추가하려 했습니다.",
                    this);
                yield break;
            }

            string message = string.IsNullOrWhiteSpace(pickupMessage)
                ? $"{item.DisplayName}을(를) 획득했습니다."
                : pickupMessage;
            yield return messagePanel.Show(message);

            // 비활성화 전에 선택 상태를 먼저 닫아 같은 프레임의 재선택 가능성도 제거한다.
            context.Target.SetSelectable(false);
            GameObject consumedObject = worldObject != null ? worldObject : gameObject;
            consumedObject.SetActive(false);
        }

        private void Reset()
        {
            inventory = FindFirstObjectByType<PlayerInventory>();
            messagePanel = FindFirstObjectByType<MessagePanel>();
            worldObject = gameObject;
        }
    }
}
