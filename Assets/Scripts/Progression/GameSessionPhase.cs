namespace HauntedHouse.Progression
{
    /// <summary>
    /// 현재 세션이 월드 복원과 플레이 중 어떤 생명주기 단계에 있는지 나타낸다.
    /// 일반적인 게임플레이 모드를 표현하지 않고 입력과 시스템 실행을 잠그기 위한 상태만 제공한다.
    /// </summary>
    public enum GameSessionPhase
    {
        /// <summary>초기 체크포인트 또는 재시작 체크포인트를 적용하는 단계다.</summary>
        Restoring,

        /// <summary>플레이어 입력과 적 시스템이 정상적으로 동작하는 단계다.</summary>
        Playing,

        /// <summary>사망 후 Restart 입력만 허용하는 단계다.</summary>
        Ending,

        /// <summary>사망 후 Restart 입력만 허용하는 단계다.</summary>
        Dead
    }
}
