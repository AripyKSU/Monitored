namespace HauntedHouse.Progression
{
    /// <summary>
    /// 현재 스토리 진행 Core가 외부에 전달하는 사건의 종류다.
    /// 사망은 스토리 사건이 아니라 체크포인트 복원을 시작하는 별도 제어 신호이므로 포함하지 않는다.
    /// </summary>
    public enum StoryEventType
    {
        /// <summary>플레이어가 새로운 방에 논리적으로 진입한 사건이다.</summary>
        RoomEntered,

        /// <summary>고유 아이템을 획득한 사건이다.</summary>
        ItemAcquired,

        /// <summary>대상 행동이 성공적으로 완료된 사건이다.</summary>
        InteractionCompleted,

        /// <summary>퍼즐이 해결된 사건이다.</summary>
        PuzzleCompleted
    }
}
