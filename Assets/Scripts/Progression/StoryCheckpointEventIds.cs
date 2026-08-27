namespace HauntedHouse.Progression
{
    /// <summary>
    /// 체크포인트 생성 시점을 알리는 스토리 사건의 고정 ID 모음이다.
    /// Room 진입만으로 표현할 수 없는 대화 완료 시점을 문자열 오타 없이 공유한다.
    /// </summary>
    public static class StoryCheckpointEventIds
    {
        public const string MasterKeyDialogueCompleted = nameof(MasterKeyDialogueCompleted);
    }
}
