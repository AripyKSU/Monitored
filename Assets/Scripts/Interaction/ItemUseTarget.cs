using System;
using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Core;
using HauntedHouse.Inventory;
using HauntedHouse.UI;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// Target 도착 후 게임을 일시정지하고 보유 아이템 선택, 정답 검사와 대상 효과 실행을 담당한다.
    /// </summary>
    public sealed class ItemUseTarget : MonoBehaviour, ITargetAction
    {
        [Header("사용 조건")]
        [SerializeField] private ItemDefinition requiredItem;
        [Tooltip("정답 아이템 사용 성공 시 인벤토리에서 제거할지 결정한다. 열쇠·손전등처럼 유지해야 하는 아이템은 끈다.")]
        [SerializeField] private bool consumeItemOnSuccess = true;
        [Tooltip("IItemUseEffect를 구현한 MonoBehaviour를 연결한다.")]
        [SerializeField] private MonoBehaviour effectComponent;

        [Header("공용 시스템")]
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private GamePauseService pauseService;
        [SerializeField] private ItemSelectionPanel selectionPanel;
        [SerializeField] private MessagePanel messagePanel;

        [Header("결과 메시지")]
        [SerializeField, TextArea] private string successMessage;
        [SerializeField, TextArea] private string failureMessage = "이 아이템은 사용할 수 없습니다.";

        private GamePauseService.PauseHandle pauseHandle;

        /// <summary>마지막 아이템 선택 실행의 최종 결과다.</summary>
        public ItemUseResult LastResult { get; private set; } = ItemUseResult.None;

        /// <summary>아이템 사용 결과가 확정될 때 결과와 선택 아이템을 전달한다.</summary>
        public event Action<ItemUseResult, ItemDefinition> ResultResolved;

        public IEnumerator Execute(TargetActionContext context)
        {
            LastResult = ItemUseResult.None;

            if (!TryValidateConfiguration(out string error))
            {
                GameLog.LogError(error, this);
                yield break;
            }

            ItemDefinition selectedItem = null;
            bool selectionFinished = false;

            void HandleSelected(ItemDefinition item)
            {
                selectedItem = item;
                selectionFinished = true;
            }

            void HandleCancelled()
            {
                selectionFinished = true;
            }

            pauseHandle = pauseService.AcquirePause(this);
            selectionPanel.ItemSelected += HandleSelected;
            selectionPanel.Cancelled += HandleCancelled;

            try
            {
                if (!selectionPanel.Open(inventory.Items))
                {
                    yield break;
                }

                while (!selectionFinished && selectionPanel.IsOpen)
                {
                    yield return null;
                }

                if (selectedItem == null)
                {
                    ResolveResult(ItemUseResult.Cancel, null);
                    yield break;
                }

                if (!MatchesRequiredItem(selectedItem))
                {
                    yield return messagePanel.Show(failureMessage);
                    ResolveResult(ItemUseResult.WrongItem, selectedItem);
                    yield break;
                }

                IItemUseEffect effect = effectComponent as IItemUseEffect;
                if (consumeItemOnSuccess && !inventory.TryRemove(selectedItem))
                {
                    GameLog.LogError(
                        $"선택한 아이템 '{selectedItem.ItemId}'을 PlayerInventory에서 제거하지 못했습니다.",
                        this);
                    yield break;
                }

                effect.ApplyItemEffect(context);
                context.MarkCompleted();

                if (!string.IsNullOrWhiteSpace(successMessage))
                {
                    yield return messagePanel.Show(successMessage);
                }

                ResolveResult(ItemUseResult.CorrectItem, selectedItem);
            }
            finally
            {
                selectionPanel.ItemSelected -= HandleSelected;
                selectionPanel.Cancelled -= HandleCancelled;
                selectionPanel.Close();
                ReleasePause();
            }
        }

        /// <summary>아이템 사용 결과를 한 번 기록하고 외부 흐름에 전달한다.</summary>
        /// <param name="result">확정된 선택 결과.</param>
        /// <param name="selectedItem">선택한 아이템. 취소인 경우 null.</param>
        private void ResolveResult(ItemUseResult result, ItemDefinition selectedItem)
        {
            LastResult = result;
            ResultResolved?.Invoke(result, selectedItem);
        }

        public bool TryValidateConfiguration(out string error)
        {
            if (requiredItem == null)
            {
                error = $"ItemUseTarget '{name}'에 RequiredItem이 연결되지 않았습니다.";
                return false;
            }

            if (!requiredItem.TryValidate(out error))
            {
                return false;
            }

            if (effectComponent is not IItemUseEffect)
            {
                error = $"ItemUseTarget '{name}'의 EffectComponent가 IItemUseEffect를 구현하지 않았습니다.";
                return false;
            }

            if (inventory == null || pauseService == null || selectionPanel == null || messagePanel == null)
            {
                error = $"ItemUseTarget '{name}'의 인벤토리, 일시정지 또는 UI 참조가 누락됐습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool MatchesRequiredItem(ItemDefinition selectedItem)
        {
            return selectedItem != null &&
                   string.Equals(selectedItem.ItemId, requiredItem.ItemId, StringComparison.Ordinal);
        }

        private void Reset()
        {
            inventory = FindFirstObjectByType<PlayerInventory>();
            pauseService = FindFirstObjectByType<GamePauseService>();
            selectionPanel = FindFirstObjectByType<ItemSelectionPanel>();
            messagePanel = FindFirstObjectByType<MessagePanel>();
        }

        /// <summary>대상 실행이 중단되는 경우에도 아이템 선택이 보유한 일시정지를 해제한다.</summary>
        private void OnDisable()
        {
            ReleasePause();
        }

        /// <summary>현재 대상 실행이 획득한 일시정지 Handle을 안전하게 반환한다.</summary>
        private void ReleasePause()
        {
            pauseHandle?.Dispose();
            pauseHandle = null;
        }
    }
}
