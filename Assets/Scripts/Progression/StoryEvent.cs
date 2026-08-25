using HauntedHouse.Commands;
using HauntedHouse.Inventory;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// GameSession이 외부에 전달하는 일회성 스토리 사건 데이터다.
    /// 사건의 의미를 해석하고 flag를 결정하는 책임은 CheckpointResolver에 있다.
    /// </summary>
    public readonly struct StoryEvent
    {
        /// <summary>사건 종류다.</summary>
        public StoryEventType Type { get; }

        /// <summary>방 진입 사건의 방 또는 관련 방이다.</summary>
        public Room Room { get; }

        /// <summary>아이템 획득 사건의 아이템이다.</summary>
        public ItemDefinition Item { get; }

        /// <summary>상호작용 완료 사건의 대상이다.</summary>
        public Target Target { get; }

        /// <summary>퍼즐 등 별도 식별자가 필요한 사건의 ID다.</summary>
        public string SubjectId { get; }

        /// <summary>
        /// 스토리 사건을 생성한다.
        /// 사용하지 않는 정보는 null로 전달해 사건 종류별 최소 데이터만 유지한다.
        /// </summary>
        public StoryEvent(
            StoryEventType type,
            Room room = null,
            ItemDefinition item = null,
            Target target = null,
            string subjectId = null)
        {
            Type = type;
            Room = room;
            Item = item;
            Target = target;
            SubjectId = subjectId;
        }
    }
}
