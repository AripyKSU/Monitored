namespace HauntedHouse.Commands
{
    /// <summary>
    /// 한 Target 명령이 현재 어느 실행 단계에 있는지 나타낸다.
    /// 명령 예약이나 대기열은 지원하지 않으므로 세 상태만 사용한다.
    /// </summary>
    public enum TargetCommandState
    {
        /// <summary>새 Target 선택을 받을 수 있는 상태.</summary>
        Idle,

        /// <summary>플레이어가 Target의 접근 지점으로 이동하는 상태.</summary>
        Moving,

        /// <summary>도착 후 Target 고유 행동을 수행하는 상태.</summary>
        Executing
    }
}
