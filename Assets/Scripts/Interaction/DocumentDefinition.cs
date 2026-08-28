using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>일기장·게시판 조사 화면에 표시할 선택 이미지와 긴 본문을 보관한다.</summary>
    [CreateAssetMenu(fileName = "Document", menuName = "Haunted House/Interaction/Document")]
    public sealed class DocumentDefinition : ScriptableObject
    {
        [SerializeField] private Sprite backgroundImage;
        [SerializeField, TextArea(8, 30)] private string bodyText;

        public Sprite BackgroundImage => backgroundImage;
        public string BodyText => bodyText ?? string.Empty;
    }
}
