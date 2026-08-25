using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// Target 도착 후 고유 아이템을 인벤토리에 추가하고 획득 메시지 뒤 월드 표현을 소모한다.
    /// </summary>
    public sealed class ItemPickupTarget : MonoBehaviour, ITargetAction, ICompletedState
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private MessagePanel messagePanel;
        [SerializeField] private GameObject worldObject;
        [SerializeField, TextArea] private string pickupMessage;

        /// <summary>아이템 획득 상태를 저장할 고유 ID다.</summary>
        [SerializeField] private string completionId;

        /// <summary>현재 아이템이 획득되어 월드에서 소모됐는지 반환한다.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>체크포인트에서 사용할 완료 상태 ID다.</summary>
        public string CompletionId => string.IsNullOrWhiteSpace(completionId) ? name : completionId;

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
            IsCompleted = true;
            context.MarkCompleted();
        }

        /// <summary>
        /// 체크포인트의 아이템 획득 여부를 월드 오브젝트와 Target 선택 상태에 반영한다.
        /// </summary>
        /// <param name="completed">획득 완료 상태로 만들려면 true.</param>
        public void RestoreCompletedState(bool completed)
        {
            IsCompleted = completed;
            Target target = GetComponent<Target>();
            target?.SetSelectable(!completed);

            GameObject consumedObject = worldObject != null ? worldObject : gameObject;
            if (consumedObject != null)
            {
                consumedObject.SetActive(!completed);
            }
        }

        private void Reset()
        {
            inventory = FindFirstObjectByType<PlayerInventory>();
            messagePanel = FindFirstObjectByType<MessagePanel>();
            worldObject = gameObject;
            completionId = name;
        }
    }
}
