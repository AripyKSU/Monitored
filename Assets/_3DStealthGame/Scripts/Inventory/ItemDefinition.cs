using UnityEngine;

namespace HauntedHouse.Inventory
{
    /// <summary>
    /// 고유 아이템의 식별자와 화면 표시 데이터만 보관하는 정적 정의다.
    /// 수량, 조합, 사용 효과는 소유하지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDefinition", menuName = "Haunted House/Item Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private Sprite icon;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;

        /// <summary>
        /// 런타임 인벤토리에 등록할 수 있는 최소 데이터가 있는지 검사한다.
        /// </summary>
        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                error = $"ItemDefinition '{name}'의 ItemId가 비어 있습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                error = $"ItemDefinition '{name}'의 표시 이름이 비어 있습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
