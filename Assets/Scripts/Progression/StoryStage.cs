namespace HauntedHouse.Progression
{
    /// <summary>
    /// 현재 플레이 세션의 큰 스토리 진행 구간이다.
    /// GameSession이 런타임 상태를 소유하고 CheckpointDefinition이 복원 기준을 제공한다.
    /// </summary>
    public enum StoryStage
    {
        None = 0,
        Sector1 = 1,
        Sector2 = 2,
        Sector3 = 3,
        Sector4 = 4,
        Sector5Ready = 5,
        FinalChase = 6,
        Ending = 7
    }
}
